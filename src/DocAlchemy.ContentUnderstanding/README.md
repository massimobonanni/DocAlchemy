# DocAlchemy.ContentUnderstanding

The Azure Content Understanding adapter project. It references `DocAlchemy.Core` and owns the validated Content Understanding client registration.

`AddDocAlchemyContentUnderstanding` binds and validates the `ContentUnderstanding` settings and registers a singleton SDK client. The GA API is pinned to `2025-11-01`. `ManagedIdentity` uses `DefaultAzureCredential`; `ApiKey` uses only the configured API key. The selected mode never falls back to the other credential type.

The client uses bounded exponential SDK retries and disables request/response content logging. Client construction does not contact Azure. Service operations are implemented in later steps.
