#!/usr/bin/env python3
"""baseline_load.py — paced, multi-device sustained load for the P0 G7/G8 baseline.

This measures CLIENT-SIDE latency and sustained throughput only. Transaction p95,
commit time and counter lock wait are NOT measurable from here and are deliberately
absent: they come from the PostgreSQL log and pg_locks (plan Task 6 Steps 6 and 8).
Reporting a client number under a server-side name is exactly the defect this
harness exists to avoid.

Rate limits this must stay under (IngressRateLimitMiddleware.cs):
  IP bucket     600 permits / 1 min fixed window  -> keep total under 10 req/s
  device bucket 240 permits / 1 min, per device   -> spread across devices
  enrollment     10 permits / 1 min, per IP       -> enroll once, paced, reuse
"""
import argparse, json, random, statistics, string, sys, threading, time, urllib.error, urllib.request

def call(base, method, path, token=None, body=None, extra=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(base.rstrip("/") + path, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    for k, v in (extra or {}).items():
        req.add_header(k, v)
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            payload = resp.read().decode("utf-8", "replace")
            return resp.status, payload, (time.perf_counter() - started) * 1000
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace"), (time.perf_counter() - started) * 1000
    except Exception as e:                                   # noqa: BLE001
        return 0, repr(e), (time.perf_counter() - started) * 1000

def enroll(base, site_code, assertion, index):
    """Enrol one device. 429 here means the 10/min enrollment window is full:
    back off and retry rather than silently running with fewer devices."""
    for attempt in range(6):
        status, body, _ = call(base, "POST", "/api/v1/devices/enroll", token=assertion,
                               body={"siteCode": site_code, "deviceName": f"bench-{index}", "role": "operator"})
        if status == 201:
            parsed = json.loads(body)
            return parsed["deviceToken"], parsed["siteId"]
        if status == 429:
            wait = 15 * (attempt + 1)
            print(f"enroll {site_code}: 429, backing off {wait}s", file=sys.stderr)
            time.sleep(wait)
            continue
        raise SystemExit(f"enroll {site_code} failed: HTTP {status} {body}")
    raise SystemExit(f"enroll {site_code} still rate-limited after 6 attempts")

def acquire_lease(base, site_id, token):
    status, body, _ = call(base, "POST", f"/api/v1/sites/{site_id}/lease/acquire", token=token)
    if status != 200:
        raise SystemExit(f"lease acquire failed for {site_id}: HTTP {status} {body}")
    return json.loads(body)["leaderTerm"]

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--base", required=True)
    p.add_argument("--sites", required=True, help="comma-separated site codes")
    p.add_argument("--assertions-file", required=True, help="one assertion per line, same order as --sites")
    p.add_argument("--mode", choices=["interactive", "bulk"], required=True)
    p.add_argument("--concurrency", type=int, default=10)
    p.add_argument("--duration-seconds", type=int, default=120)
    p.add_argument("--target-rps", type=float, default=8.0)
    args = p.parse_args()

    site_codes = [s.strip() for s in args.sites.split(",") if s.strip()]
    assertions = [l.strip() for l in open(args.assertions_file, encoding="utf-8") if l.strip()]
    if len(assertions) != len(site_codes):
        raise SystemExit(f"{len(site_codes)} sites but {len(assertions)} assertions")

    devices = []
    for i, (code, assertion) in enumerate(zip(site_codes, assertions)):
        token, site_id = enroll(args.base, code, assertion, i)
        term = acquire_lease(args.base, site_id, token) if args.mode == "bulk" else None
        devices.append({"site_id": site_id, "token": token, "term": term})
        time.sleep(7)                       # stay inside the 10/min enrollment window
    print(f"prepared {len(devices)} devices", file=sys.stderr)

    endpoint = "jms/ingest" if args.mode == "bulk" else "jms/observations"
    scan_date = time.strftime("%Y-%m-%d", time.gmtime())
    # Fleet rate = concurrency / interval, so the interval that makes the FLEET hit
    # --target-rps is concurrency/target_rps -- NOT devices/target_rps. With 10 devices
    # and 50 workers the latter paces to 50*8/10 = 40 req/s, four times over the 600/min
    # IP bucket, and the run drowns in 429s instead of measuring anything.
    interval = args.concurrency / args.target_rps if args.target_rps > 0 else 0
    lock = threading.Lock()
    latencies, counts = [], {"sent": 0, "ok": 0, "http_429": 0, "http_409": 0, "other_errors": 0}
    deadline = time.time() + args.duration_seconds

    def worker(slot):
        alphabet = string.ascii_uppercase + string.digits
        while time.time() < deadline:
            cycle = time.perf_counter()
            dev = devices[slot % len(devices)]
            suffix = "".join(random.choices(alphabet, k=10))
            headers = {"Idempotency-Key": f"bench-{suffix}"}       # 16 chars: inside the 8..128 rule
            if dev["term"] is not None:
                headers["X-Leader-Term"] = str(dev["term"])
            body = {"items": [{"waybillNo": f"BENCH-{suffix}", "scanTime": f"{scan_date} 10:00:00",
                               "code": 110, "status": "Arrived", "scanTypeName": "state_transition",
                               "payload": {"src": "baseline"}}]}
            status, text, ms = call(args.base, "POST",
                                    f"/api/v1/sites/{dev['site_id']}/{endpoint}",
                                    token=dev["token"], body=body, extra=headers)
            with lock:
                counts["sent"] += 1
                if status == 200:
                    counts["ok"] += 1
                    latencies.append(ms)
                elif status == 429:
                    counts["http_429"] += 1
                elif status == 409:
                    counts["http_409"] += 1
                else:
                    counts["other_errors"] += 1
                    if counts["other_errors"] <= 3:
                        print(f"HTTP {status}: {text[:200]}", file=sys.stderr)
            # Pace so the FLEET hits --target-rps. Without this the run measures how
            # fast a burst drains, which is not a sustained throughput at all.
            slack = interval - (time.perf_counter() - cycle)
            if slack > 0:
                time.sleep(slack)

    started = time.time()
    threads = [threading.Thread(target=worker, args=(i,)) for i in range(args.concurrency)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    elapsed = time.time() - started

    def pct(values, q):
        return round(statistics.quantiles(values, n=100)[q - 1], 1) if len(values) > 100 else None

    print(json.dumps({
        "mode": args.mode, "concurrency": args.concurrency, "devices": len(devices),
        "duration_seconds": round(elapsed, 1), **counts,
        "sustained_rps": round(counts["sent"] / elapsed, 1),
        "client_latency_p50_ms": pct(latencies, 50),
        "client_latency_p95_ms": pct(latencies, 95),
        "client_latency_p99_ms": pct(latencies, 99),
        "note": "client-side latency only; server p95 comes from the postgres log",
    }, indent=2))

if __name__ == "__main__":
    main()
