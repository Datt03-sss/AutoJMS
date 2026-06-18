# Active Scope

## Task
Synchronize native left panel controls with selected AutoJMS theme.

## Allowed to edit
* `src/AutoJMS/UI/AppTheme.cs`
* `src/AutoJMS/UI/AppPalette.cs` (if needed)

## Allowed to create
* None

## Forbidden
* License/Auth/Hash check
* Firebase session
* Supabase production config
* Velopack/update/release
* Database schema
* JMS API
* Tracking parser
* Print business logic
* WebView automation logic
* service_account/key/token/secret
* version/release config

## Stop conditions
Claude must stop if:
* implementation requires changing forbidden files
* implementation requires changing business logic
* more than 5 code files need editing
* Main.Designer.cs requires broad rewrite
* build fails in unrelated area
* scope check fails
