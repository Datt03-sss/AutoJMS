# Global Rules

## MUST READ BEFORE ANY CODE CHANGES

1. Always read `AGENTS.md` first.
2. Always read `.agent/context` before changing code.
3. Always read `.agent/rules` before changing code.
4. Always read `docs/audit/CODEBASE_AUDIT.md` if available.

## MANDATORY GLOBAL RULES

1. Always read `AGENTS.md` first.
2. Always read `.agent/context` before changing code.
3. Always read `.agent/rules` before changing code.
4. Always read `docs/audit/CODEBASE_AUDIT.md` if available.
5. Do not change old logic unless explicitly requested.
6. Prefer surgical fixes over large refactors.
7. Do not rename controls/classes/namespaces unless required.
8. Do not move production code in Phase 1.
9. Do not delete old files.
10. Do not add secrets to repo.
11. Do not hardcode new URLs/tokens/tiers.
12. Do not log full production tokens.
13. Do not block WinForms UI thread.
14. Do not access WebView2 outside UI thread.
15. Do not let BASE tier run background inventory/database sync.
16. Major update is manual via tab About.
17. GitHub Releases host Velopack binary.
18. Supabase stores only small manifest/config files.

## ABSOLUTE PROHIBITIONS

### Never Do These Without Explicit Request

- **DO NOT rewrite production code**
- **DO NOT modify namespaces, class names, or control names**
- **DO NOT change existing logic** in HOME, DKCH, TRACKING, PRINT, or ABOUT
- **DO NOT delete files**
- **DO NOT delete comments that explain intent**
- **DO NOT move production code** without a migration plan
- **DO NOT modify installer scripts** (AutoJMS.iss, build-release.ps1) without explicit request
- **DO NOT modify release scripts** without explicit request
- **DO NOT hardcode additional URLs, tokens, or tier names**

### Never Do These Ever

- **DO NOT log full tokens** in production (mask to first6...last4)
- **DO NOT block UI thread** (always async/await for long operations)
- **DO NOT access WebView2 outside UI thread** (marshal to UI thread)
- **DO NOT run background jobs on BASE tier** (inventory sync, database tracking, auto-sync timer)
- **DO NOT create duplicate services** (check if service already exists)
- **DO NOT commit secrets** (.env, service_account.json, *.pfx, *.key)

## BEFORE MAKING ANY CHANGE

1. Identify the exact file and line numbers
2. Understand the current behavior
3. Propose the minimum change needed
4. Add logging for debugging
5. Define acceptance criteria
6. Test the change

## WHEN UNCERTAIN

- State "UNKNOWN / NEED VERIFY" explicitly
- Do not guess or assume
- Ask for clarification
- Document your uncertainty

## MINIMUM VIABLE CHANGE PRINCIPLE

Always prefer the smallest change that solves the problem:
- Fix the specific bug, don't refactor the module
- Add the new feature, don't rewrite existing code
- Update documentation, don't rewrite docs

## BACKWARD COMPATIBILITY

- Never break existing functionality
- If you must change behavior, add a migration path
- Consider what happens to existing users' data

## TESTING

- If you fix a bug, describe how to test it
- If you add a feature, describe how to verify it works
- If you change a UI, describe what the expected behavior should be

## COMMUNICATION

- When you find a serious issue, document it in `docs/audit/CODEBASE_AUDIT.md`
- When you find a potential risk, document it in `docs/migration/current-to-clean-structure-plan.md`
- Do NOT fix serious issues without explicit user request
