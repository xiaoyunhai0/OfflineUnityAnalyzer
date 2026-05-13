# OfflineUnityAnalyzer v0.3.0

This release deepens Unity text YAML analysis.

## Added

- Unity YAML document splitting by `!u!classId &localId`
- Unity object export
- GameObject export with component local IDs
- Component export with owning GameObject and script resolution
- generic Unity `{fileID,guid,type}` asset reference export
- `unity-objects.json`
- `unity-gameobjects.json`
- `unity-components.json`
- `unity-asset-refs.json`
- report summary metrics for Unity objects, GameObjects, Components, and asset refs
- visible report tables for Unity GameObjects, Components, and Asset References

## Notes

This still targets text-serialized Unity assets. Binary Unity assets are not deeply parsed.

Full prefab variant/nested prefab merge remains planned.
