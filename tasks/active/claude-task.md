# Claude Code Task — AutoJMS Native Left Panel Theme Sync

## Approved by owner
Yes

## Repo
https://github.com/Datt03-sss/AutoJMS

## Branch
main

## Task summary
Synchronize native left panel controls with the selected AutoJMS theme, focusing on dropdown, numeric +/- buttons, button hover/click/focus, outlines, DATA/CONTROL/NEWBILL headers, and empty panel surfaces.

## Requirements
- **Headers (DATA, CONTROL, NEWBILL)**: Use theme accent colors. Add consistent borders. Ensure text contrast is high.
- **Dropdown (SHEET)**: Align border/focus colors to the theme. Selected items should use theme highlight colors. Disabled state must remain legible.
- **Numeric Control (-/+)**: Buttons must use theme colors with distinct hover and pressed states. Add consistent outline/border.
- **Toggle**: Checked/unchecked states should reflect theme colors.
- **Buttons (Home, DKCH1, DKCH2)**: Remove disjointed hardcoded colors. Use standardized tokens (e.g., Primary/Secondary). Ensure hover, click, and focus states are visually distinct and theme-aligned.
- **List/Panel Area (under NEWBILL)**: Use a unified surface background (avoid pure jarring white if it clashes). Apply soft borders to maintain a professional look even when empty.
- **General**: Do not hardcode colors in events; use `AppTheme.ThemeColors`. Do not cause WebView reloads or slow down tab transitions. Do not alter button click logic.

## Scope allowed
* `src/AutoJMS/UI/AppTheme.cs`
* `src/AutoJMS/UI/AppPalette.cs` (if needed)

## Scope forbidden
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

## Build commands
```powershell
dotnet restore .\AutoJMS.slnx
dotnet build .\AutoJMS.slnx -c Release
```

## Required checks before commit
```powershell
git diff --name-only
git diff --stat
powershell -ExecutionPolicy Bypass -File .\eng\agents\check-scope.ps1
```

## Commit command after build and scope check pass
```powershell
git add .
git commit -m "feat(ui): synchronize left panel controls with app theme"
git push origin main
git log --oneline -1
git status
```

## Do not
* Do not force push
* Do not rewrite history
* Do not release production
* Do not increase version
* Do not commit secrets
* Do not modify forbidden files
* Do not change business logic

## Final report required
* Summary
* Files changed
* Theme changes
* Dropdown changes
* Numeric +/- changes
* Button hover/click/focus changes
* Build result
* Scope check result
* Commit hash
* Pushed to GitHub
* Owner manual test checklist
* Risks
