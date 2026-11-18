# DocAlchemy.Web

The .NET 10 Blazor Web App using Interactive Server rendering. It composes the Core contracts and Azure adapter projects through dependency injection.

## Accessible shell

The shared responsive shell provides the `Analyzers` and `Analyze document` destinations, a skip link, visible keyboard focus, a main-content landmark, and reduced-motion support. The `/analyzers` page lists analyzer metadata, supports refresh, and imports analyzer create-request JSON with a separately entered ID. The uploaded JSON is validated but not reserialized or modified, and the upload is bounded by `Uploads:MaxDocumentBytes` (20 MiB by default). Deletion is offered only for mutable analyzers that report the `delete` operation, and requires typing the analyzer ID to confirm. Loading this page invokes the configured analyzer service; `/analyze` remains a placeholder until the analysis workflow step.

The shared `StatusMessage` component uses visible state labels as well as color, announces routine status politely, and announces errors assertively. Dynamic messages render as encoded text. The CSS palette tokens match Section 7 of the repository plan:

| Purpose | Token | Value |
| --- | --- | --- |
| Main text, titles, header/navigation | `--palette-midnight` | `#011D4E` |
| Primary actions, underlined links, selected state | `--palette-primary` | `#0049DD` |
| Progress accents | `--palette-progress` | `#1B92FC` |
| AI badge/header accent | `--palette-ai-badge` | `#02E3FC` |
| Restrained decorative accent | `--palette-decorative` | `#41F6FB` |
| Information panels | `--palette-information` | `#BCFBFC` |
| Low-confidence/review warning | `--palette-warning` | `#FDAC02` |
| Page background | `--palette-page` | `#F5F9FF` |
| Cards, dialogs, inputs | `--palette-surface` | `#FFFFFF` |
| Secondary text | `--palette-secondary` | `#435B7D` |
| Input borders | `--palette-border` | `#7185A6` |
| Saving success | `--palette-success` | `#167044` |
| Analysis/saving errors | `--palette-error` | `#B42318` |

Build and run the focused Web checks with:

```powershell
dotnet build src\DocAlchemy.Web\DocAlchemy.Web.csproj
dotnet test tests\DocAlchemy.Web.Tests\DocAlchemy.Web.Tests.csproj
```

For an isolated shell browser check without Azure configuration, provide syntactically valid synthetic HTTPS endpoints as process-local environment variables before starting the Web project. The home page and `/analyze` placeholder do not make service requests. Opening `/analyzers` calls the configured analyzer service; use that route only when a configured service and authorized credentials are intentionally available. Do not use real credentials for a shell-only visual check.

Step 08 verification:

- `dotnet build src\DocAlchemy.Web\DocAlchemy.Web.csproj --no-restore` succeeded with 0 warnings and 0 errors; `dotnet test tests\DocAlchemy.Web.Tests\DocAlchemy.Web.Tests.csproj --no-restore` passed all 20 tests.
- Browser checks confirmed the home, Analyzers, and Analyze document routes; keyboard focus and the skip link; responsive navigation/cards at a 375px viewport with no horizontal overflow; and all 13 computed palette values.
- Contrast checks measured 16.32:1 for white on midnight, 7.01:1 for white on primary blue, 8.61:1 for midnight on warning amber, 6.56:1 for secondary text on the page background, 6.11:1 for success on white, and 6.57:1 for error on white.
- The visual check used synthetic endpoint settings and made no Azure requests.

## Analyzer management

The analyzer-management page uses `IAnalyzerService` for list, create, and delete operations. Create requests require a separately entered ID and a valid JSON object with `baseAnalyzerId`; uploaded JSON is sent unchanged, and the configured upload-size limit is enforced before submission. Confidence settings are inspected without changing the definition. Built-in, read-only, and delete-unsupported analyzers cannot be deleted; mutable analyzers require typing their exact ID to confirm.

The bUnit component tests use an in-memory service and synthetic JSON files. They cover list metadata, create/delete and refresh flows, protected analyzers, JSON/ID/size validation, confidence warnings, duplicate-submission protection, and service failures; they make no Azure requests.

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
