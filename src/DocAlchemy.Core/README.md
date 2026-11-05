# DocAlchemy.Core

The framework-independent home for DocAlchemy domain models and service contracts. This project has no Azure SDK or web framework dependency.

## Contracts

- `IAnalyzerService` provides asynchronous list, get, create, and delete operations. Analyzer creation accepts the original create-request JSON and a separately supplied analyzer ID; implementations must not replace an existing analyzer.
- `IDocumentAnalysisService` accepts a validated document stream and returns an immutable `AnalysisResult`. The caller owns the stream.
- `IAnalysisResultStore` persists a completed result and returns an opaque storage reference, not a public URL.
- `AnalysisResult` captures the stable analysis ID, analyzer ID, threshold, UTC completion time, schema version, and every content item. Empty content and field collections are valid.
- `ExtractedField` retains the service type discriminator, cloned `JsonElement` value, nullable confidence, and recursively mapped children. Unknown types and explicit JSON null values are preserved.

`ConfidenceThreshold.IsBelow` implements the shared strict-less-than warning rule. Missing confidence never warns; thresholds and present confidence values must be finite and in `[0, 1]`.

Field paths use `/contents/{index}/fields/{escaped-name}`. Object properties append `/properties/{escaped-name}`, while array elements append `/items/{zero-based-index}`. Names use RFC 6901 token escaping (`~` becomes `~0`, `/` becomes `~1`), and the explicit structural segments distinguish a numeric property name from an array index.

`AnalysisJson` writes camel-case persisted JSON and includes nullable properties such as `confidence` even when their value is null. It uses `schemaVersion` 1 for the current result contract.
