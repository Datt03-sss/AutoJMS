---
name: design-taste-winforms-sunnyui
description: Premium UI/UX design taste skill for WinForms and SunnyUI. Guidance on theme consistency, layout variance, border & outline elegance, micro-animations, double buffering, high-DPI support, dark theme integration, and anti-slop guidelines in desktop apps.
---

# tasteskill: WinForms & SunnyUI Premium UI/UX Taste Skill

This skill defines rules, guidelines, and visual guidelines for building stunning, cohesive desktop UIs using **WinForms (.NET 8)** and **SunnyUI**.

---

## 1. Operating Principle: Desktop Elegance
Most C# desktop applications default to gray, boxy, or stark high-contrast layouts. To build a premium application, we must apply modern UI/UX design principles (sleek dark modes, curated color palettes, elegant rounded corners, and smooth hover/press states) to the WinForms canvas.

---

## 2. Typography & Fonts
* **Font Family**: Use modern sans-serif fonts like `Segoe UI` or `Outfit` instead of default Microsoft Sans Serif.
* **Sizing System**:
  - Main Titles: `14F - 16F`, Bold or SemiBold.
  - Subheadings / Sections: `11F - 12F`, SemiBold.
  - Body / Inputs: `9.75F - 10F`, Regular.
  - Caption / Small text: `8.5F - 9F`.
* **Application**: Apply fonts recursively to controls but preserve native scaling bounds.

---

## 3. Cohesive Color Palettes (The Dials)
Ensure color tokens map directly to the active theme. Never hardcode colors on individual control elements.

### A. Dark Theme (Cohesive WebView Match)
* **AppBackground**: `#101012` (Matches deep black/charcoal WebView canvas).
* **CardBackground**: `#18181B` (Slightly lighter surface for panels and containers).
* **InputBackground**: `#121214` (Recessed dark inputs).
* **SubtleBorder**: `#27272A` (Low-contrast divider lines).
* **InputBorder**: `#3F3F46` (Visible input borders).
* **PrimaryAccent**: `#E53935` (Bright red brand accents).
* **PrimaryHoverTint**: `#2A1414` (Dark red hover highlight).

### B. Red Theme (J&T Red Accent)
* **AppBackground**: `#F5F7FA` (Cool gray background).
* **CardBackground**: `#FFFFFF` (White panels).
* **PrimaryAccent**: `#E53935` (Solid Red).
* **PrimaryHoverTint**: `#FFEBEE` (Soft red hover fill).
* **InputBorder**: `#D1D5DB` (Light gray border).

### C. Light Theme (Default Professional)
* **AppBackground**: `#F5F7FA`
* **CardBackground**: `#FFFFFF`
* **PrimaryAccent**: `#3B82F6` (Solid Blue)
* **PrimaryHoverTint**: `#EFF6FF` (Soft blue hover fill)
* **InputBorder**: `#D1D5DB`

---

## 4. Component Synchronization Guidelines

### A. Dropdown / ComboBox (`UIComboBox`)
* **Radii**: Set `Radius = 6` for modern look.
* **Dropdown List**:
  - `ItemFillColor` must match the parent panel theme.
  - `ItemHoverColor` should be a soft accent tint (`PrimaryHoverTint`).
  - `ItemSelectBackColor` must be the solid theme accent (`PrimaryAccent`).
  - `ItemSelectForeColor` must be `Color.White` for maximum contrast.

### B. Numeric Inputs (`UIIntegerUpDown`)
* **Radii**: Set `Radius = 6`.
* **State Outlines**:
  - Set `RectColor` for default state.
  - Set `RectHoverColor` and `RectPressColor` to the theme accent.
* **Buttons (+/-)**: Set `SymbolColor`, `SymbolHoverColor`, and `SymbolPressColor` to match hover states dynamically.

### C. Switch / Toggle (`UISwitch`)
* **Thumb Contrasts**: `ButtonColor` should be bright off-white (`#FAFAFA`) in Dark theme, and solid white in Light/Red themes to stand out.
* **Active track**: `ActiveColor` should match the theme's primary accent.
* **Inactive track**: `InActiveColor` should match the subtle border color.

### D. Buttons (`UIButton` / `UISymbolButton`)
* **State Feedback**:
  - **Normal**: Outlined or solid themed background.
  - **Hover**: Shift color brightness (fill becomes slightly darker/lighter).
  - **Pressed**: Darker/solid tone matching `PrimaryPress`.
* **Standard Corner Radius**: Always use `Radius = 6` for buttons to maintain system-wide consistency.

---

## 5. Performance: Prevent Flickering
* **Double Buffering**: Always enable double buffering for all panels, grids, and tab controls to eliminate repaint lag:
  ```csharp
  public static void EnableDoubleBuffer(Control ctrl)
  {
      if (ctrl == null) return;
      try
      {
          var prop = typeof(Control).GetProperty("DoubleBuffered",
              System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
          prop?.SetValue(ctrl, true, null);
      }
      catch { }
  }
  ```
* **Layout Suspension**: Suspend layout updates when applying visual theme overrides:
  ```csharp
  form.SuspendLayout();
  // Apply changes...
  form.ResumeLayout(true);
  ```
