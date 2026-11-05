# DocAlchemy.Web

The .NET 10 Blazor Web App using Interactive Server rendering. It composes the Core contracts and Azure adapter projects through dependency injection.

## Configuration contract

The Web App validates all settings at startup. The following names are the provisioning contract for later Bicep/AZD work; Azure App Service settings use `__` in place of `:`. Endpoint validation is syntactic only and does not check Azure reachability or permissions.

| Setting | Required | Default / validation |
| --- | --- | --- |
| `ContentUnderstanding:Endpoint` | Yes | Absolute HTTPS endpoint; no user info, query, or fragment |
| `ContentUnderstanding:ApiVersion` | No | Fixed to GA `2025-11-01` |
| `ContentUnderstanding:AuthenticationMode` | No | `ManagedIdentity`; alternatively `ApiKey` |
| `ContentUnderstanding:ApiKey` | Only in `ApiKey` mode | Secret; rejected when identity mode is selected |
| `Storage:BlobServiceEndpoint` | Yes | Absolute HTTPS Blob service endpoint |
| `Storage:ContainerName` | Yes | Valid lowercase Azure Blob container name |
| `Storage:AuthenticationMode` | No | `ManagedIdentity`; alternatively `AccountKey` |
| `Storage:AccountName` | Only in `AccountKey` mode | Valid Azure Storage account name |
| `Storage:AccountKey` | Only in `AccountKey` mode | Base64 storage key; rejected when identity mode is selected |
| `Analysis:ConfidenceThreshold` | No | `0.80`; finite value in `[0, 1]` |
| `Analysis:OperationTimeout` | No | `00:10:00`; greater than zero and at most `02:00:00` |
| `Analysis:PollInterval` | No | `00:00:02`; greater than zero and no greater than the timeout |
| `Uploads:MaxDocumentBytes` | No | `20971520` bytes; greater than zero and no more than `200000000` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | No | Enables Application Insights when nonempty |

Examples of equivalent environment variable names are `ContentUnderstanding__Endpoint`, `Storage__BlobServiceEndpoint`, `Analysis__ConfidenceThreshold`, and `Uploads__MaxDocumentBytes`. Application Insights uses its standard uppercase environment variable name exactly as listed.

Identity and key modes are independent. The identity mode uses `DefaultAzureCredential` (managed identity in Azure and the configured developer credential chain locally); it does not fall back to a configured service key. Use user secrets for local key mode and never commit credentials:

```powershell
dotnet user-secrets set "ContentUnderstanding:Endpoint" "https://<resource>.services.ai.azure.com" --project src\DocAlchemy.Web
dotnet user-secrets set "ContentUnderstanding:AuthenticationMode" "ApiKey" --project src\DocAlchemy.Web
dotnet user-secrets set "ContentUnderstanding:ApiKey" "<content-understanding-key>" --project src\DocAlchemy.Web
```

For Storage key mode, set `Storage:BlobServiceEndpoint`, `Storage:ContainerName`, `Storage:AuthenticationMode` to `AccountKey`, `Storage:AccountName`, and `Storage:AccountKey` using the same user-secrets command. Do not configure unused key settings in identity mode.

SDK clients are singleton/thread-safe and are created without contacting Azure. They use bounded exponential retries and have content logging disabled. Application Insights captures operational requests/dependencies when configured; its HTTP instrumentation redacts query parameter values by default. The application does not instrument document bodies, extracted values, or analyzer JSON. Request trace/span IDs are added to logging scopes. Do not log credential values or raw service payloads.
