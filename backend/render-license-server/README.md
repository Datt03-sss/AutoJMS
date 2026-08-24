# AutoJMS License Server

This service verifies licenses and issues the signed desktop assertion. It does
not connect to PostgreSQL and it never receives or returns a DataHub device
token. The desktop enrolls its device with the VPS DataHub API separately.

> **Deploy source warning.** Render production currently serves
> `Datt03-sss/AutoJMS-API`, not this directory — proven by `GET
> /health/firebase/licenses` returning 200 in production while that route has
> never existed in this repo. Nothing in this directory affects production until
> the two copies are reconciled. See section L of
> `docs/agent/FULLSTACK_BACKEND_RISK_REVIEW.md`.

## Local setup

Run npm ci, copy env.template to .env, then run npm run check and npm start.
Run npm test for the license lifecycle unit tests.

Required secrets:

- JWT_PRIVATE_KEY
- JWT_PUBLIC_KEY
- Firebase Admin credentials through one supported source

The only DataHub setting is the public API base URL:

DATAHUB_API_BASE_URL=https://datahub.example.com

The VPS API owns database credentials, device enrollment, leases, ingest and
SignalR. Do not place PostgreSQL passwords, service keys, or device tokens in
this service or in a desktop license response.

## Verification

GET /health checks that the license service is running. A successful
POST /api/verify-license response contains the signed license payload and the
DataHub apiBaseUrl and siteId. The desktop uses apiBaseUrl to reach the VPS,
then completes device enrollment using the signed assertion.

Production enrollment must use the asymmetric issuer validator configured by
the DataHub API.

## License lifecycle

A license is sold as a one-month term but always expires at 00:00
Asia/Ho_Chi_Minh on the 16th, so the fleet renews on one calendar date:

    expiresAt = earliest "day 16, 00:00 +07:00" that is >= (start day + 30 days)

`license-expiry.js` owns that rule and nothing else. Generate a value with:

    node -e "console.log(require('./license-expiry').computeExpiry('2026-08-24').expiresAt)"

A record with no `expiresAt` is treated as perpetual, which is what keeps the
existing fleet working. Past `expiresAt` the license enters `grace` for
LICENSE_GRACE_DAYS days — it still verifies, and the server logs
LICENSE_GRACE — then becomes `expired`.

Verify-license rejections added by this schema:

| HTTP | error | When |
|---|---|---|
| 403 | LICENSE_EXPIRED | past `expiresAt` + `graceDays` |
| 403 | LICENSE_TIER_INVALID | `tier` is not BASE or ULTRA |
| 403 | LICENSE_SITE_CODE_INVALID | `middleCode` is a placeholder, and REQUIRE_UNIQUE_SITE_CODE=1 |

`middleCode` is the DataHub site code. Every existing key still carries the
`"0000"` placeholder, so REQUIRE_UNIQUE_SITE_CODE defaults to 0 and the server
only logs LICENSE_SITE_CODE_PLACEHOLDER. Backfill first, then turn it on.

`POST /api/logout` now requires the access token that owns the session it is
ending; it used to be unauthenticated and unmetered.
