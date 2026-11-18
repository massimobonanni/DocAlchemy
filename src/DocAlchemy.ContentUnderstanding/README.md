# DocAlchemy.ContentUnderstanding

The Azure Content Understanding adapter project. It references `DocAlchemy.Core` and owns the validated Content Understanding client registration.

`AddDocAlchemyContentUnderstanding` binds and validates the `ContentUnderstanding` settings and registers a singleton SDK client. The GA API is pinned to `2025-11-01`. `ManagedIdentity` uses `DefaultAzureCredential`; `ApiKey` uses only the configured API key. The selected mode never falls back to the other credential type.

The client uses bounded exponential SDK retries and disables request/response content logging. Client construction does not contact Azure.

`IAnalyzerService` is registered as a singleton and implements analyzer listing, retrieval, creation, and deletion. Listing consumes every SDK page and maps the service metadata needed by callers, including modality, status, read-only state, supported operations, and a confidence-availability warning. Custom analyzer modality comes from its `baseAnalyzerId`; known prebuilt IDs are marked read-only, and unknown modalities remain unspecified rather than being guessed.

Creation validates the analyzer ID against the service's 1-64 character rule, requires a JSON object with `baseAnalyzerId`, and rejects an `analyzerId` body property because the path ID is authoritative. It validates with `System.Text.Json` but sends the original JSON string unchanged through the SDK protocol overload, preserving formatting and extension properties. The SDK request explicitly sets `allowReplace=false`, starts the long-running operation, and polls with the configured `Analysis:PollInterval` and `Analysis:OperationTimeout`; caller cancellation remains distinguishable from timeout. Duplicate, permission, throttling, polling, and other service failures remain errors; a missing analyzer returns `null` only for `GetAsync`, while deletion failures propagate. Built-in IDs cannot be deleted.

Confidence warnings account for the analyzer-level `estimateFieldSourceAndConfidence` setting and field-level `estimateSourceAndConfidence` overrides, including nested fields and array item definitions. A warning is returned whenever at least one field is not explicitly or effectively confidence-enabled; analyzer definitions are never changed.
