# DocAlchemy.ContentUnderstanding.Tests

xUnit transport-isolated tests for `DocAlchemy.ContentUnderstanding`. `AnalyzerServiceTests` covers unchanged create-body transmission, no-replacement semantics, request validation, pagination, confidence inheritance/overrides, built-in deletion restrictions, bounded polling, cancellation, and surfaced service failures.

`DocumentAnalysisServiceTests` verifies binary submission, stream bounds, all field types and nested values, multiple and empty content items, malformed output, polling retries and throttling, operation failures, timeouts, cancellation, and single-attempt submission even after errors, lost responses, or redirects. Tests use an in-process HTTP handler and never call Azure.

Run with `dotnet test tests\DocAlchemy.ContentUnderstanding.Tests\DocAlchemy.ContentUnderstanding.Tests.csproj`.
