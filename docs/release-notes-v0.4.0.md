# OfflineUnityAnalyzer v0.4.0

This release improves hot update and resource address understanding.

## Added

- YooAsset manifest asset extraction
- YooAsset package/address/asset path/bundle/tag export
- YooAsset load API string literal extraction from C# code
- structured YooAsset code references
- config/reference shallow index for JSON, CSV, XML, and readable `.bytes`
- GUID, asset-address, and type-name clue extraction from config-like files
- `config-refs.json`
- report tables for YooAsset manifest assets, YooAsset code references, and config references

## Notes

YooAsset manifest parsing supports common JSON field names and intentionally remains tolerant. Unknown manifest shapes are skipped with warnings instead of failing the analysis.
