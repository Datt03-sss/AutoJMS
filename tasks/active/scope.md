# Active Scope

## Task
Set/unset page size must apply to the current Windows default printer for AutoJMS print jobs.

## Allowed to edit
- `src/AutoJMS/Forms/Main.cs`

## Allowed to create
- None.

## Forbidden
- License/Auth/Hash check
- Firebase session
- Supabase production config
- Velopack/update/release
- Database schema
- JMS API
- Tracking parser
- WebView automation
- service_account/key/token/secret
- version/release config
- unrelated UI theme files
- Windows global printer preferences (Win32 DEVMODE)

## Stop conditions
Claude must stop if:
- implementation requires changing forbidden files
- implementation requires changing Windows global printer preferences
- implementation requires Win32 DEVMODE global change
- more than 5 code files need editing
- build fails in unrelated area
- scope check fails
