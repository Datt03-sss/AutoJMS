# Refactoring Rules for AI Agents

These rules guide agents when refactoring or cleaning up code in the AutoJMS project. We must balance cleanup with safety to prevent breaking WinForms binding or obfuscation compatibility.

---

## 1. Safe Refactors
These changes carry minimal risk and are encouraged:
- **Variable and Field Renaming**: Renaming local variables or private fields inside classes, provided they do not affect WinForms control names.
- **Extracting Business Logic**: Moving non-UI calculations, string manipulation, or payload preparation from forms into standalone helper utility classes.
- **Unifying Duplicate Logic**: Extracting repeated helper code into common files (e.g., standardizing SQLite connection string builder or string normalizer).
- **Adding Comments & Logging**: Adding descriptive docstrings and logging triggers for troubleshooting.

---

## 2. Risky Refactors (PROHIBITED without approval)
These changes carry high regression risks and must be avoided:
- **Renaming WinForms Controls**: Changing the name of components (e.g. `btnLogin`, `tabMain`) because it breaks event binding in the `.Designer.cs` and resource mappings in `.resx`.
- **Changing Namespaces**: Moving classes into different namespaces. This breaks reflection-based deserialization and post-build obfuscation mappings in .NET Reactor.
- **Modifying Constructor Signatures**: Changing parameters of constructors for main forms or services instantiated via reflection.
- **WebView2 Lifecycle Tweaks**: Changing the initialization or thread marshaling of WebView2 controls.

---

## 3. Namespace & Rename Constraints
- **Preserve Namespaces**: All new classes in `src/AutoJMS/Services/` should remain in the `AutoJMS` namespace, unless a feature-specific sub-namespace is established.
- **Designer Synchronicity**: Never modify `.Designer.cs` files manually. Always use the WinForms designer or rewrite them using a safe property edit.

---

## 4. Rollback Strategy
If a refactor causes build failures or regression errors:
1. **Discard local changes**:
   ```bash
   git checkout -- <file-path>
   ```
2. **Clean build files**:
   ```powershell
   dotnet clean
   ```
3. **Verify original state**:
   Run `verify.ps1` to ensure the project compiles cleanly before attempting a different refactoring approach.
