# Source Workspace

This folder contains production source projects.

Rule:
Only files that compile into the AutoJMS solution should live under `src/`.

Current status:
- `src/AutoJMS/` contains the .NET 8 WinForms production app.
- `src/AutoJMS.Abstractions/` contains shared module contracts referenced by the main project.
- Legacy sample module projects are archived under `archive/old-module-system/`.

Do not move additional files into `src/` unless they are compiled source, project assets, or runtime content required by the app.
