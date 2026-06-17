# Gitignore Recommendations For Target Workspace

Do not apply this blindly. Review against current `.gitignore` before editing.

Recommended target additions:

```gitignore
# Build output
bin/
obj/
publish/
artifacts/
tools/release/output/
tools/installer/installer-output/

# Local app runtime data
AppData/
BrowserData/
logs/
*.log

# Packages
*.nupkg

# IDE
.vs/
*.user
*.suo

# Secrets
.env
*.pfx
*.key
serviceAccountKey.json
service_account.json
backend/**/serviceAccountKey.json
backend/**/.env

# Keep tracked
!.agent/
!docs/
!backend/**/*.md
!backend/**/*.json.example
!tools/**/*.ps1
!tools/**/*.bat
!tools/**/*.iss
```

Current notes:

- Existing `.gitignore` already ignores `bin/`, `obj/`, `publish/`, `installer/inno/installer-output/`, `release/output/`, `*.nupkg`, logs, `.vs/`, `.env`, `serviceAccountKey.json`, and `service_account.json`.
- Existing `.gitignore` does not yet include target `artifacts/`, `tools/release/output/`, or `tools/installer/installer-output/`.
- Do not edit `.gitignore` until structure migration execution is approved.


