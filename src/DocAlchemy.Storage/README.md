# DocAlchemy.Storage

The Azure Blob Storage adapter project. It references `DocAlchemy.Core` and owns the validated Blob client registration.

`AddDocAlchemyStorage` binds and validates the `Storage` settings and registers a singleton `BlobServiceClient`. `ManagedIdentity` uses `DefaultAzureCredential`; `AccountKey` uses the configured account name and key directly. The selected mode never falls back to the other credential type.

The client uses bounded exponential SDK retries and disables request/response content logging. Client construction does not contact Azure.

`IAnalysisResultStore` saves the immutable Core snapshot as UTF-8 JSON with the `application/json` content type. Each result uses its stable analysis ID as a `.json` blob name and conditional creation; a retry that finds the blob verifies byte-for-byte equality, which also handles a prior successful upload whose response was lost. A conflicting payload for the same ID fails explicitly. The configured results container must already exist and be private; this adapter does not create containers or modify access policies. Its returned reference is only the blob name, never a URL or SAS token. Authorization, missing-container, and other storage errors propagate as Azure request failures for the caller to distinguish from analysis errors.
