# COMMERCIAL HARDENING REPORT

## Summary
The project was audited for release stability, persistence durability, and .NET Reactor compatibility. The most important fixes focused on config/license survival, JSON contract stability, and WebView zoom persistence.

## Issues addressed
- Fixed secure config and settings serialization to be Reactor-safe.
- Ensured `license.dat` and `AutoJMS.config.enc` use safer atomic write patterns.
- Added obfuscation exclusions for runtime configuration models.
- Preserved legacy plain JSON settings migration.
- Applied zoom factor to both Home and DKCH WebViews.
- Kept offline-first cache and heartbeat flow intact.

## Remaining risks to verify on real hardware
- `PdfiumViewer` is still a .NET Framework-targeted dependency and should be regression-tested in publish output.
- Google credential APIs emit obsolete warnings and should be reviewed only if a safe replacement is available.
- Nullable warnings exist in several files but do not currently block Release build.
- End-to-end runtime validation on a clean machine is still required.

## .NET Reactor compatibility notes
- Avoid obfuscating JSON property names or changing public model names used for persistence.
- Keep config/license DTOs excluded from rename/control-flow transformations when possible.
- Test with the exact Reactor protection profile intended for production, not the demo defaults.
