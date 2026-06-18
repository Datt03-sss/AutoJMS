# Native Left Panel Theme Plan

## Summary
The goal is to synchronize the UI components in the native left panel (tabDKCH left panel) with the currently selected AutoJMS theme. The changes will ensure consistency in styling across normal, hover, active, and focus states for various controls, without altering any business logic or the right-side WebView.

## Current UI problems from screenshot
- DATA, CONTROL, and NEWBILL section headers lack a cohesive theme accent and well-defined borders.
- SHEET dropdown (ComboBox) does not reflect theme colors when opened or selected.
- Numeric +/- buttons lack distinct hover and click states matching the theme.
- Action buttons (Home, DKCH1, DKCH2) have disjointed colors that do not align with theme tokens.
- Panel and list areas beneath NEWBILL appear too starkly white and lack soft boundaries or theme-consistent surface colors.

## Controls involved
- `tabDKCH_dataSrc` (DATA section header)
- `uiTitlePanel1` (CONTROL section header)
- `uiTitlePanel2` (NEWBILL section header)
- `tabDKCH_sheetName` (SHEET Dropdown)
- `tabDKCH_numRow` (Numeric Up/Down)
- `tabDKCH_useSheet` (Toggle Switch)
- `tabDKCH_Home` (Home Button)
- `tabDKCH_btnDKCH1` (DKCH1 Button)
- `tabDKCH_btnDKCH2` (DKCH2 Button)
- Empty lists/panels under the NEWBILL section (e.g., `tabDKCH_inputNewBill`, `tabDKCH_newBillDone`, `tabDKCH_nowTracking`).

## Files inspected
- `src/AutoJMS/Forms/Main.Designer.cs`
- `src/AutoJMS/UI/AppTheme.cs`
- `src/AutoJMS/Forms/Main.cs`

## Proposed files to edit
- `src/AutoJMS/UI/AppTheme.cs` (to introduce new theme tokens and apply styling logic)
- `src/AutoJMS/UI/AppPalette.cs` (if additional generic theme tokens are required)

## Proposed files to create
- None (Helpers will be added within `AppTheme.cs` to prevent unnecessary file creation).

## Forbidden files
- `src/AutoJMS/Licensing/*`
- `src/AutoJMS/Updates/*`
- Any file related to WebView automation logic, Firebase, Supabase, Print logic, and Tracking parser.

## Theme tokens needed
To avoid hardcoding, the following conceptual tokens should be established or utilized from `AppTheme.ThemeColors`:
- `SectionHeaderBackground`
- `SectionHeaderText`
- `EmptyPanelBackground` (for empty tracking lists)
- `ButtonPrimaryHover`, `ButtonPrimaryPressed`
- `ControlBorder`, `ControlFocusBorder`

## Implementation steps for Claude
1. **Analyze existing AppTheme**: Locate `AppTheme.cs` and the recent helper methods (`ApplyThemeToComboBox`, `ApplyThemeToNumeric`, etc.).
2. **Expand Theme Handling**: Introduce logic to style `UITitlePanel` (DATA, CONTROL, NEWBILL) uniformly in `ApplyStyleToControl`.
3. **Style Lists/Panels**: Ensure `UIRichTextBox` (`tabDKCH_inputNewBill`, `tabDKCH_newBillDone`, etc.) have a surface color (e.g., `CardBackground` or `InputBackground`) and a subtle border rather than pure white.
4. **Refine Buttons**: Adjust the helper logic for `UIButton` to ensure Home/DKCH1/DKCH2 use appropriate theme tokens instead of isolated hardcoded ARGB values.
5. **Testing**: Build the application, verify that the left panel components react correctly to Theme Mode changes without breaking functionality.

## Risks
- Modifying control styles dynamically may cause visual inconsistencies if a control is not captured by `ApplyStyleToControl`.
- Overriding designer properties at runtime requires careful tracking of control names.

## Owner review checklist
- [ ] Review proposed styling token mapping.
- [ ] Confirm no business logic is planned for modification.
- [ ] Approve the scope.
