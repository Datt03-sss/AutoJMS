# AutoJMS License Server

This service verifies licenses and issues the signed desktop assertion. It does
not connect to PostgreSQL and it never receives or returns a DataHub device
token. The desktop enrolls its device with the VPS DataHub API separately.

## Local setup

Run npm ci, copy env.template to .env, then run npm run check and npm start.

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
