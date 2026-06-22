# BRIEFING — 2026-06-22T09:57:00+07:00

## Mission
Analyze the codebase to plan the refactoring of `FullStackOperation.cs` and related partial classes into a full-screen WebView2 host.

## 🔒 My Identity
- Archetype: Fullscreen Explorer
- Roles: Read-only investigator, analyzer
- Working directory: d:\v1.2605.2(new-test)\.agents\explorer_fullscreen
- Original parent: 2542f1cc-93a8-4f47-9989-3078a20d18a2
- Milestone: Planning Full Stack WebView2 Refactoring

## 🔒 Key Constraints
- Read-only investigation — do NOT implement code changes.
- Adhere strictly to the Workspace Model and Workspace Lock Rules if writing (though we are read-only, we only write in our folder).
- UI Architecture Rule (FullStackOperation): FullStackOperation form must act exclusively as a full-screen host for the WebView2 control. No native tabs, WebView2 must dock Fill directly to the Form.

## Current Parent
- Conversation ID: 2542f1cc-93a8-4f47-9989-3078a20d18a2
- Updated: 2026-06-22T09:57:00+07:00

## Investigation State
- **Explored paths**: `src/AutoJMS/Forms/FullStackOperation*.cs`, `src/AutoJMS/Web/index.html`
- **Key findings**:
  - `ShowTitle` and `TitleHeight` exist in `Sunny.UI.UIForm` (v3.9.6).
  - All native tabs (`uiTabControl1`, `tabDash`, `tabChat`, `_tabThoiHieu`, `_tabDetail`), grids, and custom layouts are obsolete.
  - State fields will replace native control getters.
  - Zalo chatbot reminders are obsolete in `FullStackOperation` and do not affect `Main.cs`.
- **Unexplored areas**: None.

## Key Decisions Made
- Replace UI control getters in `PostStateToWebView2` and message handlers with internal class fields.
- Clean up all WinForms control definitions and layout methods.

## Artifact Index
- d:\v1.2605.2(new-test)\.agents\explorer_fullscreen\handoff.md — Complete analysis and refactoring plan.
