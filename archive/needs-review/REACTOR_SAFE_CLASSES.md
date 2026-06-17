# REACTOR SAFE CLASSES

The following classes are currently protected with obfuscation exclusions and are intended to remain stable under .NET Reactor:

- `AppRuntimeConfig`
- `AppConfig`
- `AppSettings`
- `SettingsManager`
- `ProtectedPayload`
- `SecureConfigCrypto`
- `WaybillDbModel`

## Notes
- JSON property names are explicitly fixed with `JsonPropertyName` where persistence matters.
- Secure payloads are public and marked with `[Obfuscation(Exclude = true, ApplyToMembers = true)]`.
- Config and settings write paths use atomic replacement patterns to reduce corruption risk.
- Reflection-based code should not depend on obfuscated member names for persistence models.
