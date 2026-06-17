# Module Boundaries & Coupling Rules

To maintain codebase sanity and prevent spaghetti dependencies, all developers and AI agents must follow these architectural boundaries.

---

## 1. Dependency Direction Rule

Dependencies must always flow inward:
```
UI/Forms ──> Services/Automation ──> Domain/Models <── Data/Infrastructure
```
* **UI Layer**: Can reference `Services`, `Domain`, and `Data`.
* **Services Layer**: Can reference `Domain` and `Infrastructure`. It must **never** reference WinForms components or UI controls directly.
* **Domain Layer**: Must have **zero** dependencies on UI, Services, or Data. It contains only clean C# types and interfaces.

---

## 2. SunnyUI Isolation
- SunnyUI controls (e.g. `UITabControl`, `UIRoundButton`) must remain isolated to the UI project.
- Do not let business logic classes receive SunnyUI objects as method parameters. Pass primitive types (e.g. `string`, `int`, `bool`) or Domain DTO models.

---

## 3. WebView2 Interface Boundaries
- WebView2 automation uses JavaScript injection.
- The UI Form must host the WebView2 control.
- JavaScript scripts must be stored in `WebViewAutomation.cs` or as embedded resource text files, and executed through a thread-safe UI thread invocation.
- Do not write business calculations or validation logic in the JavaScript injection scripts. Use them solely for DOM reading and simulated user input events.

---

## 4. Shared Configuration Access
- Do not read from `ConfigurationManager` or custom registry classes from business services.
- Always retrieve configuration properties through `AppConfig` or `SettingsManager` instances resolved via the main dependency injection registry or passed in constructors.
