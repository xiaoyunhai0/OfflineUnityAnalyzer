This release improves split-project Unity layouts where source code and the Unity project live in different folders.

## Added

- Automatic sibling source-root discovery from a selected Unity project root.
- DLL type metadata indexing for managed `.dll` and `.dll.bytes` files.
- Source-to-compiled-DLL bridge data exported to `data/code-assembly-bridges.json`.
- A new report section, **Source To Compiled DLLs**, showing how external source types line up with compiled Unity assemblies.
- Regression coverage for `GameCode/` plus `UnityProject/` layouts.

## Improved

- HybridCLR and hot-update DLLs are easier to understand because bridge matches include assembly kind.
- CLI/config can disable sibling source discovery with `--no-auto-discover-sibling-code` or `autoDiscoverSiblingCodeRoots: false`.

## Notes

The analyzer still registers automatically discovered source roots as read-only input roots. Output must remain outside both the Unity project and any detected source repository.
