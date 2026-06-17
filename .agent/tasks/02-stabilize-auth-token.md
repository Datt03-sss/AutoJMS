# Stabilize AuthToken / JMS 401

## Required reading

- AGENTS.md
- .agent/context/*
- .agent/rules/*
- docs/audit/CODEBASE_AUDIT.md

## Goal

Make JMS authToken handling reliable without confusing it with the license JWT.

## Do not modify

- License JWT validation contract unless explicitly requested
- WebView2 access from background thread
- Existing HOME/DKCH/TRACKING/PRINT behavior

## Allowed files

- `JmsAuthTokenService.cs`
- `JmsApiClient.cs`
- `JmsResponseClassifier.cs`
- `Main.cs` only for token capture/refresh paths if needed
- Relevant docs/tests if created

## Steps

1. Map token sources: in-memory, WebView2, config.
2. Verify JMS token format is 32 hex.
3. Ensure JWT-like strings are rejected for JMS API calls.
4. Check 401 flow: refresh from WebView2, retry once, then expire.
5. Ensure HTTP 200 business errors are not automatically classified as token expired without evidence.
6. Mask full token logs.
7. Test token refresh and retry behavior.

## Acceptance criteria

- Valid JMS token accepted.
- License JWT is not used as JMS token.
- First 401 triggers refresh and one retry.
- Token is not cleared on first 401.
- Full token is not logged.

## Rollback notes

Revert classifier/token-service changes if API calls stop authenticating; keep any docs describing the failure.

