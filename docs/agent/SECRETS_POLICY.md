# AutoJMS Secrets Policy

This policy outlines how secrets, credentials, and tokens must be handled to prevent security leaks in the AutoJMS repository.

---

## 1. What Counts as a Secret
The following files and patterns are considered sensitive and must **never** be tracked by Git:

| Secret Type | Example | Exclusion Rule in `.gitignore` |
|---|---|---|
| **Google Service Accounts** | `service_account.json` | `service_account*.json`, `service_accout*.json` |
| **Firebase Keys** | `serviceAccountKey.json` | `serviceAccountKey*.json` |
| **Certificates / PFX** | `autojms.pfx`, `key.pem` | `*.pfx`, `*.key`, `*.pem` |
| **Claude Local Settings** | `settings.local.json` | `.claude/settings.local.json` |
| **Local Environment** | `.env` | `.env`, `.env.*` |
| **Encrypted Config** | `configs.sec` | `*.sec` |
| **Secrets Folder** | `secrets/` | `secrets/` |

---

## 2. Token Masking in Logs
AutoJMS deals with two different tokens:
1. **License JWT** (From Render license server): Authenticates user subscription.
2. **JMS AuthToken** (From WebView2 local storage): Authenticates calls to the JMS API Gateway.

### Rules:
- **Never log full tokens**: Writing full 32-character hex tokens or JWT payloads to console or logs is a high-risk security violation.
- **Always mask**: Tokens must be truncated and masked to `first4...last4` (e.g. `abcd...ef01`) before logging.
- **Implementation**:
  ```csharp
  public static string MaskToken(string token)
  {
      if (string.IsNullOrEmpty(token) || token.Length <= 8) return "****";
      return $"{token.Substring(0, 4)}...{token.Substring(token.Length - 4)}";
  }
  ```

---

## 3. Gitignore Enforcement
- All development environments must run `check-secrets.ps1` as part of the pre-commit checks.
- If a secret file is accidentally staged, run:
  ```bash
  git rm --cached <file-path>
  ```
  And verify that it matches one of the patterns in `.gitignore`.
