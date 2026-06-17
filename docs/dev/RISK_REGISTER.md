# Risk Register

| Risk | Level | Area | Mitigation |
| ---- | ----- | ---- | ---------- |
| Build fail due to missing modules JSON | Medium | Build | Keep optional root `modules/*.json` content includes conditional or provide minimal defaults if runtime requires them. |
| WebView2 UI thread access | High | WebView2/WinForms | Marshal all WebView2 calls to UI thread through `Invoke`/`UiThread`. |
| JMS authToken 401 false expired | High | Auth/JMS API | Refresh token and retry once; do not clear token on first 401; do not treat all business errors as expired. |
| BASE running background jobs | High | Tier policy | Guard with `TierRuntimePolicy`; test BASE and ULTRA separately. |
| FullStack grid null/disposed | High | FullStackOperation | Check control lifecycle before grid update; cancel tasks on close. |
| Supabase file size limit | Medium | Release | Upload `.nupkg` and Setup.exe to GitHub Releases only. |
| GitHub Release update source | Medium | Update | Keep `version-latest.json` tag/channel aligned with GitHub assets. |
| Hash mismatch after .NET Reactor | High | Integrity/update | Hash `AutoJMS.dll` after Reactor protect and update `hash-manifest.json`. |
| Token logging | High | Security/logging | Mask full JMS token/license JWT in production logs. |
| Service account secret exposure | Critical | Secrets | Do not commit service account files; rotate if exposed. |
| WinForms Designer path breakage if moving files | High | Project structure | Do not move code into `src/` until Designer/release paths are planned and tested. |

