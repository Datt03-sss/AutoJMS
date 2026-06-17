# AutoJMS Render License Server

Node/Express service for AutoJMS license verification and heartbeat.

## Setup

```powershell
cd backend/render-license-server
npm install
copy .env.example .env
```

Fill `.env` with real JWT keys, Firebase credentials, and the Supabase anon key.
`FIREBASE_OPERATION_TIMEOUT_MS` defaults to `10000` so license verification fails fast instead of hanging when Firebase is unreachable or misconfigured.

## Run

```powershell
npm run check
npm start
```

Health check:

```powershell
Invoke-RestMethod http://localhost:3000/health
```

## Required Secrets

- `JWT_PRIVATE_KEY`
- `JWT_PUBLIC_KEY`
- Firebase service account through one of:
  - `FIREBASE_SERVICE_ACCOUNT_JSON`
  - `FIREBASE_SERVICE_ACCOUNT_BASE64`
  - `GOOGLE_APPLICATION_CREDENTIALS`
  - local fallback `serviceAccountKey.json`
- `SUPABASE_ANON_KEY`

Never use the Supabase `service_role` key as `SUPABASE_ANON_KEY`.
