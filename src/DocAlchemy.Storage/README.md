# DocAlchemy.Storage

The Azure Blob Storage adapter project. It references `DocAlchemy.Core` and owns the validated Blob client registration.

`AddDocAlchemyStorage` binds and validates the `Storage` settings and registers a singleton `BlobServiceClient`. `ManagedIdentity` uses `DefaultAzureCredential`; `AccountKey` uses the configured account name and key directly. The selected mode never falls back to the other credential type.

The client uses bounded exponential SDK retries and disables request/response content logging. Client construction does not contact Azure. Persistence operations are implemented in a later step.
