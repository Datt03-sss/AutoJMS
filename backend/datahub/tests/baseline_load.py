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

In bulk mode the lease has to be renewed while the load runs: LeaseRepository sets
LeaseDurationSeconds = 120, which a 120 s run reaches exactly. Renewal traffic is
overhead, not load -- it is counted in lease_renewals / lease_renew_failures and is
deliberately kept out of sent / sustained_rps.
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

def renew_lease(base, site_id, token, term):
    """Extend one lease by LeaseDurationSeconds without changing its term.
    LeaseRepository.RenewAsync keeps leader_term and sets lease_expires_at = now + 120s,
    so renewing every RenewIntervalSeconds holds the lease for the whole run. This is
    the contract the desktop client follows -- see the DataHubClient.cs header comment
    on POST /lease/renew: "body { leaderTerm }; the term does NOT change"."""
    status, body, _ = call(base, "POST", f"/api/v1/sites/{site_id}/lease/renew",
                           token=token, body={"leaderTerm": term})
    return status, body

# LeaseRepository.RenewIntervalSeconds. One renew per device per 30s is ~4 requests per
# device per run: 40 requests against the 600/min IP bucket and 2/min against the
# 240/min device bucket, i.e. negligible next to the load itself.
RENEW_INTERVAL_SECONDS = 30

def renew_leases_until(base, devices, deadline, counters, lock, stop):
    """Daemon loop: renew every lease every RENEW_INTERVAL_SECONDS until the run's
    deadline. A failed renewal is a real finding (the lease was lost or fenced), so it
    is counted and printed, never swallowed."""
    next_at = time.time() + RENEW_INTERVAL_SECONDS
    while True:
        wait = min(next_at, deadline) - time.time()
        if wait > 0 and stop.wait(wait):
            return
        if time.time() >= deadline or stop.is_set():
            return
        next_at += RENEW_INTERVAL_SECONDS
        for dev in devices:
            if dev["term"] is None:
                continue
            status, body = renew_lease(base, dev["site_id"], dev["token"], dev["term"])
            with lock:
                if status == 200:
                    counters["lease_renewals"] += 1
                else:
                    counters["lease_renew_failures"] += 1
                    if counters["lease_renew_failures"] <= 3:
                        print(f"lease renew failed for {dev['site_id']}: HTTP {status} {body[:200]}",
                              file=sys.stderr)

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
        devices.append({"site_id": site_id, "token": token, "term": None})
        time.sleep(7)                       # stay inside the 10/min enrollment window
    print(f"enrolled {len(devices)} devices", file=sys.stderr)

    # Leases are taken only AFTER every device is enrolled. Acquiring inside the loop
    # above burned up to ~70s of the 120s lease on the enrollment pacing sleeps, so the
    # earliest sites' leases expired part-way through a 120s run and the load collected
    # 409 leader_fenced. Acquire late, then renew on a timer: every lease starts fresh.
    # interactive mode takes no lease at all and leaves every term None, so nothing
    # below this line runs for it.
    if args.mode == "bulk":
        for dev in devices:
            dev["term"] = acquire_lease(args.base, dev["site_id"], dev["token"])
        print(f"acquired {len(devices)} leases", file=sys.stderr)

    endpoint = "jms/ingest" if args.mode == "bulk" else "jms/observations"
    scan_date = time.strftime("%Y-%m-%d", time.gmtime())
    # Fleet rate = concurrency / interval, so the interval that makes the FLEET hit
    # --target-rps is concurrency/target_rps -- NOT devices/target_rps. With 10 devices
    # and 50 workers the latter paces to 50*8/10 = 40 req/s, four times over the 600/min
    # IP bucket, and the run drowns in 429s instead of measuring anything.
    interval = args.concurrency / args.target_rps if args.target_rps > 0 else 0
    lock = threading.Lock()
    latencies, counts = [], {"sent": 0, "ok": 0, "http_429": 0, "http_409": 0, "other_errors": 0}
    # Kept in their own dict, not in counts, so renewal overhead can never leak into
    # sent or sustained_rps.
    lease_counts = {"lease_renewals": 0, "lease_renew_failures": 0}

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
    deadline = started + args.duration_seconds
    stop = threading.Event()
    renewer = None
    if args.mode == "bulk":
        renewer = threading.Thread(target=renew_leases_until,
                                   args=(args.base, devices, deadline, lease_counts, lock, stop),
                                   daemon=True)
        renewer.start()
    threads = [threading.Thread(target=worker, args=(i,)) for i in range(args.concurrency)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    stop.set()
    if renewer is not None:
        renewer.join(timeout=35)
    elapsed = time.time() - started

    def pct(values, q):
        return round(statistics.quantiles(values, n=100)[q - 1], 1) if len(values) > 100 else None

    print(json.dumps({
        "mode": args.mode, "concurrency": args.concurrency, "devices": len(devices),
        "duration_seconds": round(elapsed, 1), **counts,
        "sustained_rps": round(counts["sent"] / elapsed, 1),
        **lease_counts,
        "client_latency_p50_ms": pct(latencies, 50),
        "client_latency_p95_ms": pct(latencies, 95),
        "client_latency_p99_ms": pct(latencies, 99),
        "note": "client-side latency only; server p95 comes from the postgres log",
    }, indent=2))

if __name__ == "__main__":
    main()
