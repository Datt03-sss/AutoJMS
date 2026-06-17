# Secret Review Required

Migration did not open or move known secret files.

## Files requiring review

| Path | Reason | Action |
| ---- | ------ | ------ |
| `service_account.json` | Google service account-like filename at repository root | Do not commit. Rotate if exposed. Move to secure deployment secret store outside repo. |
| `infra/firebase/config-key.json` | Firebase/key-like config file migrated from old `infra/firebase`; content not reviewed during migration | Verify whether this is a safe schema/example or a real secret before tracking. Rename to `.example.json` if safe. |

## Rules

- Do not commit service account JSON.
- Do not commit `.env`, private keys, `.pfx`, or service-role tokens.
- Prefer `.example.json` and `.env.example` for safe references.

