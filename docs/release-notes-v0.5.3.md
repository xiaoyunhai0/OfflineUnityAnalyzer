# OfflineUnityAnalyzer v0.5.3

This release fixes the first real usability problems found from running the GUI on a larger Unity project.

## Fixed

- GUI text contrast is now explicit for normal, hover, pressed, disabled, and path input states.
- Unity-root-only analysis now scans the important Unity folders automatically instead of relying on manually filled code roots.
- The HTML report no longer behaves like a placeholder/table dump. It now opens as a relationship-first project map.

## Improved

- C# source analysis now uses Roslyn syntax parsing instead of broad regex matching.
- The analyzer exports `data/source-relations.json` with inheritance, field, serialized-field, property, return, parameter, creation, and likely call relations.
- The report highlights modules, important types, code relations, Unity GameObject-to-script bindings, asset reference chains, HybridCLR/YooAsset evidence, diagnostics, and raw data files.
- Added a regression test that verifies Unity-root-only scans include `Assets`, `Packages`, and `ProjectSettings`, and that relationship data is generated.

## Notes

The analyzer still stays read-only for input projects. All report files and JSON data are written only to the selected output directory.
