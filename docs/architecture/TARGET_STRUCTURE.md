# Target Architecture Structure

To ensure maximum readability, testability, and clean dependency management for AI agents, we propose migrating the AutoJMS project to a standardized clean architecture structure in a multi-phase roadmap.

---

## Proposed Solution Structure

```
AutoJMS.slnx
├── src/
│   ├── AutoJMS.App/                    ← WinForms executable shell
│   │   ├── Forms/                      ← Shell Main form, login forms
│   │   ├── Program.cs                  ← Startup entry point
│   │   └── Resources/                  ← Images, icons, local resources
│   │
│   ├── AutoJMS.UI/                     ← Reusable UI controls and helper layers
│   │   ├── Controls/                   ← Custom user controls
│   │   └── TabShell/                   ← Tab manager and layout configurations
│   │
│   ├── AutoJMS.Domain/                 ← Core business entities and contracts
│   │   ├── Models/                     ← Waybill, inventory, tracking DTOs
│   │   └── Services/                   ← Abstractions (Interfaces)
│   │
│   ├── AutoJMS.Services/               ← Core business logic implementation
│   │   ├── Automation/                 ← WebView2 injections and automation
│   │   ├── Printing/                   ← Barcode printer drivers & preflights
│   │   ├── Tracking/                   ← Waybill status tracker
│   │   └── Updates/                    ← Velopack and SmallUpdate services
│   │
│   ├── AutoJMS.Data/                   ← Data access implementations
│   │   ├── Supabase/                   ← PostgreSQL gateway API
│   │   └── LocalSQLite/                ← Fullstack local database & repos
│   │
│   └── AutoJMS.Infrastructure/         ← Shared infrastructure
│       ├── Config/                     ← AppConfig, SettingsManager
│       ├── Logging/                    ← AppLogger
│       └── Security/                   ← AES and DPAPI encryptors
│
├── tests/
│   ├── AutoJMS.UnitTests/              ← Domain & Service unit tests
│   └── AutoJMS.IntegrationTests/       ← Database & API integration tests
│
└── eng/                                ← Scripts and prompt files
    ├── harness/                        ← Powershell safety scripts
    └── prompts/                        ← Agent system prompts
```

---

## Multi-Phase Migration Roadmap

### Phase 1: Preparation (Current Phase)
- Implement safety gates (`verify.ps1`, `check-secrets.ps1`, `check-project-structure.ps1`).
- Standardize developer documents and agent instructions.
- Ensure `.gitignore` ignores all sensitive configurations.

### Phase 2: Decouple Logic from Main Form
- Move the embedded `DkchManager` out of `WebViewAutomation.cs` into its own file.
- Move WebView2 logic out of event handlers in `Main.cs` to isolated controller classes in `AutoJMS.UI`.
- Move Google Sheets commands out of `FullStackOperation.cs` to `GoogleSheetService.cs`.

### Phase 3: Project Split
- Separate models, interfaces, and services into dedicated C# assemblies.
- Create `AutoJMS.Domain.csproj` and `AutoJMS.Services.csproj`.
- Update namespace imports. Verify build compilation.

### Phase 4: Build Test Infrastructure
- Add `tests/AutoJMS.UnitTests/` containing tests for `TierRuntimePolicy`, `SecureConfigCrypto`, and `JmsResponseClassifier`.
- Integrate tests with the `verify.ps1` script to block PRs if a test fails.
