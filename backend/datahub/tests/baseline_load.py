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
overhead, not load -- it is counted in lease_renewals / lease_renew_failures (bulk
only) and is deliberately kept out of sent / sustained_rps.

An instrument must not move the numbers it publishes. Three properties are load
bearing here and are asserted by the code below, not by hope:
  * elapsed covers the LOADING window only -- not the renewer's shutdown, and not a
    worker's trailing pacing sleep (both used to land in the divisor of sustained_rps);
  * renewal overhead is a FLOOR spread across its interval, not a 2x burst landing in
    the same window whose latencies are being recorded;
  * a rejected request is not throughput -- sustained_rps is the attempt rate, ok_rps
    is the successful one, and both are printed so neither can be mistaken for the other.
"""
import argparse, json, random, statistics, string, sys, threading, time, urllib.error, urllib.request

# One HTTPS call can block this long. Everything that has to outlast an in-flight
# request (see RENEWER_JOIN_TIMEOUT_SECONDS) is derived from this number rather than
# repeating it, so the two can never drift apart.
REQUEST_TIMEOUT_SECONDS = 30

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
        with urllib.request.urlopen(req, timeout=REQUEST_TIMEOUT_SECONDS) as resp:
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
        # Truncated: this request carried the site assertion as a Bearer header, so its
        # error body is the one most likely to echo request context back into a log or
        # a pasted report. 200 chars is enough to diagnose and short enough to bound.
        raise SystemExit(f"enroll {site_code} failed: HTTP {status} {body[:200]}")
    raise SystemExit(f"enroll {site_code} still rate-limited after 6 attempts")

def acquire_lease(base, site_id, token):
    status, body, _ = call(base, "POST", f"/api/v1/sites/{site_id}/lease/acquire", token=token)
    if status != 200:
        # Truncated for the same reason as enroll(): Bearer device token on the way out.
        raise SystemExit(f"lease acquire failed for {site_id}: HTTP {status} {body[:200]}")
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

# LeaseRepository.RenewIntervalSeconds. On the 120 s run this harness is actually used
# for, ticks land at t=30/60/90 -- the t=120 tick is the deadline itself and never fires
# -- so it is 3 renewals per device per run, i.e. 30 requests across 10 devices. That is
# exactly what all four published runs observed (lease_renewals = 30). Against the
# 600/min IP bucket and the 240/min per-device bucket it is negligible next to the load.
RENEW_INTERVAL_SECONDS = 30
# stop.set() can land while the renewer is inside one HTTPS call, which call() caps at
# REQUEST_TIMEOUT_SECONDS. Wait that long plus slack for TLS teardown before declaring
# the renewer stuck -- never a bare literal, so the two cannot drift apart.
RENEWER_JOIN_TIMEOUT_SECONDS = REQUEST_TIMEOUT_SECONDS + 5

def renew_leases_until(base, devices, deadline, counters, lock, stop):
    """Daemon loop: renew every lease every RENEW_INTERVAL_SECONDS until the run's
    deadline. A failed renewal is a real finding (the lease was lost or fenced), so it
    is counted and printed, never swallowed.

    The tick is SPREAD across the interval instead of fired as a back-to-back burst.
    urllib.request pools nothing, so N serial renewals mean N fresh TLS handshakes: as
    a burst that is roughly a 2x instantaneous rate spike three times per run, landing
    in the very window whose client latencies are being recorded. Spread at
    RENEW_INTERVAL_SECONDS / len(devices) the same N requests become a constant floor,
    and every lease is still renewed once per interval -- ~31 s apart against a 120 s
    LeaseDurationSeconds, which is margin, not a race."""
    gap = RENEW_INTERVAL_SECONDS / max(1, len(devices))
    next_at = time.time() + RENEW_INTERVAL_SECONDS
    while True:
        wait = min(next_at, deadline) - time.time()
        if wait > 0 and stop.wait(wait):
            return
        if time.time() >= deadline or stop.is_set():
            return
        next_at += RENEW_INTERVAL_SECONDS
        for i, dev in enumerate(devices):
            # Pace between devices, and bail on stop. Without the check, a tick that had
            # already started ran all N serial 30 s-timeout calls to completion whatever
            # stop said, so shutdown was unbounded -- and every second of it used to be
            # charged to elapsed, the divisor of sustained_rps.
            if i > 0 and stop.wait(gap):
                return
            if stop.is_set():
                return
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
    # (wall_clock_epoch, latency_ms) per successful request, not a bare latency. The
    # timestamp is what lets a later analysis exclude a contaminated window -- a renewal
    # tick, a sampler pass, anything overlapping -- without re-running the load. The
    # latency component is extracted unchanged for the percentiles below, so the
    # published p50/p95/p99 formula is untouched by this.
    samples = []
    counts = {"sent": 0, "ok": 0, "http_429": 0, "http_409": 0, "other_errors": 0}
    # Kept in their own dict, not in counts, so renewal overhead can never leak into
    # sent or sustained_rps.
    lease_counts = {"lease_renewals": 0, "lease_renew_failures": 0}

    # Bound BEFORE worker() is defined. worker() closes over deadline, and binding it
    # after the closure only worked because no thread started early. A future edit that
    # starts one sooner would raise NameError inside a thread, where it prints and the
    # run silently continues with fewer workers -- a quieter, worse failure than a crash.
    started = time.time()
    deadline = started + args.duration_seconds

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
                    samples.append((time.time(), ms))
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
            # Clamped at the deadline: the loop tests the deadline BEFORE a request and
            # sleeps AFTER accounting, so an unclamped worker appends up to one full
            # interval of pure idle -- 6.25 s at --concurrency 50 --target-rps 8 -- with
            # zero requests in flight. That idle is not measurement time; leaving it in
            # inflated duration_seconds and deflated every rate derived from it.
            slack = min(interval - (time.perf_counter() - cycle), deadline - time.time())
            if slack > 0:
                time.sleep(slack)

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
    # Stamped the instant the last worker stops, BEFORE teardown. elapsed is the divisor
    # of every rate this prints, so anything charged to it has to be loading. Stopping
    # the renewer is teardown, not load: taken after stop.set() it billed the whole
    # shutdown -- up to one in-flight HTTPS call per device -- to the measurement.
    elapsed = time.time() - started
    stop.set()
    renewer_incomplete = False
    if renewer is not None:
        renewer.join(timeout=RENEWER_JOIN_TIMEOUT_SECONDS)
        # A renewer still running past its own bounded shutdown means the lease traffic
        # for this run is unaccounted for. Say so in the output: an unchecked join()
        # turns a non-final run into one that looks final.
        renewer_incomplete = renewer.is_alive()
    # Snapshot under the lock. If the renewer did outlive the join it is still mutating
    # lease_counts, and unpacking it unlocked was a data race on the way to publication.
    with lock:
        counts_out = dict(counts)
        lease_out = dict(lease_counts)
        latencies = [ms for _, ms in samples]

    def pct(values, q):
        return round(statistics.quantiles(values, n=100)[q - 1], 1) if len(values) > 100 else None

    out = {
        "mode": args.mode, "concurrency": args.concurrency, "devices": len(devices),
        "duration_seconds": round(elapsed, 1), **counts_out,
        # sustained_rps counts every attempt, including rejections -- kept with its
        # original name and formula so the published baselines stay comparable. ok_rps
        # is the rate that actually got served. A run has been seen printing
        # sustained_rps 8.0 with 309/960 rejected, where the served rate was 5.4.
        "sustained_rps": round(counts_out["sent"] / elapsed, 1),
        "ok_rps": round(counts_out["ok"] / elapsed, 1),
    }
    # bulk only: interactive takes no lease, so these fields are structurally always
    # zero for it and printing them invites reading a zero as a measurement.
    if args.mode == "bulk":
        out.update(lease_out)
        if renewer_incomplete:
            out["lease_renewer_incomplete"] = True
    out.update({
        "client_latency_p50_ms": pct(latencies, 50),
        "client_latency_p95_ms": pct(latencies, 95),
        "client_latency_p99_ms": pct(latencies, 99),
        "note": "client-side latency only; server p95 comes from the postgres log. "
                "sustained_rps is the ATTEMPT rate -- 429/409/errors are counted as sent; "
                "ok_rps is the successful rate. Neither is a saturation point: both are "
                "capped by --target-rps.",
    })
    print(json.dumps(out, indent=2))

if __name__ == "__main__":
    main()
