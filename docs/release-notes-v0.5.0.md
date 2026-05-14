This release turns the desktop app into a practical GUI analysis entry point.

## Added

- Avalonia desktop analysis workspace
- automatic Unity project path discovery
- C# code root, DLL/HybridCLR root, and YooAsset manifest root suggestions
- safe default output directory under the user's documents folder
- readonly guard status in the GUI
- live analysis stage progress using CLI JSON progress events
- analysis log panel, cancel button, report/output open buttons, and CLI command copy

## Notes

The GUI still delegates analysis to the bundled CLI. It only reads input paths and writes analysis output to the selected output directory.
