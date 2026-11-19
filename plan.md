# DocAlchemy implementation plan

## 1. Goal and current state

Build the DocAlchemy demo described in the attached `ContentUnderstanding.md`: create Azure Content Understanding analyzers from JSON, analyze documents, display dynamically extracted fields and confidence scores, and persist analysis results to Azure Blob Storage.

The repository currently contains a short root README, a license, a .gitignore, and local GitHub instructions/skills. There is no application, solution, infrastructure, or test suite to extend. Preserve the existing untracked `.github` content.

This is an implementation plan only. This revision changes the plan, not application or infrastructure files; it does not authorize dependency installation, subscription access, or provisioning. The repository-root `plan.md` is the human-readable source of truth for execution and handoffs. The earlier session copy is historical; SQL mirrors the executable steps but is not required to resume in a different chat.

## 2. Confirmed decisions and boundaries

| Area | Decision |
| --- | --- |
| Runtime | .NET 10 LTS, C# 14, Blazor Web App with Interactive Server |
| Hosting | Azure App Service Linux |
| Separation | Separate Core, Azure services, and frontend projects; UI uses service interfaces through dependency injection |
| Azure environment | New, parameterized environment; subscription and regions confirmed before provisioning |
| Deployment | Standard AZD layout with `src` and `infra`; Bicep infrastructure |
| Authentication | Anonymous end users; managed identity by default in Azure; optional key authentication for local testing |
| Scope | Only the requested demo features, supporting tests, and documentation; no synthetic demo documents or analyzer examples |
| Threshold | Configurable default `0.80`, valid inclusive range `[0, 1]` |
| Document upload | One document per analysis; default maximum `20 MiB` = `20,971,520` bytes |
| Missing confidence | Display `Not available`; preserve JSON null; never replace with zero |
| Analyzer import | Standard Content Understanding create-request JSON body, with analyzer ID supplied separately |
| Definition fidelity | Preserve the uploaded definition; warn if confidence is not enabled, without silently changing it |
| Storage failure | Display the completed results and a saving error; offer retry of persistence only |
| Exposure | Anonymous demo in a controlled environment; confirmation for deletion and an explicit operational warning in documentation |
| Language | All application strings, validation messages, comments, and repository documentation in English |

Out of scope: authentication UI, analysis history, document preview/editing, batch analysis, audio/video processing, model-management UI, analyzer editing, human-review workflows, queues, databases, CI/CD pipelines, private-network architecture, and production hardening beyond the safeguards needed for the demo.

## 3. Verified service constraints

1. Use Azure Content Understanding, not Azure Document Intelligence. Pin the stable .NET package `Azure.AI.ContentUnderstanding` at `1.1.0`; use .NET SDK feature band `10.0.4xx` and target `net10.0`. Select the then-current servicing patch when scaffolding and record that exact SDK in `global.json`.
2. Use GA data-plane API `2025-11-01`. Do not silently adopt `2026-06-01-preview`. The stable SDK exposes protocol overloads for raw analyzer JSON and binary input, so the API/schema combination is source-verified without adding a separate REST client.
3. Create analyzers with `PUT /contentunderstanding/analyzers/{analyzerId}?api-version=2025-11-01`; the path ID is canonical and matches `^[a-zA-Z0-9._-]{1,64}$`. The create body is the GA request resource (configuration, schema, model, and supported metadata); analyzer ID is supplied separately. Set `allowReplace=false` explicitly. Treat a duplicate as an error; never retry creation as replacement.
4. The SDK raw protocol overloads are the fidelity boundary for user definitions and analysis results: `CreateAnalyzerAsync` accepts `RequestContent` and returns `Operation<BinaryData>`; `AnalyzeBinaryAsync` accepts `RequestContent` plus a content type and returns `Operation<BinaryData>`. The pinned 1.1.0 pageable `GetAnalyzersAsync(CancellationToken)` returns `AsyncPageable<ContentAnalyzer>`; its `RequestContext` protocol overload is a single raw response, not a paged `BinaryData` sequence. Use the pageable model for metadata and preserve the original create JSON by passing it unchanged as `RequestContent`. Start create operations with `WaitUntil.Started` and poll with the configured timeout/interval and caller cancellation. Do not use typed SDK round-trips for user analyzer definitions or result field discriminators.
5. Analyzer listing is paginated (`nextLink`); consume every page. Use analyzer metadata and the documented prebuilt-analyzer catalog to establish modality. A custom analyzer's `baseAnalyzerId` identifies its base modality (`prebuilt-document`, `prebuilt-image`, `prebuilt-audio`, or `prebuilt-video`); there is no documented `isBuiltIn` property. Do not infer that the list contains every prebuilt analyzer or expose prebuilt IDs as mutable custom resources.
6. Confidence is opt-in. Analyzer configuration uses `estimateFieldSourceAndConfidence`; a field can set `estimateSourceAndConfidence`, which overrides the analyzer-level setting. For document analyzers, GA documentation supports confidence for extract/classify/generate field methods. Missing confidence remains null; do not synthesize it.
7. GA asynchronous document/text input formats include PDF/TIFF, JPEG/JPE/PNG/BMP/HEIF/HEIC, Office formats including DOCX, and text formats including TXT and Markdown. The service async ceiling is 200 MB (and format-specific page/character limits); this app's 20 MiB upload limit is intentionally stricter. Use the document-analysis path for images that contain documents. The API accepts a binary body; the SDK's content type defaults to `application/octet-stream`, and the app will send a server-selected canonical MIME type from its extension allowlist.
8. Result `contents` is an array and field values are a discriminated JSON union: string, date, time, number, integer, boolean, array, object, and JSON. Array items and object properties can nest. Preserve the returned `type`, typed JSON value, and nullable `confidence`; retain valid unknown future field nodes opaquely as JSON, and fail explicitly on malformed nodes rather than inventing values.
9. Analyzer creation and analysis are long-running operations. Use SDK `Operation<T>` with `WaitUntil.Started`, then cancellable bounded polling; creation and analysis have distinct operation results. The GA binary operation is `:analyzeBinary`; URL-based `:analyze` is not the upload path.
10. A Foundry resource alone is insufficient: the Content Understanding resource needs compatible deployed models and resource defaults. The GA defaults API is `PATCH /contentunderstanding/defaults?api-version=2025-11-01` with `modelDeployments`; it supports Microsoft Entra ID. Choose `gpt-5.2` version `2025-12-11` plus `text-embedding-3-large` as the current GA candidate for completion/embedding aliases. Check each analyzer's `supportedModels` and actual deployment availability before use; the older SDK README baseline is not a regional availability guarantee.
11. Use `Cognitive Services Content Understanding Owner` (`4b42bd01-da42-4c92-9b07-15ea5bd6a602`) for the Web App managed identity at the Foundry account scope. The service security page documents Owner as full Content Understanding data-plane access, including analyzer create/list/analyze/delete; Contributor lacks analyzer delete. The role name/capabilities are confirmed by Microsoft Learn, and the GUID matches the Azure-maintained built-in role catalog. For the separate defaults bootstrap, use Microsoft Entra ID via `DefaultAzureCredential` and assign `Cognitive Services User` (`a97b65f3-24c7-4388-baec-2e87135dc908`) to the bootstrap principal at the Foundry resource scope, as directed by the .NET SDK README for defaults configuration. The GA defaults API explicitly supports Entra ID. Keep deployment/role-assignment control-plane permissions separate from these data-plane roles; do not grant the bootstrap role to the Web App unless that identity itself performs bootstrap.
12. Model compatibility, model/deployment version, quota, and regional availability must be validated against the selected environment. Documentation verifies the GA API/model support list, not a particular resource, region, quota, or role assignment.

### Integration compatibility gate

Before building the UI, verify with official API/SDK contracts:

- SDK support for create/list/get/delete analyzers, binary document submission, operation polling, nullable confidence, nested fields, and multiple content items.
- Preservation of analyzer JSON properties. Prefer the SDK; use its protocol API where needed to avoid lossy typed deserialization. Add a narrowly scoped REST adapter only for an actual SDK gap.
- The exact create-request schema, analyzer-ID rules, built-in analyzer behavior, document-compatible analyzer detection, and pagination.
- GA-supported formats and confidence configuration, including any limitations for Office/text inputs. Unsupported confidence remains absent rather than synthesized.
- A compatible completion/embedding model set and its required named/default mappings. The SDK's documented `gpt-4.1`, `gpt-4.1-mini`, and `text-embedding-3-large` baseline is a starting candidate, not a promise of availability.
- Record the selected package versions, API version, model names/versions, and compatibility restrictions in the project documentation. If the GA path cannot meet a requirement, report the blocker before selecting a preview or reducing scope.

## 4. Solution structure

```text
DocAlchemy.slnx
global.json
azure.yaml
README.md
src/
  DocAlchemy.Core/
    Models/
    Services/
    Interfaces/
    Utilities/
    README.md
  DocAlchemy.ContentUnderstanding/
    Models/
    Services/
    Configuration/
    README.md
  DocAlchemy.Storage/
    Models/
    Services/
    Configuration/
    README.md
  DocAlchemy.Web/
    Components/
      Layout/
      Pages/
      Shared/
    Configuration/
    wwwroot/
    Program.cs
    appsettings.json
    README.md
tests/
  DocAlchemy.Core.Tests/
    README.md
  DocAlchemy.Web.Tests/
    README.md
  DocAlchemy.ContentUnderstanding.Tests/
    README.md
  DocAlchemy.Storage.Tests/
    README.md
infra/
  abbreviations.json
  main.bicep
  main.parameters.json
  modules/
    monitoring.bicep
    foundry.bicep
    storage.bicep
    keyvault.bicep
    frontend.bicep
    access.bicep
  README.md
scripts/
  azd/
    configure-content-understanding.ps1
```

Use .NET templates rather than hand-writing generated scaffolding. Keep project dependencies explicit:

- Core has no Azure SDK or UI dependencies.
- Azure references Core and owns all Foundry, Content Understanding, and Blob SDK calls.
- Web references Core and Azure only for application composition; Razor components inject Core service interfaces and do not instantiate SDK clients.
- Use one test project per application project: DocAlchemy.Core.Tests, DocAlchemy.Storage.Tests, DocAlchemy.Web.Tests, and DocAlchemy.ContentUnderstanding.Tests. Each directly references its corresponding project under src.
- Do not add a separate HTTP API, AppHost, Docker image, or agent framework: they are unnecessary for this server-rendered demo.

### Core contracts

- `IAnalyzerService`: asynchronously list, retrieve, create, and delete analyzers, with cancellation.
- `IDocumentAnalysisService`: analyze a validated document with the selected analyzer and return a normalized result.
- `IAnalysisResultStore`: persist the immutable normalized result and return a nonpublic storage reference.
- Shared models for analyzer metadata, recursive extracted fields, content-item grouping, analysis results, and storage references.
- Represent dynamic typed values with System.Text.Json types rather than flattening everything to strings. Preserve numbers, booleans, dates as returned, arrays, objects, nulls, and per-field nullable confidence.
- Keep the service's field `type` string and raw JSON value together; unknown future field types remain opaque JSON instead of being dropped or coerced. Only malformed required structure is an explicit mapping failure.
- Keep Azure configuration, client construction, request retries, and SDK-specific error translation in the Azure project.
- Put shared threshold comparison and result-contract rules in Core. Keep UI presentation and interaction state in Web.

## 5. Functional design

### Analyzer management

1. Load the complete analyzer listing, following service pagination.
2. Show ID, description, modality/capabilities, and status when available. Distinguish built-ins from user-created analyzers and reflect their supported operations.
3. Upload a JSON definition and enter an analyzer ID separately. Validate file size, JSON syntax, required request structure, and ID constraints before calling the service.
4. Preserve the definition's semantics, including extension properties supported by the chosen API. Reject conflicting IDs and duplicate creation explicitly; never turn creation into an implicit update.
5. Warn when effective analyzer/field settings do not enable confidence, or when the effective configuration cannot establish that it is enabled. Do not mutate the definition or existing analyzers.
6. Await asynchronous creation and show progress or actionable service errors; refresh the list after success.
7. Confirm deletion by showing the analyzer ID. Built-in/read-only analyzers cannot be deleted. Refresh after successful deletion.
8. Handle empty lists, authentication failures, conflicts, missing analyzers, throttling, and failed operations distinctly. Do not display an empty list as a substitute for a failed request.

### Document analysis

1. Select a document-compatible analyzer from the configured resource. Do not offer audio/video analyzers as valid document targets.
2. Upload exactly one supported input. Allow PDF, DOCX, TXT, MD, and document-image extensions supported by the pinned API, including JPEG, PNG, BMP, TIFF, HEIF, and HEIC where supported.
3. Validate extension, declared content type, nonempty content, and bounded stream size on the server. Do not trust browser-provided size or type alone.
4. Use Blazor streaming with an explicit maximum, not a large SignalR message-size workaround. Never persist the source document in Blob Storage; dispose request buffers/streams.
5. Capture the analyzer ID and threshold at analysis start. Disable duplicate submission while processing.
6. Submit to Content Understanding and poll the long-running operation with cancellation, a bounded timeout, and transient retries that honor service throttling. Do not automatically resubmit paid analysis after an uncertain submission outcome.
7. Render every returned content item and its recursive fields. Show field name/path, type, typed value, and confidence; retain nesting for arrays and objects.
8. Show an amber warning icon and English accessible label only when a numeric confidence is strictly less than the captured threshold. Equality is not a warning. Missing confidence displays `Not available`.
9. Handle zero extracted fields as a valid empty result. Unsupported or malformed service output must produce an explicit error, not fabricated values.
10. Automatically save each successfully normalized analysis, including empty field collections.
11. Show completed analysis and persistence status separately. If Blob saving fails, retain the immutable result in the current circuit and offer retry without repeating analysis.

### Persisted JSON contract

Store one UTF-8 `application/json` blob per analysis in a private configured container, using a server-generated analysis ID as the blob name.

| Property | Purpose |
| --- | --- |
| `schemaVersion` | Version of the application's persisted result format |
| `analysisId` | Stable identifier reused for saving retries |
| `analyzerId` | Analyzer used for the actual request |
| `confidenceThreshold` | Threshold captured at analysis start |
| `analyzedAtUtc` | Completion timestamp, unchanged on save retries |
| `contents` | All content items, each with a stable index and its extracted fields |
| Field `name`, `path`, `type` | Identification and dynamic type |
| Field `value` | Typed JSON value, preserving arrays, objects, and nulls |
| Field `confidence` | Service confidence or explicit null |
| Field `children` | Recursive fields/items where nested confidence must be retained |

Do not store the original document, API keys, public SAS URLs, or a separate copy of the complete raw service response. The normalized contract must retain all extracted field values and confidence scores, not just fields displayed on the first screen.

Saving retries use the same ID and identical serialized snapshot, making retries idempotent even if the prior upload succeeded but its response was lost. A refreshed/lost circuit does not provide durable recovery; document this limitation without adding a background job system.

## 6. Configuration, authentication, and errors

- Web settings include `Analysis:ConfidenceThreshold` and `Uploads:MaxDocumentBytes`, with the confirmed defaults. Bound analyzer JSON uploads explicitly as well, within the same maximum-upload policy.
- Bind and validate Content Understanding endpoint/API/auth settings and Blob endpoint/container/auth settings at startup.
- Freeze the configuration/provisioning contract: `ContentUnderstanding:Endpoint`, `ContentUnderstanding:ApiVersion` (`2025-11-01`), `ContentUnderstanding:AuthenticationMode` (`ManagedIdentity` or `ApiKey`), `ContentUnderstanding:ApiKey`; `Storage:BlobServiceEndpoint`, `Storage:ContainerName`, `Storage:AuthenticationMode` (`ManagedIdentity` or `AccountKey`), `Storage:AccountName`, `Storage:AccountKey`; `Analysis:ConfidenceThreshold`, `Analysis:OperationTimeout`, `Analysis:PollInterval`, and `Uploads:MaxDocumentBytes`. Azure environment variables use the standard double-underscore form. Defaults remain threshold `0.80`, max upload `20,971,520`, and managed identity for both services.
- Provisioning maps the Foundry resource endpoint and Blob service endpoint/container to those settings, sets the GA API version and numeric defaults, sets Application Insights through `APPLICATIONINSIGHTS_CONNECTION_STRING`, and uses a Key Vault reference for `ContentUnderstanding:ApiKey` only in explicitly selected key mode.
- Configure identity/key modes independently for Content Understanding and Storage. Require the correct credential inputs for the selected mode; never fall back silently from failed identity authentication to a key.
- Use managed identity in Azure. For local identity use the documented Azure credential chain; for local key mode use .NET user secrets or protected environment settings.
- Storage key mode uses the account name/key through the Blob SDK; Content Understanding uses its API key through the Content Understanding SDK.
- Keep secret values out of committed settings, deployment outputs, logs, browser code, and error messages.
- Provision the required Foundry key as a Key Vault secret. Use an App Service Key Vault reference when key mode is explicitly configured in Azure; managed identity remains the default.
- Treat service polling failures, authorization errors, throttling, unsupported inputs, and saving errors explicitly. Use structured server logs with correlation IDs and safe English UI messages.
- Application Insights records operational health, dependencies, and failures without document bodies, extracted values, or analyzer JSON.
- Respect cancellation when a component/circuit is disposed. Do not share mutable per-user analysis state through singleton services.

## 7. UI and accessibility

Create a small responsive shell with two primary pages: `Analyzers` and `Analyze document`.

Use the supplied palette directly, without substituting a different design system:

| Purpose | Color |
| --- | --- |
| Main text, titles, header/navigation | `#011D4E` |
| Primary actions, underlined links, selected state | `#0049DD` |
| Progress and illustration accents | `#1B92FC` |
| Small AI badges/header accents | `#02E3FC` |
| Restrained decorative accents | `#41F6FB` |
| Information panels/result headings | `#BCFBFC` |
| Low-confidence/review warnings | `#FDAC02` |
| Page background | `#F5F9FF` |
| Cards, dialogs, inputs | `#FFFFFF` |
| Secondary text | `#435B7D` |
| Input borders | `#7185A6` |
| Saving success | `#167044` |
| Analysis/saving errors | `#B42318` |

Implement palette tokens in frontend CSS. Use white text on dark headers and primary blue buttons, and midnight-blue text on pale/cyan/amber surfaces.

Provide keyboard navigation, visible focus, associated form labels, accessible progress/status announcements, and non-color warning indicators. Show loading, empty, failed, analyzing, saving, saved, and saving-failed states. Render uploaded and extracted content as encoded text, never executable markup.

## 8. Azure infrastructure and AZD

### Module responsibilities

| Module | Resources and responsibilities |
| --- | --- |
| `main.bicep` | Subscription-scope entry point, resource group, parameters, module composition, stable nonsecret outputs |
| `monitoring.bicep` | Log Analytics workspace and workspace-based Application Insights |
| `foundry.bicep` | Foundry account of the supported kind, endpoint/custom subdomain, identity where needed, and compatible model deployments |
| `storage.bicep` | General-purpose storage account, Blob service, private analysis-results container |
| `keyvault.bicep` | RBAC-enabled Key Vault and a Foundry account key stored directly as a secret, without key outputs |
| `frontend.bicep` | Linux App Service plan, .NET 10 Web App, system-assigned identity, monitoring and application settings |
| `access.bicep` | Role assignments after resource identities exist, avoiding frontend/Key Vault dependency cycles |

Use the canonical AZD `abbreviations.json` resource prefixes, combined with an environment name and deterministic uniqueness token. Interpret the specification's wording about a "suffix" consistently with standard AZD prefix-based resource naming.

Parameterize environment, primary and Foundry locations, SKU, model deployment names/versions/capacity, result-container name, threshold, and relevant auth settings. Keep a small single-instance App Service default suitable for a controlled demo; verify Linux .NET 10 runtime availability before fixing the SKU/runtime configuration.

Enable HTTPS, TLS minimums, App Service WebSockets, and Always On on a supporting SKU. Enable session affinity for Interactive Server; do not add Azure SignalR for a single-instance demo.

Keep Blob containers nonpublic and use identity by default. Account-key access remains available because it is an explicit local-test requirement; never expose keys to the browser.

### Access matrix

| Principal | Scope | Permission |
| --- | --- | --- |
| Web App managed identity | Foundry account | `Cognitive Services Content Understanding Owner` (the service-specific role documented to include create/list/analyze/delete; do not substitute subscription/resource-group Owner) |
| Web App managed identity | Results container | `Storage Blob Data Contributor` |
| Web App managed identity | Key Vault | `Key Vault Secrets User` |
| Deployment/bootstrap operator identity | Foundry account | `Cognitive Services User` (`a97b65f3-24c7-4388-baec-2e87135dc908`) for Entra-authenticated defaults configuration, as specified by the .NET SDK README; use `DefaultAzureCredential`. |
| Deployment/bootstrap operator | Deployment and role-assignment scopes | The separate control-plane permissions needed to provision resources and create the role assignments above; confirm the operator's actual access in the approved environment. |
| Foundry identity, if required by the selected model path | Required model resource | Only the documented model-inference permissions |

Do not confuse Content Understanding Owner with subscription/resource-group `Owner`. The selected service roles and GUIDs are documented above; verify that both built-in definitions are available and that the approved principal can receive the assignments at the intended scopes during environment preflight. Do not claim live authorization until tested against that environment.

### Provisioning and readiness

- `azure.yaml` declares one deployable C# `web` service hosted on App Service. Core and Azure are class libraries, not separate AZD services.
- Tag the Web App with the matching `azd-service-name`. Map AZD environment inputs through `main.parameters.json`.
- Output resource names, IDs, endpoints, and frontend URL only; never key values.
- Bicep provisions model deployments. A short idempotent post-provision script configures/verifies Content Understanding model defaults through its supported data-plane API.
- Run the bootstrap using the deployment operator's identity, with bounded handling of RBAC propagation. Fail clearly when defaults or deployments cannot be configured; do not mark the environment ready on partial success.
- Use PowerShell Core for the bootstrap and verify AZD hook support on the documented platforms. Supply a platform wrapper only if needed.
- The Azure CLI provisioning instructions call the same bootstrap explicitly, so the CLI and AZD paths result in equivalent ready environments.
- Exclude `.azure`, local secrets, build results, and generated deployment artifacts from source control.
- Confirm tenant, subscription, region/model availability, quota, and provisioning scope before any cloud-changing command. Planning does not authorize deployment.

## 9. Step-by-step execution and handoff

### How to execute one step

1. Choose the development model manually in the Copilot model selector before sending the step prompt. No model is assigned or switched automatically by this plan. Development-model selection is unrelated to the models deployed in Foundry.
2. Attach the current repository-root `plan.md`, and use the same worktree containing the previous steps' changes. A new chat or model must read the files and completion records, not rely on earlier conversation memory.
3. Select the next step whose dependencies are `done`. Numerical order is a safe default; the dependency column defines the actual prerequisites.
4. Copy that step's prompt from section 13. It authorizes only the specified step, its directly related tests/documentation, and its handoff record.
5. The executing model reads the repository instructions and relevant source, verifies prerequisite evidence, then changes the step status to `in_progress`.
6. Execute the bounded task, run its checks, and fix failures caused by that task. If a prerequisite, environment, or decision blocks completion, record `blocked` and explain what is missing instead of inventing a successful result.
7. Before finishing, update the row below and append a completion record under section 14. Mark `done` only when the step's acceptance criteria have evidence. A later cloud check is not evidence for an untested local step.
8. Stop after the step. The user chooses the model and starts the next step manually. Do not launch a chain of agents, automatically continue, commit, push, or provision outside the explicit step authorization.

The `Focus for manual model selection` column describes the work, not a model ranking or availability claim. It lets the user choose a model with suitable research, reasoning, coding, UI, or infrastructure capabilities without prescribing a provider or version.

### Execution register

All steps are initially `pending`; plan preparation does not count as implementation. Keep this register and the section 14 records current. If SQL tracking is available, mirror these IDs and dependencies there; the file remains authoritative across sessions.

| Step / tracking ID | Deliverable | Depends on | Focus for manual model selection | Status |
| --- | --- | --- | --- | --- |
| 01 / `step-01-contract` | Verified SDK/API, schemas, model mappings, and access contracts | None | Source-backed research; compatibility reasoning | done |
| 02 / `step-02-scaffold` | Compiling solution and test scaffolding | 01 | Precise .NET tooling and project setup | done |
| 03 / `step-03-core` | Core interfaces, recursive models, and result contract | 02 | Domain modeling; type safety; boundary tests | done |
| 04 / `step-04-config` | Validated settings, authentication modes, and DI | 03 | Azure SDK integration; credential correctness | done |
| 05 / `step-05-analyzers` | Analyzer management adapter and tests | 04 | API semantics; asynchronous operations | done |
| 06 / `step-06-extraction` | Analysis adapter and lossless result mapping | 04 | Complex mapping; polling and failure handling | done |
| 07 / `step-07-storage` | Idempotent Blob persistence and tests | 04 | Persistence consistency; serialization | done |
| 08 / `step-08-shell` | Accessible Blazor shell and palette | 04 | Blazor UI; CSS; accessibility | done |
| 09 / `step-09-analyzer-ui` | Complete analyzer-management page | 05, 08 | Interactive forms; component state | done |
| 10 / `step-10-results-ui` | Recursive results and confidence components | 03, 08 | Recursive UI; exact display semantics | done |
| 11 / `step-11-analysis-ui` | Upload-to-analysis-to-save workflow | 05, 06, 07, 08, 10 | Cross-service state; cancellation; retry correctness | done |
| 12 / `step-12-infra-base` | Naming, resource group, monitoring, and storage IaC | 04 | Bicep resource composition | done |
| 13 / `step-13-infra-foundry` | Foundry models and Key Vault IaC | 01, 12 | Model deployment contracts; secret handling | done |
| 14 / `step-14-infra-web` | App Service configuration and scoped role assignments | 04, 12, 13 | Dependency graphs; identity and runtime configuration | pending |
| 15 / `step-15-azd` | AZD service wiring and model-default bootstrap | 14 | Deployment lifecycle; idempotent scripting | pending |
| 16 / `step-16-docs` | Complete English project and deployment documentation | 09, 11, 15 | Technical writing grounded in implemented code | pending |
| 17 / `step-17-local-checks` | Integrated local tests, browser checks, and packaging | 16 | Systematic verification and debugging | pending |
| 18 / `step-18-preflight` | Read-only Azure readiness report and deployment proposal | 17 | Azure availability, quota, and scope assessment | pending |
| 19 / `step-19-deploy` | Explicitly approved provisioned and deployed environment | 18 | Controlled deployment and operational recovery | pending |
| 20 / `step-20-live-checks` | Real-service acceptance evidence and demo handover | 19 | End-to-end validation and diagnosis | pending |

Steps 05-08 can be undertaken in any order after 04; infrastructure can proceed independently after its prerequisites. This flexibility is for manually selecting the next task, not permission to run concurrent editors on shared files. Preserve common contracts and rerun impacted checks if a later step changes them.

Steps 01-17 must not create Azure resources or execute billable analysis. Step 18 confirms the target before read-only Azure checks. Steps 19-20 require explicit authorization for their cloud changes, costs, and test operations.

## 10. Validation and acceptance criteria

### Automated checks

- Build and test the solution with nullable reference types enabled.
- Test threshold values `0`, `0.80`, and `1`; reject out-of-range/nonfinite settings. At threshold `0.80`, confidence `0.79` warns, `0.80` and `0.81` do not, and null shows `Not available`.
- Verify the exact upload boundary: `20,971,520` bytes accepted by the application-size policy and the next byte rejected; use stream doubles, not large committed fixtures.
- Test invalid/empty files, malformed JSON, duplicate/conflicting analyzer IDs, confidence opt-out/inheritance, paginated lists, protected built-ins, missing/deleted analyzers, and unsupported modality.
- Test nested arrays/objects, all required scalar types, null values/confidence, empty results, and multiple content items without lost fields.
- Serialize and round-trip the exact persisted schema, including analyzer ID, original captured threshold, confidence nulls, and stable analysis ID/timestamp.
- Verify that a Blob failure preserves results, shows an error, and retry performs no second Content Understanding analysis call.
- Verify identity and key client selection, missing configuration failures, transient retry limits, cancellation, failed polling, and user-safe errors.
- Use test doubles or inline test data; do not add sample demo documents/analyzer packages excluded by the agreed scope.
- Run Bicep compilation/lint and verify `azure.yaml` discovery and packaging. Do not install unnecessary tooling.

### Browser checks

- Confirm both pages, responsive results, keyboard flow, accessible warning labels, and the supplied palette.
- Exercise loading, empty, failure, analysis-in-progress, saving, success, and saving-failure states.
- Verify extraction output is safely encoded and credentials never appear in browser-visible state.

### Real-service checks, separately authorized

- Verify selected regions support Content Understanding, the configured GA API, and the pinned deployments; verify usable quota and operator RBAC.
- Provision using the approved AZD environment, then verify model defaults and app readiness. Inspect changes with the appropriate Azure validation/what-if before provisioning.
- Use an operator-provided analyzer JSON and nonsensitive input documents; no real documents are committed.
- Create/list/delete a custom analyzer, and analyze PDF, an image, DOCX, TXT, and MD through compatible document analyzers.
- Verify every saved blob against the displayed normalized values, analyzer ID, and captured threshold.
- Test low and missing confidence deterministically in automated tests; live model output is not guaranteed to produce those cases on demand.
- Verify managed-identity access in Azure, local identity and key configuration, private-container behavior, and Key Vault references when key mode is exercised.
- Verify operational telemetry without document content or credentials.
- Verify the Azure CLI provisioning instructions against the same Bicep/bootstrap contract; execute an additional provisioning path only with explicit approval.
- If no environment has been approved, report cloud smoke tests as pending rather than claiming end-to-end Azure success.

## 11. Required documentation

Every C# project, including the test project, gets its own README explaining its purpose, features, significant classes/interfaces, and configuration or usage. Add concise Mermaid class diagrams where they clarify relationships.

The root README explains the demo, architecture, project layout, prerequisites, local execution, identity/key modes, configuration, upload/format/confidence limitations, analyzer JSON contract, result JSON contract, tests, deployment, monitoring, and controlled-environment warning.

The infrastructure README documents all modules, parameter/output contracts, AZD naming, roles, model dependencies and bootstrap, provisioning with both Azure CLI and AZD, application deployment, validation, troubleshooting, expected cost-bearing services, and safe cleanup of only the selected demo environment.

Explain that public anonymous access permits analyzer management and billable requests, that confidence can be absent despite successful extraction, and that unsaved results do not survive a lost circuit. Include no time estimates or sample document/analyzer deliverables.

## 12. Reference sources

- [Content Understanding .NET SDK](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ai.contentunderstanding-readme?view=azure-dotnet)
- [Content Understanding document solutions and confidence settings](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/document/overview)
- [Service quotas, supported inputs, and model support](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/service-limits)
- [Model deployments and resource-level defaults](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/models-deployments)
- [Security and service-specific RBAC roles](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/secure-communications)
- [Create AZD-compatible projects](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/make-azd-compatible)
- [AZD schema reference](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/azd-schema)
- [Canonical AZD abbreviations](https://github.com/Azure/azure-dev/blob/main/cli/azd/resources/scaffold/base/abbreviations.json)

The implementation must recheck changing model/runtime availability against the eventual deployment target. This plan does not assert that an Azure environment has been inspected or deployed.

## 13. Individually executable steps

Each prompt below includes the common execution protocol by reference to section 9. Read the entire specified step, not just its short prompt. The file scopes are starting points: only directly related changes are allowed, and any necessary contract change must be recorded for subsequent steps.

### Step 01 - Verify and freeze integration contracts

**Prerequisites:** None.

**Read/change scope:** Sections 3, 4, 6, and 8; official Microsoft API/SDK documentation. Record findings in section 14; do not scaffold application code yet.

**Actions:**
1. Revalidate all previously researched claims against the supported GA service and current SDK rather than treating the plan's package/role names as executable facts.
2. Select and record the .NET SDK and package/API versions, analyzer request shape and ID rules, JSON-preservation approach, supported input/MIME types, confidence inheritance, paging, and operation-polling methods.
3. Verify typed/recursive result shapes and how unknown field types are handled without losing data or fabricating values.
4. Identify compatible model deployments/default mappings, exact RBAC role definitions, and whether the proposed bootstrap API supports operator identity.
5. Record application configuration names and the provisioning-to-application contract used by later steps. Distinguish facts verified from documentation from region/quota checks deferred to step 18.

**Completion gate:** A source-linked compatibility record with concrete contracts; no unresolved SDK/schema/auth issue that would force speculative implementation. If GA cannot satisfy a requirement, stop for a decision.

**Prompt:**
> Execute only step 01 of the attached repository plan.md. Follow section 9. Verify the integration contracts with official sources and record the chosen contracts, evidence, and remaining environment-only checks in section 14. Do not implement the application or access an Azure subscription. Stop after the handoff.

### Step 02 - Scaffold the solution and test project

**Prerequisites:** Step 01.

**Read/change scope:** Root solution/SDK configuration, `.gitignore`, the  projects under `src`, and the test projects under `tests`.

**Actions:**
1. Read repository C# instructions and inspect installed tooling. Use .NET templates for Core, Azure services, the .NET 10 Interactive Server web app, and a test project.
2. Configure the dependency directions from section 4, nullable reference types, the pinned SDK policy, and a consistent test framework.
3. Remove irrelevant template demo pages; do not create fake application behavior or sample analyzer/document deliverables.
4. Create initial per-project README descriptions and ignore local credentials/build artifacts without overwriting user changes.

**Completion gate:** `dotnet build DocAlchemy.slnx` succeeds; the test project discovers and runs an initial meaningful scaffolding check. No secrets, cloud calls, or fabricated service implementations.

**Prompt:**
> Execute only step 02 of the attached repository plan.md. Follow section 9 and the step 01 compatibility record. Scaffold the solution with .NET tooling, verify project references/build/test discovery, update the affected READMEs and completion record, and stop. Do not implement later features.

### Step 03 - Define Core models and contracts

**Prerequisites:** Step 02.

**Read/change scope:** `DocAlchemy.Core`, Core tests, and its README; sections 4-5.

**Actions:**
1. Implement the three service interfaces with asynchronous operations, cancellation, and no Azure SDK types in their public contracts.
2. Model analyzer metadata, immutable analysis identity/threshold/timestamp, multiple content items, and recursively typed fields with nullable confidence.
3. Define JSON serialization rules and field-path conventions that remain unambiguous for nested arrays/objects and unusual field names.
4. Implement the shared strict-less-than threshold rule and test threshold boundaries, null confidence, typed values, nested results, and serialization round trips.

**Completion gate:** Focused Core tests pass; the persisted schema contains every property required by section 5; Core has no UI or Azure SDK dependency.

**Prompt:**
> Execute only step 03 of the attached repository plan.md. Follow section 9. Implement and test the Core interfaces, recursive result model, immutable analysis snapshot, threshold semantics, and JSON contract. Preserve the approved project boundaries, record public contracts in the handoff, and stop.

### Step 04 - Wire validated configuration and Azure clients

**Prerequisites:** Step 03.

**Read/change scope:** Azure configuration/DI, Web settings and `Program.cs`, related tests and READMEs; section 6.

**Actions:**
1. Add the selected SDK dependencies and options using the step 01 contract. Bind endpoint, container, auth-mode, threshold, upload, and operation-limit settings.
2. Validate required settings and bounds at startup, including nonfinite/out-of-range thresholds. Distinguish syntactic configuration validation from live resource reachability.
3. Construct Content Understanding and Blob clients for independently selected identity/key modes, with no automatic credential fallback.
4. Register suitable lifetimes, cancellation/retry policies, safe error/logging conventions, and the planned application telemetry integration. Keep mutable analysis state circuit-local.
5. Document local user-secret/environment configuration without inserting actual credentials.

**Completion gate:** Configuration and DI tests pass for both modes, missing settings, invalid endpoints/limits, and mixed service-auth modes. Client construction and tests do not contact Azure.

**Prompt:**
> Execute only step 04 of the attached repository plan.md. Follow section 9. Implement validated configuration and Azure client/DI composition, including independent identity/key modes and safe diagnostics. Test without cloud calls, document the exact settings consumed by infrastructure, record the handoff, and stop.

### Step 05 - Implement analyzer management services

**Prerequisites:** Step 04.

**Read/change scope:** Azure Content Understanding analyzer adapter, service tests, and Azure README.

**Actions:**
1. Implement listing across all pages, retrieval, creation from unchanged request JSON, and deletion.
2. Validate analyzer IDs and create-request structure; preserve supported extension properties and refuse implicit replacement.
3. Expose built-in/read-only and document-compatibility metadata needed by the UI.
4. Compute confidence-configuration warnings using the verified effective configuration rules without mutating definitions.
5. Handle long-running creation, cancellation, duplicate/missing analyzers, throttling, permission failures, and service errors explicitly.

**Completion gate:** Transport/mock tests cover pagination, exact request-body fidelity, confidence inheritance/overrides, duplicate IDs, deletion restrictions, asynchronous completion/failure, and cancellation.

**Prompt:**
> Execute only step 05 of the attached repository plan.md. Follow section 9. Implement the analyzer-management service against the verified SDK/API, including unchanged JSON import and confidence warnings. Verify request and failure semantics using isolated tests, update the handoff, and stop before UI implementation.

### Step 06 - Implement extraction and recursive result mapping

**Prerequisites:** Step 04.

**Read/change scope:** Azure document-analysis adapter and mapper, associated tests, and Azure README.

**Actions:**
1. Submit a bounded document stream using the verified asynchronous analysis API and poll with cancellation, timeout, and bounded transient retries.
2. Separate submission retry safety from polling retries so uncertain paid submissions are not blindly repeated.
3. Map every returned content item and field into Core models, retaining types, nesting, nulls, and confidence at the correct level.
4. Preserve analyzer ID and threshold captured for the request; set the completion timestamp once.
5. Make malformed or unsupported output an explicit diagnosable failure; preserve valid empty extraction results.

**Completion gate:** Isolated tests cover multiple content items, nested arrays/objects, all supported field types, missing confidence, empty results, failed/timeout operations, cancellation, and no unsafe analysis resubmission.

**Prompt:**
> Execute only step 06 of the attached repository plan.md. Follow section 9. Implement asynchronous document analysis and lossless recursive mapping with explicit failure behavior. Test polling and submission retry semantics without Azure, record the contracts and evidence, and stop.

### Step 07 - Implement JSON persistence to Blob Storage

**Prerequisites:** Step 04.

**Read/change scope:** Azure Storage adapter, serialization/storage tests, and Azure README.

**Actions:**
1. Serialize the immutable Core snapshot to UTF-8 JSON with the full section 5 contract and `application/json` content type.
2. Write to the configured private results container using a stable analysis-ID blob name.
3. Make saving retries reuse the same identity and payload, including the case where a successful upload response was lost.
4. Return a nonpublic storage reference; never save the original document, keys, SAS URLs, or an extra raw response.
5. Surface authorization, unavailable-container, and storage failures for the UI to handle separately from analysis errors.

**Completion gate:** Storage/serialization tests prove exact output shape, typed values and nulls, stable retry payload/name, and explicit failures. No test uploads to Azure.

**Prompt:**
> Execute only step 07 of the attached repository plan.md. Follow section 9. Implement and test idempotent JSON result persistence using the immutable Core contract. Verify content type, private reference semantics, and failed/lost-response retries; record the handoff and stop.

### Step 08 - Build the accessible Blazor shell

**Prerequisites:** Step 04.

**Read/change scope:** Web layouts, navigation, shared status primitives, CSS, component tests, and Web README.

**Actions:**
1. Create the DocAlchemy header/navigation and page layout for `Analyzers` and `Analyze document`.
2. Implement the exact section 7 colors as CSS tokens, including warning/success/error styles and focus states.
3. Add only shared UI elements actually needed for loading, error/status messages, and confirmations.
4. Verify responsive layout, keyboard navigation, labels, live status announcements, and readable contrast; do not add a UI framework solely for styling.

**Completion gate:** Web build and focused component checks pass; the shell is visually checked in a browser without invoking Azure services. Test doubles stay in tests, not in a fake application backend.

**Prompt:**
> Execute only step 08 of the attached repository plan.md. Follow section 9. Build and verify the accessible Blazor shell and exact approved palette without implementing business pages or invoking Azure. Update the Web README and handoff with browser/check evidence, then stop.

### Step 09 - Build the analyzer-management page

**Prerequisites:** Steps 05 and 08.

**Read/change scope:** Analyzer page and directly related components/tests, Web README.

**Actions:**
1. Connect listing, metadata, empty/loading/error states, and refresh to `IAnalyzerService`.
2. Add the bounded JSON upload and separate analyzer-ID input, validation messages, and non-mutating confidence warning.
3. Show creation progress and conflicts; enable only supported operations and require explicit deletion confirmation with the ID.
4. Avoid duplicate submissions and handle disposal/cancellation without losing actionable errors.

**Completion gate:** Component tests verify list/create/delete flows, protected analyzers, JSON errors and size limits, confidence warnings, refresh behavior, and service failures with no fake-success states.

**Prompt:**
> Execute only step 09 of the attached repository plan.md. Follow section 9. Implement the analyzer-management page using the existing service interface, including unchanged JSON import and confirmed deletion. Run focused component tests, record the handoff, and stop.

### Step 10 - Build dynamic result and confidence components

**Prerequisites:** Steps 03 and 08.

**Read/change scope:** Shared results components, their component tests, and Web README.

**Actions:**
1. Render all content items and recursively nested fields without relying on a fixed analyzer schema.
2. Display unambiguous paths/names, field types, typed values, and confidence while safely encoding extracted content.
3. Use the captured threshold and shared rule: below it shows the amber icon plus accessible text; equal/above does not; null shows `Not available`.
4. Support empty results and large/nested layouts without adding document preview or editing.

**Completion gate:** Component tests cover `0.79`, `0.80`, `0.81`, and null at threshold `0.80`, nested arrays/objects, multiple content items, null/empty values, and encoded markup.

**Prompt:**
> Execute only step 10 of the attached repository plan.md. Follow section 9. Implement and test schema-independent recursive results and confidence components, including exact boundary behavior and safe output encoding. Do not implement the analysis workflow yet; record the handoff and stop.

### Step 11 - Connect upload, analysis, results, and saving

**Prerequisites:** Steps 05, 06, 07, 08, and 10.

**Read/change scope:** Analysis page, upload validation, circuit-local orchestration/state, component/integration tests, and Web README.

**Actions:**
1. List document-compatible analyzers and accept one supported document through bounded server-side streaming.
2. Validate extension/content type, empty content, and actual bytes, including the exact configured maximum. Dispose input streams and do not save source documents.
3. Capture analyzer and threshold once, prevent overlapping submissions, and show analysis progress and failures.
4. Render normalized results and automatically persist them, with separate analysis and storage states.
5. On saving failure retain the snapshot and expose retry of saving only; prevent retries from using a later analysis or changed threshold.
6. Handle circuit disposal/cancellation and document that unsaved in-memory results are lost on a new circuit.

**Completion gate:** Tests prove the entire isolated workflow, exact `20,971,520`/`20,971,521` byte boundary, success and empty results, invalid input, cancellation, and Blob retry without a second analysis invocation.

**Prompt:**
> Execute only step 11 of the attached repository plan.md. Follow section 9. Wire the complete analysis page to the existing services and result components. Test upload boundaries, immutable snapshot/state handling, and storage-only retry without Azure. Record the evidence and handoff, then stop.

### Step 12 - Create base Bicep, monitoring, and storage

**Prerequisites:** Step 04.

**Read/change scope:** `infra/main.bicep`, `main.parameters.json`, `abbreviations.json`, monitoring/storage modules, and infrastructure README.

**Actions:**
1. Establish the subscription-scope entry point, environment parameters, resource group, canonical AZD prefixes, deterministic naming, and tags.
2. Create Log Analytics and workspace-based Application Insights.
3. Create Storage and a private results container matching the application configuration contract; preserve optional key support without publishing keys.
4. Define module contracts and safe outputs for the later Foundry/Web modules.

**Completion gate:** Bicep compilation/lint passes for the current composition; parameter/output names match step 04; no secret outputs or public container. No provisioning or subscription lookup.

**Prompt:**
> Execute only step 12 of the attached repository plan.md. Follow section 9. Create the base Bicep composition, canonical AZD naming, monitoring, and private results storage. Validate locally, document module contracts, record the handoff, and stop without provisioning.

### Step 13 - Add Foundry models and Key Vault

**Prerequisites:** Steps 01 and 12.

**Read/change scope:** Foundry/Key Vault modules, main composition and parameters, infrastructure README.

**Actions:**
1. Implement the verified Foundry account/resource shape, supported model deployments, and explicit region/version/capacity parameters.
2. Add identities and model permissions only when required by the verified service path.
3. Create an RBAC-enabled Key Vault and persist a Foundry key as a secret without returning it as a module or deployment output.
4. Expose nonsecret resource identifiers and secret-reference metadata needed by App Service, keeping the dependency graph acyclic.
5. Keep regional capacity checks deferred to step 18; do not describe a Bicep build as evidence of deployability.

**Completion gate:** Integrated Bicep compilation/lint succeeds; deployed-model and bootstrap parameter contracts agree with step 01; no plaintext secret parameters/outputs/logging.

**Prompt:**
> Execute only step 13 of the attached repository plan.md. Follow section 9. Add Foundry/model deployments and Key Vault using the verified contracts, preserving secret handling and acyclic dependencies. Validate locally, document deferred regional checks, update the handoff, and stop.

### Step 14 - Add App Service and role assignments

**Prerequisites:** Steps 04, 12, and 13.

**Read/change scope:** Frontend/access modules, main composition, application settings mapping, infrastructure README.

**Actions:**
1. Create the Linux plan and .NET 10 Web App with system-assigned identity, HTTPS/TLS settings, WebSockets, affinity, and Always On on a compatible SKU.
2. Map validated application settings, endpoints, monitoring, threshold, upload limit, and default identity modes.
3. Support explicit key mode with Key Vault references where specified; never embed secret values in public settings documentation or outputs.
4. Assign verified deletion-capable Content Understanding access, Blob data access at the results scope, and Key Vault secret access.
5. Set AZD service-discovery tags and stable nonsecret deployment outputs.

**Completion gate:** Bicep compilation/lint succeeds; setting names and types match application options; role scopes and module dependencies are correct; no provisioning.

**Prompt:**
> Execute only step 14 of the attached repository plan.md. Follow section 9. Add App Service, application settings, and least-privilege role assignments covering the required operations. Validate the complete Bicep graph and configuration contracts, record the handoff, and stop without deploying.

### Step 15 - Wire AZD and the model-default bootstrap

**Prerequisites:** Step 14.

**Read/change scope:** `azure.yaml`, `scripts/azd/configure-content-understanding.ps1`, related infrastructure parameters and README.

**Actions:**
1. Declare the single C# App Service deployment and bind it to the correct project, Bicep entry point, tag, and output names.
2. Implement the idempotent post-provision defaults configuration using the verified API and deployment operator's identity.
3. Validate model readiness and existing defaults before mutation; implement bounded RBAC-propagation handling and explicit partial-failure reporting.
4. Document the same script's invocation after Azure CLI provisioning. Define the PowerShell prerequisites and supported host platforms.
5. Test script logic with mocked service responses, check syntax, and run `azd package` with no provisioning hooks executed.

**Completion gate:** AZD packages the Web project; the script has tested success/already-configured/failure paths; CLI and AZD readiness contracts are equivalent. No live bootstrap, `azd up`, `azd provision`, or `azd deploy`.

**Prompt:**
> Execute only step 15 of the attached repository plan.md. Follow section 9. Wire AZD and implement the idempotent Content Understanding defaults bootstrap. Validate packaging and script behavior without cloud changes, document the shared Azure CLI path, record the handoff, and stop.

### Step 16 - Complete and reconcile documentation

**Prerequisites:** Steps 09, 11, and 15.

**Read/change scope:** Root README, every C# project's README including tests, infrastructure README, and relevant handoff decisions.

**Actions:**
1. Reconcile documentation against the actual implementation rather than copying aspirational behavior from the plan.
2. Explain architecture, significant classes/interfaces, configuration, credentials, local execution, validation, analyzer/result contracts, and troubleshooting.
3. Add concise Mermaid class/architecture diagrams where useful and document both AZD and Azure CLI provisioning/bootstrap/application-deployment paths.
4. Explain costs, anonymous-access risks, confidence limitations, supported inputs, pending live checks, and scoped cleanup.
5. Keep everything in English; do not introduce sample document/analyzer deliverables or claim an unexecuted deployment succeeded.

**Completion gate:** All four C# projects and infrastructure have accurate READMEs; documented paths/settings/commands match implemented contracts; no credential values or unsupported success claims.

**Prompt:**
> Execute only step 16 of the attached repository plan.md. Follow section 9. Complete and reconcile all required English READMEs against the code and infrastructure, including operational limitations and both provisioning paths. Do not change behavior or execute deployment commands; record the handoff and stop.

### Step 17 - Verify the complete local solution

**Prerequisites:** Step 16.

**Read/change scope:** Full solution for validation; only fixes directly needed for the approved features and their tests/documentation.

**Actions:**
1. Run the full solution build/tests and a Release publish of the Web project. Use the actual configured test tooling.
2. Recheck the complete section 10 boundary, JSON-shape, cancellation, credential-selection, and storage-retry acceptance matrix.
3. Compile/lint Bicep and package with AZD; check that service paths/settings/outputs agree.
4. Use component tests and isolated browser checks for loading, error, results, accessibility, responsive layout, and palette. Keep doubles in test infrastructure, never as application fallback behavior.
5. Inspect diagnostics for accidental sensitive data; fix relevant failures and rerun the affected checks.

**Completion gate:** Local acceptance checks pass with recorded commands/results. Any live-data browser checks that require Azure are explicitly deferred to step 20, not claimed as passed.

**Prompt:**
> Execute only step 17 of the attached repository plan.md. Follow section 9. Validate the full local solution, required behavior, browser/component states, Bicep, Release publish, and AZD package. Fix relevant defects, record actual evidence and deferred live checks, then stop without accessing Azure.

### Step 18 - Assess Azure readiness without provisioning

**Prerequisites:** Step 17.

**Read/change scope:** Deployment parameters and documentation for inspection; sanitized findings in section 14. No Azure resource mutations.

**Actions:**
1. Ask the user to confirm tenant, subscription, AZD environment name, regions, and intended resource scope before authenticated inspection.
2. Check supported service/model regions, model versions and deployment types, usable quota, Linux runtime/SKU availability, provider readiness, and operator permissions.
3. Prepare a proposed resource inventory and parameters with cost-bearing services identified. Never register providers, grant roles, request quota increases, or change subscriptions/resources silently.
4. Run appropriate nonprovisioning validation/what-if only within the confirmed scope and permissions. Report prerequisites that prevent it.
5. Provide the exact deployment scope and operations to authorize in step 19, plus rollback/cleanup considerations that preserve unrelated resources.

**Completion gate:** The target and readiness evidence are recorded without secrets; blockers are resolved or the step remains `blocked`. Readiness is not authorization to deploy.

**Prompt:**
> Execute only step 18 of the attached repository plan.md. Follow section 9. First confirm the Azure target with me, then perform read-only readiness and quota/compatibility checks and permitted validation/what-if. Do not provision, grant roles, register providers, or run the bootstrap. Record the proposed step 19 scope and stop.

### Step 19 - Provision and deploy the approved environment

**Prerequisites:** Step 18 and explicit approval of the target, resource changes, and expected cost-bearing services.

**Read/change scope:** Approved local AZD environment state and deployment outputs; targeted application/IaC fixes only if needed and documented.

**Actions:**
1. Reconfirm the step 18 target and request approval for provisioning/deployment; selecting this step alone must not bypass a missing target approval.
2. Run the applicable validation workflow before deployment, then provision the approved resources using AZD and execute the verified bootstrap.
3. Verify model-default readiness and scoped access before deploying the Web application and checking its health/readiness.
4. Diagnose partial failures from actual evidence. Do not delete/recreate unrelated resources, silently broaden scope, or perform an additional CLI deployment for comparison.
5. Save only nonsecret deployment references locally; record actual completion separately from planned changes.

**Completion gate:** Approved resources, model defaults, Web deployment, and basic readiness are verified. Application functional acceptance remains step 20.

**Prompt:**
> Execute only step 19 of the attached repository plan.md. Follow section 9. Confirm the target and obtain explicit authorization for the proposed provisioning/deployment before cloud changes. Validate, provision, bootstrap, deploy, and verify readiness within that scope. Record actual results and stop before functional smoke tests.

### Step 20 - Run live acceptance checks and hand over the demo

**Prerequisites:** Step 19; user authorization for test analyzer creation/deletion, billable analyses, and saved test results.

**Read/change scope:** Deployed demo and operator-provided nonsensitive inputs; focused fixes and acceptance/documentation records.

**Actions:**
1. Obtain a compatible analyzer definition and nonsensitive PDF, document image, DOCX, TXT, and MD inputs from the operator. Do not create excluded demo samples or commit their documents.
2. Exercise the real management and analysis UI, including create/list/delete of only the designated test analyzer and all required input types.
3. Compare saved JSON to displayed results, analyzer ID, captured threshold, typed/nested fields, and confidence. Verify private Blob access and telemetry without content leakage.
4. Verify identity use in Azure and authorized local identity/key setups. Use deterministic automated tests for low/missing-confidence and fault cases that cannot be reproduced reliably or safely against live services.
5. Verify deployment/bootstrap repeatability without changing scope, reconcile READMEs, and record remaining limitations or blockers. Test-resource cleanup needs an explicit bounded scope; preserve the demo environment for use.

**Completion gate:** Evidence covers real analyzer CRUD, all requested document formats, persistence, credential modes, and monitoring. Missing inputs/permissions leave the affected acceptance criteria pending rather than weakening the requirement.

**Prompt:**
> Execute only step 20 of the attached repository plan.md. Follow section 9. Confirm the permitted live test operations and request my nonsensitive inputs. Validate the deployed demo end to end, compare stored JSON with the UI, verify auth/monitoring, and record the acceptance matrix and handover. Do not delete the environment or unrelated resources.

## 14. Per-step completion records

Step 01 is complete; its verified contracts, role evidence, and intentionally deferred environment checks are recorded below. Append one record per attempted step using the structure below. Keep existing evidence; if a retry supersedes a failed attempt, explain what changed. Store no keys, tokens, document contents, or sensitive account details here.

### Step 01 / `step-01-contract`

```text
Step: 01 / step-01-contract
Status: done
Development model: not recorded
Prerequisites checked: None; read Sections 3, 4, 6, 8, 9, 12, and 13 and repository C# instructions.
Files changed: plan.md
Contracts/decisions:
  - .NET target net10.0; SDK feature band 10.0.4xx, with exact servicing patch to be pinned at Step 02. Stable Azure.AI.ContentUnderstanding 1.1.0; GA data-plane API 2025-11-01. No preview API/package.
  - Create uses the analyzer ID in the path (1-64 characters; ^[a-zA-Z0-9._-]{1,64}$) and a GA create-request JSON body. Pass raw RequestContent; explicitly pass allowReplace=false. A duplicate must fail, not replace. The REST request schema does not list analyzerId as a body property; keep the separately entered path ID authoritative and reject a conflicting/unsupported duplicate rather than mutating uploaded JSON.
  - Use the stable SDK protocol surface for lossless create/analyze JSON: CreateAnalyzerAsync(RequestContent) -> Operation<BinaryData>; AnalyzeBinaryAsync(RequestContent, contentType) -> Operation<BinaryData>. The pinned pageable GetAnalyzersAsync(CancellationToken) returns AsyncPageable<ContentAnalyzer>; its RequestContext overload is a single raw response. Use pageable typed analyzer metadata, never typed round-trips for user create bodies or result fields.
  - Use :analyzeBinary for a single uploaded document, not URL-based :analyze. Use WaitUntil.Started and cancellable, bounded Operation polling. The request body is binary; SDK contentType defaults to application/octet-stream. App allowlist mapping: PDF application/pdf; DOCX application/vnd.openxmlformats-officedocument.wordprocessingml.document; TXT text/plain; MD text/markdown; JPEG image/jpeg; PNG image/png; BMP image/bmp; TIFF image/tiff; HEIF image/heif; HEIC image/heic. The service documents supported extensions, while the exact MIME mapping is an application-side contract.
  - API supports async PDF/TIFF/JPEG/JPE/PNG/BMP/HEIF/HEIC, DOCX and other Office types, and TXT/MD plus other text types; service ceiling is 200 MB and type-specific page/character limits. App's 20 MiB bound remains. Image documents use document analysis.
  - The list response is paged by nextLink. No documented isBuiltIn flag exists; use the official prebuilt catalog and analyzer metadata, and use custom baseAnalyzerId for modality. Do not assume the list includes all prebuilt analyzers or permit prebuilt mutation. Document custom analyzers are those based on prebuilt-document.
  - Effective confidence: analyzer config estimateFieldSourceAndConfidence defaults off; per-field estimateSourceAndConfidence overrides it. GA document analyzer confidence is supported across extract/classify/generate methods. Preserve absent confidence as null.
  - Normalize contents as an array; retain field type and exact JSON value recursively, including arrays, objects, and JSON fields. Preserve valid unrecognized future type nodes opaquely; fail malformed nodes without fabricated values.
  - Candidate GA model set: gpt-5.2 (model version 2025-12-11) and text-embedding-3-large. Resource-default mappings use prebuilt-analyzer-completion and prebuilt-analyzer-embedding aliases to the deployment names, plus concrete model-name mappings where required. The mini alias is only added if an actual selected analyzer's supportedModels requires it. GET analyzer supportedModels is authoritative; target-region capacity remains unverified.
  - App/provisioning keys: ContentUnderstanding:Endpoint, ContentUnderstanding:ApiVersion, ContentUnderstanding:AuthenticationMode, ContentUnderstanding:ApiKey; Storage:BlobServiceEndpoint, Storage:ContainerName, Storage:AuthenticationMode, Storage:AccountName, Storage:AccountKey; Analysis:ConfidenceThreshold, Analysis:OperationTimeout, Analysis:PollInterval; Uploads:MaxDocumentBytes; APPLICATIONINSIGHTS_CONNECTION_STRING. Identity mode is default; keys are independently opt-in per service.
  - App identity contract: assign `Cognitive Services Content Understanding Owner` (`4b42bd01-da42-4c92-9b07-15ea5bd6a602`) to the Web App managed identity at the Foundry account scope. This service-specific Owner role covers CRUD/analyze; Contributor lacks delete.
  - Bootstrap contract is separate: the SDK README says to assign `Cognitive Services User` (`a97b65f3-24c7-4388-baec-2e87135dc908`) to the principal configuring model defaults, including when that principal is a resource owner. Use `DefaultAzureCredential` and `PATCH /contentunderstanding/defaults` with Entra ID; keep deployment and role-assignment control-plane permissions separate.
Evidence (official sources, reviewed 2026-10-23):
  - SDK package/readme and async-operation behavior: https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ai.contentunderstanding-readme?view=azure-dotnet
  - SDK raw create overload: https://learn.microsoft.com/en-us/dotnet/api/azure.ai.contentunderstanding.contentunderstandingclient.createanalyzerasync?view=azure-dotnet
  - SDK raw binary operation and contentType: https://learn.microsoft.com/en-us/dotnet/api/azure.ai.contentunderstanding.contentunderstandingclient.analyzebinaryasync?view=azure-dotnet
  - SDK analyzer-list pageable surface: https://learn.microsoft.com/en-us/dotnet/api/azure.ai.contentunderstanding.contentunderstandingclient.getanalyzersasync?view=azure-dotnet
  - GA create body, ID constraint, allowReplace, and operation: https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers/create-or-replace?view=rest-contentunderstanding-2025-11-01
  - GA list/nextLink: https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers/list?view=rest-contentunderstanding-2025-11-01
  - GA binary input and operation response: https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers/analyze-binary?view=rest-contentunderstanding-2025-11-01
  - GA result schema, content array, and field union: https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers/get-result?view=rest-contentunderstanding-2025-11-01
  - GA analyzer operation polling: https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers/get-operation-status?view=rest-contentunderstanding-2025-11-01
  - GA model defaults API and mappings: https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers/update-defaults?view=rest-contentunderstanding-2025-11-01 and https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/models-deployments
  - Supported models, versions, input types and service limits: https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/service-limits
  - Confidence field inheritance and analyzer schema: https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/analyzer-reference
  - Prebuilt analyzers and modality catalog: https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/prebuilt-analyzers
  - Content Understanding role names/capabilities: https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/secure-communications
  - Content Understanding Owner GUID cross-check (Azure-maintained role catalog): https://github.com/Azure/Azure-Sentinel/blob/master/Sample%20Data/Feeds/AzureBuiltInRole.csv
  - Cognitive Services User built-in role definition and GUID: https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles/ai-machine-learning#cognitive-services-user
  - SDK identity/defaults guidance: https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ai.contentunderstanding-readme?view=azure-dotnet
  - .NET 10 SDK lifecycle/feature bands: https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs#lifecycle
Checks executed:
  - Official Microsoft Learn documentation searches and full-page fetches for the API, SDK, model, role, confidence, formats, and .NET SDK contracts. Verified the current stable SDK docs describe v1.1.0 and SDK API pages list the raw protocol overloads.
  - Step 05 compiled against the pinned 1.1.0 package and confirmed `GetAnalyzersAsync(CancellationToken)` yields pageable typed `ContentAnalyzer` items; corrected the earlier listing return-type note above.
  - Cross-checked the user-provided Content Understanding Owner GUID against the Azure-maintained built-in role catalog; verified its capabilities against the Microsoft Learn Content Understanding security page.
  - Verified `Cognitive Services User` GUID in Microsoft Learn built-in role documentation and Azure-maintained repositories; confirmed the .NET SDK README explicitly uses that role for configuring defaults. Confirmed the GA defaults API declares Microsoft Entra ID OAuth support.
  - Read-only repository review only. No application, infrastructure, dependency, or test files were created or changed.
Checks not executed:
  - No Azure subscription/resource access, role lookup, API call, deployment, model operation, or billable analysis, per request.
  - No local package restore/build or installed SDK inventory; there is no solution yet and exact SDK servicing patch is selected at Step 02.
  - No target-region/model availability, quota, model deployment, or role-assignment test; these require the approved environment. Do not claim these checks passed.
Open issues:
  - No contract blocker remains. Target-resource role assignment, operator control-plane access, analyzer `supportedModels`, model deployment/version/region/capacity/quota, and actual input acceptance remain environment checks for the approved target; no Azure subscription was accessed in Step 01.
Handoff: Step 01 is done. Dependent steps may proceed using the recorded role IDs and separated principals. At environment preflight, verify role availability/scope and operator authorization; after provisioning, verify the Web App managed identity can call Content Understanding and that the bootstrap identity can read/update defaults. Do not claim those live checks passed until executed. Stop here for manual selection of the next step.
```

### Step 02 / `step-02-scaffold`

```text
Step: 02 / step-02-scaffold
Status: done
Development model: not recorded
Prerequisites checked: Step 01 is done; reviewed its contract and handoff in section 14, section 4 project boundaries, section 9 execution protocol, and .github/instructions/csharp.instructions.md.
Files changed: .gitignore, README.md, Directory.Build.props, DocAlchemy.slnx, global.json, src/DocAlchemy.Core/DocAlchemy.Core.csproj, src/DocAlchemy.Core/README.md, src/DocAlchemy.ContentUnderstanding/DocAlchemy.ContentUnderstanding.csproj, src/DocAlchemy.ContentUnderstanding/README.md, src/DocAlchemy.Storage/DocAlchemy.Storage.csproj, src/DocAlchemy.Storage/README.md, src/DocAlchemy.Web/DocAlchemy.Web.csproj, src/DocAlchemy.Web/README.md, generated Web scaffold under src/DocAlchemy.Web, tests/DocAlchemy.Tests/DocAlchemy.Tests.csproj, tests/DocAlchemy.Tests/README.md, tests/DocAlchemy.Tests/ArchitectureTests.cs, deleted generated Class1.cs files and template Home.razor/UnitTest1.cs.
Contracts/decisions:
  - Pinned SDK 10.0.403 in global.json with latestPatch roll-forward; all projects target net10.0 and Directory.Build.props enables C# 14, nullable reference types, and implicit usings.
  - Initially used one xUnit test project; superseded by the user-requested per-project test structure recorded below. Core has no project dependencies; ContentUnderstanding and Storage each reference Core; Web references Core and both adapters.
  - Created the .NET 10 Blazor Web App with Interactive Server rendering and removed the generated Hello World page and placeholder library/test classes. No demo analyzers/documents or service behavior were added.
  - Added project READMEs and root build/test/run instructions. Extended .gitignore for local AZD environments and machine-specific/secrets JSON files while preserving existing ignore rules.
  - Added a meaningful architecture test asserting that the Core assembly does not reference Azure SDK or ASP.NET Core assemblies.
Checks executed:
  - dotnet --version: 10.0.403.
  - dotnet build DocAlchemy.slnx: succeeded, 0 warnings and 0 errors.
  - dotnet test DocAlchemy.slnx --no-build: passed, 1 test discovered and passed.
  - dotnet sln DocAlchemy.slnx list: confirmed the four application projects and single test project are included.
  - git diff --check: passed with no whitespace errors.
Checks not executed:
  - No Azure resource access, credentials, deployments, billable requests, or application/service acceptance checks; those are outside this step.
Open issues: None for the step 02 acceptance criteria.
Handoff: Step 03 may add Core contracts/models and Core-focused tests. Keep Core free of Azure SDK and UI dependencies. No later feature or Azure service implementation was added; stop for manual selection of the next step.
```

#### Step 02 follow-up - Per-project test structure

```text
Status: done
Reason: User requested one test project for every project under src, superseding the initial single-project test decision.
Files changed: DocAlchemy.slnx, README.md, plan.md, tests/DocAlchemy.Core.Tests, tests/DocAlchemy.Storage.Tests, tests/DocAlchemy.Web.Tests, tests/DocAlchemy.ContentUnderstanding.Tests; removed tests/DocAlchemy.Tests.
Contracts/decisions:
  - Four .NET 10 xUnit projects created with dotnet templates. Each directly references only its corresponding application project.
  - Moved the existing architecture test into DocAlchemy.Core.Tests and updated its namespace.
  - Removed generated placeholder tests. The other three test projects intentionally contain no tests until their application behavior is implemented.
  - Updated section 4 and project READMEs to require the per-project structure for subsequent steps.
Checks executed:
  - dotnet build DocAlchemy.slnx: succeeded, 0 warnings and 0 errors.
  - dotnet test DocAlchemy.slnx --no-build: Core architecture test passed; the other three projects reported no available tests, as expected for these empty scaffolds.
  - dotnet sln DocAlchemy.slnx list: confirmed four application projects and four corresponding test projects; obsolete DocAlchemy.Tests is no longer included.
  - git diff --check: passed.
Open issues: None for the structural correction.
Handoff: Add future tests to the project matching the application project under test; do not restore the combined test project.
```

```text
Step: <two-digit number and tracking ID>
Status: <in_progress | done | blocked>
Development model: <user-selected model, if known; otherwise "not recorded">
Prerequisites checked: <step numbers and evidence inspected>
Files changed: <repository-relative paths>
Contracts/decisions: <concrete API, schema, settings, naming, or behavior decisions>
Checks executed: <commands or browser/test actions, with actual outcomes>
Checks not executed: <reason; distinguish blocked from intentionally later checks>
Open issues: <none, or exact blocker and required user/environment input>
Handoff: <what the next step must know; no automatic execution>
```

A step's record must be sufficient for a different model in a new chat to continue from the repository, this plan, and the recorded evidence. If an earlier contract changes, identify dependent steps whose checks need to be rerun before their completion status remains valid.

### Step 03 / `step-03-core`

```text
Step: 03 / step-03-core
Status: done
Development model: not recorded
Prerequisites checked: Step 02 is done; reviewed sections 4-5 and 9, the Step 02 handoff, the Core project/test scaffolds, and .github/instructions/csharp.instructions.md.
Files changed: src/DocAlchemy.Core/Interfaces/IAnalyzerService.cs, src/DocAlchemy.Core/Interfaces/IDocumentAnalysisService.cs, src/DocAlchemy.Core/Interfaces/IAnalysisResultStore.cs, src/DocAlchemy.Core/Models/AnalyzerMetadata.cs, src/DocAlchemy.Core/Models/AnalysisContent.cs, src/DocAlchemy.Core/Models/AnalysisResult.cs, src/DocAlchemy.Core/Models/AnalysisStorageReference.cs, src/DocAlchemy.Core/Models/DocumentAnalysisRequest.cs, src/DocAlchemy.Core/Models/ExtractedField.cs, src/DocAlchemy.Core/Models/FieldPath.cs, src/DocAlchemy.Core/Models/ConfidenceThreshold.cs, src/DocAlchemy.Core/Services/AnalysisJson.cs, tests/DocAlchemy.Core.Tests/AnalysisContractTests.cs, src/DocAlchemy.Core/README.md, plan.md.
Contracts/decisions:
  - Added SDK-independent asynchronous analyzer, document-analysis, and result-store interfaces. Analyzer creation receives the separately supplied ID and original JSON body; document input is already validated and its caller retains stream ownership; storage returns an opaque reference rather than a URL.
  - Analyzer metadata, analysis snapshots, content groups, fields, and storage references expose get-only properties; repeated collections use ImmutableArray. The snapshot captures a Guid analysis ID, analyzer ID, threshold, UTC completion time, schemaVersion, and all content items.
  - Extracted fields retain their service type string, cloned JsonElement value, nullable confidence, and recursive children. JSON null and unknown future types are preserved; an empty result is valid.
  - Field paths use `/contents/{index}/fields/{escaped-name}`, with `/properties/{escaped-name}` and `/items/{index}` structural segments; names use RFC 6901 escaping.
  - AnalysisJson serializes camel-case names and explicitly emits nullable properties. ConfidenceThreshold applies strict `<`; null confidence does not warn, and threshold/confidence values must be finite and in `[0, 1]`.
Checks executed:
  - `dotnet test tests\DocAlchemy.Core.Tests\DocAlchemy.Core.Tests.csproj`: passed, 13 tests including the Core assembly boundary test.
  - `dotnet build DocAlchemy.slnx`: succeeded, 0 warnings and 0 errors.
  - `git diff --check`: passed.
Checks not executed:
  - No Azure resource access, credentials, deployments, billable requests, or application/service acceptance checks; those are outside this step.
Open issues: None for the Step 03 acceptance criteria.
Handoff: Step 04 may add Azure configuration and DI; keep Core free of Azure SDK and UI dependencies. Use AnalysisResult and AnalysisJson as the normalized persistence contract, preserve captured identity/threshold/time on retries, and use FieldPath for recursive mapper output. Stop here for manual selection of the next step.
```

### Step 04 / `step-04-config`

```text
Step: 04 / step-04-config
Status: done
Development model: not recorded
Prerequisites checked: Step 03 is done; reviewed sections 6 and 9, the Step 03 handoff, Step 04 scope, and .github/instructions/csharp.instructions.md.
Files changed: README.md, plan.md, src/DocAlchemy.ContentUnderstanding/DocAlchemy.ContentUnderstanding.csproj, src/DocAlchemy.ContentUnderstanding/Configuration/ContentUnderstandingOptions.cs, src/DocAlchemy.ContentUnderstanding/Configuration/ContentUnderstandingOptionsValidator.cs, src/DocAlchemy.ContentUnderstanding/ContentUnderstandingServiceCollectionExtensions.cs, src/DocAlchemy.ContentUnderstanding/README.md, src/DocAlchemy.Storage/DocAlchemy.Storage.csproj, src/DocAlchemy.Storage/Configuration/StorageOptions.cs, src/DocAlchemy.Storage/Configuration/StorageOptionsValidator.cs, src/DocAlchemy.Storage/StorageServiceCollectionExtensions.cs, src/DocAlchemy.Storage/README.md, src/DocAlchemy.Web/DocAlchemy.Web.csproj, src/DocAlchemy.Web/Program.cs, src/DocAlchemy.Web/Configuration/AnalysisOptions.cs, src/DocAlchemy.Web/Configuration/UploadsOptions.cs, src/DocAlchemy.Web/Configuration/DocAlchemyOptionsValidator.cs, src/DocAlchemy.Web/Configuration/DocAlchemyConfigurationExtensions.cs, src/DocAlchemy.Web/README.md, tests/DocAlchemy.Web.Tests/AzureClientCompositionTests.cs, tests/DocAlchemy.Web.Tests/README.md.
Contracts/decisions:
  - Pinned Azure.AI.ContentUnderstanding 1.1.0; added Azure.Identity 1.21.0, Azure.Storage.Blobs 12.30.1, Microsoft.Extensions.Options.ConfigurationExtensions 10.0.14, and Microsoft.ApplicationInsights.AspNetCore 3.2.0.
  - Content Understanding's GA API version is fixed to 2025-11-01. Both Azure clients are singleton/thread-safe and use three exponential retries with SDK content logging disabled.
  - ContentUnderstanding:AuthenticationMode (ManagedIdentity or ApiKey) and Storage:AuthenticationMode (ManagedIdentity or AccountKey) are validated and selected independently. Managed identity uses DefaultAzureCredential with interactive browser auth excluded; a selected mode never falls back to the other credential. Irrelevant key settings are rejected.
  - Required endpoints must be absolute HTTPS URIs without user information, query, or fragment. Storage container/account names and account-key Base64 syntax are checked before client construction. These checks are syntactic and do not test Azure reachability or permissions.
  - Analysis defaults are threshold 0.80, operation timeout 00:10:00, and poll interval 00:00:02. Confidence must be finite and in [0,1], timeout is positive and at most two hours, and polling is positive and no longer than the timeout. Upload defaults to 20,971,520 bytes and is bounded by the service 200,000,000-byte limit.
  - Application Insights is enabled only when APPLICATIONINSIGHTS_CONNECTION_STRING is set. Its HTTP instrumentation redacts query parameter values by default; the application does not instrument request bodies or service payloads. Trace/span IDs are included in logging scopes.
  - Documented every setting and its default/validation in src/DocAlchemy.Web/README.md, including the exact provisioning names and local user-secrets workflow. No credentials were added to the repository.
Checks executed:
  - dotnet test tests\DocAlchemy.Web.Tests\DocAlchemy.Web.Tests.csproj: passed, 18 tests including four independent identity/key combinations and validation failures; client construction did not issue Azure calls.
  - dotnet test DocAlchemy.slnx: passed, 31 tests. The ContentUnderstanding and Storage test projects contain no discoverable tests yet.
  - dotnet build DocAlchemy.slnx: succeeded, 0 warnings and 0 errors.
  - git diff --check: passed.
Checks not executed:
  - No Azure resource access, credential use, deployment, billable requests, or live reachability/authorization tests.
Open issues: None for Step 04 local acceptance; live endpoint and role checks remain environment work.
Handoff: Step 04 is done. Infrastructure must provide the documented endpoint/container settings, retain ManagedIdentity defaults, and configure each key mode only when explicitly selected. Steps 05-08 can proceed from the registered SDK clients and validated Analysis/Uploads options; keep Core Azure-free. Stop for manual selection of the next step.
```

### Step 05 / `step-05-analyzers`

```text
Step: 05 / step-05-analyzers
Status: done
Development model: not recorded
Prerequisites checked: Step 04 is done; reviewed sections 3-6, 9, and 13, the Step 04 handoff, analyzer Core contracts, Content Understanding configuration/client registration, pinned SDK 1.1.0 package XML, and .github/instructions/csharp.instructions.md.
Files changed: plan.md, src/DocAlchemy.ContentUnderstanding/Configuration/AnalyzerOperationOptions.cs, src/DocAlchemy.ContentUnderstanding/ContentUnderstandingServiceCollectionExtensions.cs, src/DocAlchemy.ContentUnderstanding/DocAlchemy.ContentUnderstanding.csproj, src/DocAlchemy.ContentUnderstanding/README.md, src/DocAlchemy.ContentUnderstanding/Services/AnalyzerService.cs, src/DocAlchemy.Core/Models/AnalyzerMetadata.cs, tests/DocAlchemy.ContentUnderstanding.Tests/AnalyzerServiceTests.cs, tests/DocAlchemy.ContentUnderstanding.Tests/README.md.
Contracts/decisions:
  - Registered singleton IAnalyzerService. List consumes SDK AsyncPageable<ContentAnalyzer>; Get uses the raw protocol response and returns null only for 404. The pinned SDK's RequestContext list overload is a single raw response, not an AsyncPageable<BinaryData>; corrected the earlier plan statement based on the package API and tests.
  - Analyzer IDs use the verified 1-64 ASCII letter/digit/period/underscore/hyphen constraint. Creation validates a JSON object with a nonempty baseAnalyzerId and rejects any analyzerId body property; validation does not rewrite JSON. The unchanged string is sent via RequestContent with allowReplace=false, and the SDK is awaited through operation completion using caller cancellation.
  - Conflicts, authorization errors, throttling, operation failures, and other service failures remain explicit RequestFailedException errors; SDK bounded retries remain configured. Only Get maps 404 to null. Delete refuses IDs in the prebuilt namespace before sending a request.
  - Analyzer creation starts with WaitUntil.Started and polls using Analysis:OperationTimeout and Analysis:PollInterval defaults/validation from Step 04. An internally reached timeout becomes TimeoutException; a caller-requested cancellation remains OperationCanceledException.
  - Metadata exposes description, status, modality, built-in/read-only state, and supported analyze/delete operations. Custom modality derives from baseAnalyzerId; known prebuilt modality names are mapped conservatively. Confidence warnings account for analyzer-level estimateFieldSourceAndConfidence and nullable field-level estimateSourceAndConfidence overrides, including nested properties/item definitions, without mutating analyzer definitions.
  - Added HasConfidenceWarning to the SDK-independent Core AnalyzerMetadata contract so later UI can display the computed warning.
Checks executed:
  - `dotnet test tests\DocAlchemy.ContentUnderstanding.Tests\DocAlchemy.ContentUnderstanding.Tests.csproj`: passed, 13 isolated tests covering exact create-body bytes, allowReplace=false, invalid ID/body rejection without requests, 409 duplicate conflict, pageable listing, confidence inheritance/overrides, built-in restrictions, 404/403/429 behavior, async operation failure, configured timeout, and caller cancellation.
  - `dotnet test DocAlchemy.slnx`: passed, 44 tests across Core (13), ContentUnderstanding (13), and Web (18); Storage.Tests has no discoverable tests yet.
  - `dotnet build DocAlchemy.slnx`: succeeded, 0 warnings and 0 errors.
  - `git diff --check`: passed.
Checks not executed:
  - No Azure resource access, credential use, live analyzer CRUD, billable requests, deployment, or browser/UI work.
Open issues: None for the local Step 05 acceptance criteria. Live authorization and service behavior remain environment checks.
Handoff: Step 05 is complete. Step 06 may use the registered ContentUnderstandingClient and analyzer metadata, keeping analysis results on the verified raw BinaryData protocol path. Confidence warnings are exposed as AnalyzerMetadata.HasConfidenceWarning; IDs with the prebuilt prefix are read-only. Steps 08-09 remain manual and were not started; stop here for the user's next-step selection.
```

### Step 06 / `step-06-extraction`

```text
Step: 06 / step-06-extraction
Status: done
Development model: not recorded
Prerequisites checked: Step 04 is done; reviewed its handoff, Step 03 Core contracts, Step 05 adapter and tests, sections 3-6, 9 and 13, SDK 1.1.0 protocol signatures, the GA get-result schema, and repository C# instructions.
Files changed: plan.md, src/DocAlchemy.ContentUnderstanding/Configuration/DocumentUploadOptions.cs, src/DocAlchemy.ContentUnderstanding/ContentUnderstandingServiceCollectionExtensions.cs, src/DocAlchemy.ContentUnderstanding/Services/AnalysisResultMapper.cs, src/DocAlchemy.ContentUnderstanding/Services/AnalysisRetryPolicy.cs, src/DocAlchemy.ContentUnderstanding/Services/AnalysisSubmissionException.cs, src/DocAlchemy.ContentUnderstanding/Services/DocumentAnalysisService.cs, src/DocAlchemy.ContentUnderstanding/README.md, tests/DocAlchemy.ContentUnderstanding.Tests/DocumentAnalysisServiceTests.cs, tests/DocAlchemy.ContentUnderstanding.Tests/README.md, tests/DocAlchemy.Web.Tests/AzureClientCompositionTests.cs.
Contracts/decisions:
  - Registered singleton IDocumentAnalysisService. It reads the caller-owned stream without closing it, rejects empty or over-limit content based on actual bytes before any request, and buffers no more than the configured maximum. Uploads:MaxDocumentBytes defaults to 20,971,520 and is validated against the existing 200,000,000-byte service ceiling.
  - Uses the SDK raw AnalyzeBinaryAsync protocol overload with WaitUntil.Started, server-selected content type, and RequestContent; a linked timeout bounds reading, submission and polling. Caller cancellation remains OperationCanceledException; internally reached timeout is TimeoutException.
  - The shared SDK retry policy retains three exponential retries for polling and other existing operations, honors Retry-After, and disables retries and redirects specifically for POST :analyzeBinary. Submission SDK failures are wrapped in AnalysisSubmissionException with the original error as inner exception and an explicit uncertain-outcome warning. Never automatically resubmit after submission, timeout, cancellation, or polling failure; a lost response may represent an accepted paid operation.
  - SDK raw operation Value contains the completed operation envelope, with extracted items at result.contents. Map every document content item, including empty/missing field collections, to stable indexed Core models. Field values use the GA discriminated value* properties; object/array values contain reconstructed typed values and their children retain nested type, path and confidence.
  - Explicit JSON nulls and absent confidence remain null; unknown type nodes are preserved whole as opaque JSON. Invalid JSON, missing result/contents or required value, incorrect value type, invalid confidence, duplicate mapped properties and non-document content kinds fail with InvalidDataException. Snapshot captures the request analyzer/threshold, a new Guid, and one UTC completion timestamp. No complete service response is persisted.
Checks executed:
  - dotnet test DocAlchemy.slnx --no-restore: passed 71 tests (Core 13, ContentUnderstanding 40, Web 18); Storage.Tests has no tests yet. Tests use an in-process HTTP handler and make no Azure requests.
  - Isolated tests cover all GA field types, nested arrays/objects, unknown fields, null/missing confidence, multiple and empty items, malformed results, byte bounds, running/completed and failed operations, bounded 429/503 polling retries, Retry-After, timeout, caller cancellation, and no submission retry on 429, 500, lost response, or redirect. The tests verify binary bytes, content type, captured threshold/analyzer, UTC timestamp and caller stream ownership.
  - dotnet build DocAlchemy.slnx --no-restore: succeeded with 0 warnings and 0 errors.
  - SDK 10.0.404 selected under global.json latestPatch roll-forward. GA result envelope cross-checked against https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers/get-result?view=rest-contentunderstanding-2025-11-01.
Checks not executed:
  - No Azure resource access, live analysis, credentials, deployment, browser/UI work, or storage persistence. Real-service behavior remains for the separately authorized live checks.
Open issues: No local Step 06 acceptance blocker. UI must treat uncertain submission and polling failures as failures, not automatically repeat analysis; live compatibility remains unverified.
Handoff: Step 06 is complete. Step 11 can inject IDocumentAnalysisService and must perform upload format/modality validation and manage user-facing processing/error state; preserve the immutable returned snapshot for persistence retries. Step 07 remains independent. Stop for manual selection of the next step.
```

### Step 07 / `step-07-storage`

```text
Step: 07 / step-07-storage
Status: done
Development model: not recorded
Prerequisites checked: Step 04 is done; reviewed its storage configuration and DI, the Step 03 immutable Core result and IAnalysisResultStore contract, sections 5 and 9, the Step 06 handoff, and repository C# instructions.
Files changed: plan.md, src/DocAlchemy.Storage/Services/BlobAnalysisResultStore.cs, src/DocAlchemy.Storage/StorageServiceCollectionExtensions.cs, src/DocAlchemy.Storage/README.md, tests/DocAlchemy.Storage.Tests/BlobAnalysisResultStoreTests.cs, tests/DocAlchemy.Storage.Tests/README.md.
Contracts/decisions:
  - Serialize only the immutable AnalysisResult with the Core AnalysisJson contract to UTF-8 bytes. Upload with BlobHttpHeaders.ContentType=application/json.
  - Use {analysisId:D}.json as a stable blob name and If-None-Match: * conditional creation in the already provisioned results container. A 412 triggers a download and exact byte comparison: an identical payload succeeds idempotently (including a retry after the first response was lost); a different payload for the same ID fails explicitly without overwrite.
  - Return only the blob name in AnalysisStorageReference; do not return a URL/SAS or create or alter container access policy. The configured container must already exist and be private.
  - Propagate Azure RequestFailedException for authorization, missing-container, and other storage failures so callers can distinguish persistence failure from analysis failure. No source document, credential, or raw Content Understanding response is serialized.
Checks executed:
  - dotnet test tests\\DocAlchemy.Storage.Tests\\DocAlchemy.Storage.Tests.csproj --no-restore: passed 7 tests using an in-process HTTP handler; no Azure requests.
  - dotnet test DocAlchemy.slnx --no-restore: passed all 78 tests (Core 13, Storage 7, ContentUnderstanding 40, Web 18).
  - dotnet build DocAlchemy.slnx --no-restore: succeeded with 0 warnings and 0 errors.
  - git diff --check: passed.
  - Tests verify the full JSON shape, typed nested values, explicit nulls, persisted application/json blob metadata, stable name/payload, opaque non-URL reference, idempotent retry, response-lost retry, collision failure, and surfaced 403/404/500 storage errors. No test uploads to Azure.
Checks not executed:
  - No Azure resource access, live Blob upload, credentials, deployment, or public-access configuration changes.
Open issues: No local Step 07 acceptance blocker. Private access depends on provisioning the configured results container privately; the adapter intentionally does not create containers or modify access policies.
Handoff: Step 07 is complete. Step 11 can inject IAnalysisResultStore, keep the same immutable AnalysisResult while offering persistence-only retries, and report persistence errors separately from analysis results. SaveAsync's returned reference is opaque and must not be presented as a public URL. Stop for manual selection of the next step.
```

### Step 08 / `step-08-shell`

```text
Step: 08 / step-08-shell
Status: done
Development model: not recorded
Prerequisites checked: Step 04 is done; reviewed its Web configuration and DI contract, the Step 07 handoff, Sections 7, 9, and 10, existing Web components/tests, and .github/instructions/csharp.instructions.md.
Files changed: plan.md, src/DocAlchemy.Web/Components/App.razor, src/DocAlchemy.Web/Components/Layout/MainLayout.razor, src/DocAlchemy.Web/Components/Layout/MainLayout.razor.css, src/DocAlchemy.Web/Components/Pages/Index.razor, src/DocAlchemy.Web/Components/Pages/Analyzers.razor, src/DocAlchemy.Web/Components/Pages/Analyze.razor, src/DocAlchemy.Web/Components/Shared/StatusMessage.razor, src/DocAlchemy.Web/Components/Shared/StatusMessageKind.cs, src/DocAlchemy.Web/Components/_Imports.razor, src/DocAlchemy.Web/wwwroot/app.css, src/DocAlchemy.Web/README.md, tests/DocAlchemy.Web.Tests/DocAlchemy.Web.Tests.csproj, tests/DocAlchemy.Web.Tests/StatusMessageTests.cs.
Contracts/decisions:
  - Kept the Blazor route tree on Interactive Server rendering and added the accessible responsive shell with Analyzers and Analyze document navigation. Those destinations are explicitly nonfunctional placeholders; no analyzer, upload, or analysis business logic was added.
  - Implemented all 13 Section 7 palette colors as CSS custom properties with the prescribed surface/text pairings. Shared status messages have visible labels, polite status announcements, assertive error announcements, encoded text, and loading/warning/success/error styles.
  - The skip link moves keyboard focus to the main landmark after interactive startup. No Azure service, credentials, or UI framework were added.
Checks executed:
  - dotnet build src\DocAlchemy.Web\DocAlchemy.Web.csproj --no-restore: succeeded with 0 warnings and 0 errors.
  - dotnet test tests\DocAlchemy.Web.Tests\DocAlchemy.Web.Tests.csproj --no-restore: passed all 20 tests; added focused checks for assertive error announcements, encoded dynamic text, and non-color warning labels.
  - git diff --check: passed.
  - Browser at http://localhost:5053: home, /analyzers, and /analyze rendered their expected headings/titles and selected navigation state. Interactive navigation preserved the document; the skip link was keyboard focused and Enter moved focus to main. At a 375px viewport, navigation stacked, workspace cards used one column, and document width matched the viewport content width with no horizontal overflow. A 1280x900 screenshot was visually checked.
  - Browser-computed CSS values matched all 13 approved hex colors. Measured contrast ratios: white on midnight 16.32:1; white on primary blue 7.01:1; midnight on warning amber 8.61:1; secondary text on page background 6.56:1; success on white 6.11:1; error on white 6.57:1.
  - Browser run used only synthetic HTTPS endpoint configuration and local HTTP; no Azure requests or credentials were used. The HTTP-only runner logged the expected missing-HTTPS-port redirection warning; this did not affect the shell checks.
Checks not executed:
  - No full-solution suite, Azure reachability/authentication, or live-service checks; those are outside this shell step and no Azure service was invoked.
Open issues: None for Step 08. The analyzer and analysis destinations intentionally remain placeholders for their later steps.
Handoff: Step 09 can replace the Analyzers placeholder and reuse the responsive shell, selected navigation, palette tokens, and StatusMessage component. Step 10 can reuse the same status and palette primitives. No automatic follow-on work was started.
```

### Step 09 / `step-09-analyzer-ui`

```text
Step: 09 / step-09-analyzer-ui
Status: done
Development model: not recorded
Prerequisites checked: Steps 05 and 08 are done; reviewed IAnalyzerService, AnalyzerMetadata, the existing analyzer adapter and Web shell, Sections 9, 10, and 13, and .github/instructions/csharp.instructions.md.
Files changed: plan.md, src/DocAlchemy.Web/Components/Pages/Analyzers.razor, src/DocAlchemy.Web/Components/Pages/Analyzers.razor.css, src/DocAlchemy.Web/README.md, tests/DocAlchemy.Web.Tests/DocAlchemy.Web.Tests.csproj, tests/DocAlchemy.Web.Tests/AnalyzerManagementTests.cs.
Contracts/decisions:
  - The page uses IAnalyzerService for listing, creation, and deletion, displays available metadata and supported operations, and exposes loading, empty, refresh, and error states.
  - Analyzer IDs are entered separately and validated against the service's 1-64 ASCII-character rule. Imported files must be UTF-8 JSON objects with a non-empty baseAnalyzerId and no analyzerId property. The original JSON string is passed to CreateAsync unchanged; it is never deserialized and reserialized for submission.
  - File size is bounded by Uploads:MaxDocumentBytes. The UI inspects global and per-field confidence settings to warn without changing the imported definition.
  - Delete controls appear only for mutable analyzers with a reported delete operation. Deletion requires typing the exact analyzer ID. Busy guards prevent duplicate mutations; component disposal cancels outstanding service work, and service/refresh failures are visible without success-shaped fallback states.
  - Added bUnit for realistic component event tests using an in-memory IAnalyzerService; test files and service doubles do not call Azure.
Checks executed:
  - dotnet build src\\DocAlchemy.Web\\DocAlchemy.Web.csproj --no-restore: succeeded with 0 warnings and 0 errors.
  - dotnet test tests\\DocAlchemy.Web.Tests\\DocAlchemy.Web.Tests.csproj --no-restore: passed all 29 tests, including analyzer list metadata and loading/empty/error states, refresh, unchanged JSON create, duplicate submission, malformed JSON and size limits, ID validation, confidence warnings, supported/protected deletion, exact-ID confirmation, and create/delete failures.
  - git diff --check: passed.
Checks not executed:
  - No browser-based live-service check, full-solution suite, Azure reachability/authentication, or live-service operation; no Azure request was made.
Open issues: None for Step 09.
Handoff: Step 10 can reuse the accessible status and palette styles. Step 11 can reuse IAnalyzerService and the analyzer page's explicit operation/error handling patterns. No automatic follow-on work was started.
```

### Step 10 / `step-10-results-ui`

```text
Step: 10 / step-10-results-ui
Status: done
Development model: not recorded
Prerequisites checked: Steps 03 and 08 are done; reviewed the Core AnalysisResult/AnalysisContent/ExtractedField/FieldPath/ConfidenceThreshold contracts, the Web shell and palette, Sections 9, 10, and 13, and .github/instructions/csharp.instructions.md.
Files changed: plan.md, src/DocAlchemy.Web/Components/Shared/AnalysisResults.razor, src/DocAlchemy.Web/Components/Shared/AnalysisResults.razor.css, src/DocAlchemy.Web/Components/Shared/FieldResult.razor, src/DocAlchemy.Web/Components/Shared/FieldResult.razor.css, src/DocAlchemy.Web/Components/Shared/ConfidenceIndicator.razor, src/DocAlchemy.Web/Components/Shared/ConfidenceIndicator.razor.css, src/DocAlchemy.Web/README.md, tests/DocAlchemy.Web.Tests/AnalysisResultsTests.cs.
Contracts/decisions:
  - AnalysisResults accepts the existing immutable AnalysisResult snapshot and renders each content item, including empty results and items without fields, with stable result ordering and no analyzer-schema assumptions.
  - FieldResult recursively displays each field name, stable FieldPath, service type, raw JSON value, optional confidence, and children. Extracted values are rendered only as Razor-encoded text, never as markup.
  - ConfidenceIndicator uses the snapshot's captured threshold through Core's strict-below ConfidenceThreshold rule. Below-threshold values show an amber decorative icon and visible warning text; equal/above values do not warn; null displays "Not available". Percent text uses invariant formatting.
  - Scoped responsive styles use the shared palette and keep large JSON values in a bounded scrollable region. The components are intentionally not wired to the /analyze placeholder; step 11 owns workflow integration.
Checks executed:
  - `dotnet test tests\DocAlchemy.Web.Tests\DocAlchemy.Web.Tests.csproj --no-restore`: passed all 32 tests, including 3 focused result/confidence component tests covering 0.79, 0.80, 0.81, null confidence, nested objects/arrays, multiple content items, empty results, null values, unknown field types, and safely encoded markup.
  - `dotnet build src\DocAlchemy.Web\DocAlchemy.Web.csproj --no-restore`: succeeded with 0 warnings and 0 errors.
  - `git diff --check`: passed.
Checks not executed:
  - No full-solution suite, browser workflow, Azure reachability/authentication, or live-service operation; no Azure request was made.
Open issues: None for the Step 10 acceptance criteria.
Handoff: Step 11 can pass its completed immutable AnalysisResult directly to AnalysisResults; the components use the captured confidence threshold and require no fixed analyzer schema. Keep analysis submission, upload, saving, workflow state, and service calls in step 11. No automatic follow-on work was started.
```

### Step 11 / `step-11-analysis-ui`

```text
Step: 11 / step-11-analysis-ui
Status: done
Development model: not recorded
Prerequisites checked: Steps 05-08 and 10 are done; reviewed the analyzer and analysis service contracts, upload/analysis options, immutable result/storage contracts, result components, Section 9, and .github/instructions/csharp.instructions.md.
Files changed: plan.md, src/DocAlchemy.Web/Components/Pages/Analyze.razor, src/DocAlchemy.Web/Components/Pages/Analyze.razor.css, src/DocAlchemy.Web/README.md, tests/DocAlchemy.Web.Tests/AnalysisWorkflowTests.cs, tests/DocAlchemy.ContentUnderstanding.Tests/DocumentAnalysisServiceTests.cs.
Contracts/decisions:
  - The page lists only analyzers supporting analyze with document or image modality; audio/video and unsupported operations are excluded.
  - The server validates allowed extensions and declared MIME types, rejects empty/oversized selections, and supplies canonical MIME types. The existing analysis service independently measures actual stream bytes, rejects empty/oversized input, and keeps ownership with the page; the source document is never saved.
  - Analyzer ID and confidence threshold are captured for the analysis request. The normalized immutable result is rendered directly by AnalysisResults and saved automatically, with analysis and storage status reported separately.
  - A failed save retains the same snapshot in the circuit, disables starting another analysis, and retries IAnalysisResultStore only. The opaque storage reference is not displayed. Disposal cancels outstanding page operations; unsaved circuit-local results are documented as lost when the circuit ends.
  - Added isolated component tests for analyzer filtering, supported/unsupported and mismatched input, empty files, exact configured-size acceptance and next-byte rejection, captured threshold, immutable-snapshot retry, no second analysis on retry, empty results, and disposal cancellation. Added a service boundary test at 20,971,520 and 20,971,521 actual bytes.
Checks executed:
  - `dotnet test tests\DocAlchemy.Web.Tests\DocAlchemy.Web.Tests.csproj --no-restore`: passed all 36 tests.
  - `dotnet test tests\DocAlchemy.ContentUnderstanding.Tests\DocAlchemy.ContentUnderstanding.Tests.csproj --no-restore`: passed all 42 tests, including the exact configured upload boundary; all Azure calls use test doubles and no service requests were made.
  - `git diff --check`: passed.
Checks not executed:
  - No full-solution suite, browser workflow, Azure authentication/reachability, or live-service analysis/storage; no Azure request was made.
Open issues: None for the Step 11 acceptance criteria.
Handoff: Step 16 can document the implemented upload, analysis, persistence, retry, and circuit-lifetime behavior. The user chooses and starts any next step manually; no automatic follow-on work was started.
```

### Step 12 / `step-12-infra-base`

```text
Step: 12 / step-12-infra-base
Status: done
Development model: not recorded
Prerequisites checked: Step 04 is done; reviewed its section 14 record, the Storage configuration validator, Web and Storage READMEs, and sections 6, 8, 9, and 13.
Files changed: plan.md, infra/main.bicep, infra/main.parameters.json, infra/abbreviations.json, infra/modules/monitoring.bicep, infra/modules/storage.bicep, infra/README.md.
Contracts/decisions:
  - Subscription-scope main creates the resource group and composes monitoring and storage. AZD parameters map AZURE_ENV_NAME and AZURE_LOCATION; resultsContainerName defaults to analysis-results and is independently overridable.
  - The full 135-entry canonical AZD abbreviation list is used for prefix-based names. Names incorporate a sanitized, bounded environment portion and a deterministic subscription/environment token. Project and azd-env-name tags apply to taggable resources.
  - Monitoring creates a Log Analytics workspace with 30-day retention and workspace-based Application Insights. Storage creates a Standard_LRS StorageV2 account and a private results container, enforces HTTPS/TLS 1.2, disables anonymous Blob access, and retains shared-key support for explicit key mode.
  - Main exposes only resource names, IDs, and the Blob endpoint. STORAGE_BLOB_SERVICE_ENDPOINT and STORAGE_RESULTS_CONTAINER_NAME map to Storage__BlobServiceEndpoint and Storage__ContainerName; STORAGE_RESULTS_CONTAINER_ID is the later role-assignment scope. ManagedIdentity remains the app default and account name/key must be omitted in that mode.
  - No Application Insights connection string or Storage key is output. A later frontend module must retrieve the monitoring connection string and configure APPLICATIONINSIGHTS_CONNECTION_STRING securely, and add container-scoped access. Private container access does not imply a private endpoint or network isolation.
Checks executed:
  - Bicep CLI 0.43.8: `bicep build .\infra\main.bicep --outfile <session-file>` and `bicep lint .\infra\main.bicep` passed without warnings or errors.
  - Parsed the compiled ARM template locally: verified subscription composition, workspace-linked Application Insights, StorageV2 HTTPS/TLS and shared-key settings, disabled anonymous account/container access, exact parameter/output contracts, and absence of key/connection-string expressions in outputs. Checked storage-name shape and length with representative environment-name edge cases.
  - Compared all 135 abbreviations against the public Azure/azure-dev canonical JSON; exact match. An initial comparison exposed three transcription differences, which were corrected and rechecked.
  - `git diff --check` passed.
Checks not executed:
  - No subscription lookup, Azure authentication, deployment, provisioning, resource access, role assignment, or live validation. No AZD packaging; service wiring belongs to step 15. Compilation is not evidence of Azure deployment success, regional availability, quota, or authorization.
Open issues: No blocker for the local step 12 acceptance criteria. Container-name character rules and global name availability remain Azure deployment checks; environment preflight remains step 18.
Handoff: Steps 13-14 may extend the documented module contracts and composition, retain nonsecret outputs, map the exact application settings, and add scoped identity access. Step 15 owns azure.yaml and bootstrap wiring. Stop for manual selection of the next step.
```

### Step 13 / `step-13-infra-foundry`

```text
Step: 13 / step-13-infra-foundry
Status: done
Development model: not recorded
Prerequisites checked: Steps 01 and 12 are done; reviewed their section 14 records, section 8 infrastructure contracts, section 9 execution protocol, and the step 13 scope.
Files changed: plan.md, infra/main.bicep, infra/modules/foundry.bicep, infra/modules/keyvault.bicep, infra/README.md.
Contracts/decisions:
  - Added a `Microsoft.CognitiveServices/accounts@2025-06-01` Foundry `AIServices` account with S0 as the default account SKU, a custom subdomain, and local key authentication enabled for the separately opted-in API-key mode.
  - Added GPT-5.2 (`2025-12-11`) and text-embedding-3-large (`1`) OpenAI-format deployments. Names, versions, deployment SKUs, capacities, Foundry region, and account SKU are parameters. Defaults are `gpt-52`, `text-embedding-3-large`, `GlobalStandard`, and capacity 1; deployment model and alias defaults remain step 15's responsibility.
  - Added a `Microsoft.KeyVault/vaults@2025-05-01` vault with Azure RBAC, soft delete, and purge protection. The Key Vault module obtains Foundry key 1 through `listKeys()` and writes it directly to the `ContentUnderstandingApiKey` secret resource. The key is not a Bicep parameter, output, or logged value; only resource identifiers and versionless secret-reference metadata are exposed.
  - The Key Vault module consumes the Foundry account name output, making it depend on Foundry. Later App Service settings and access assignments can consume the vault metadata and Web identity without introducing a reverse dependency.
  - `main.parameters.json` remains unchanged: the Foundry region defaults to the primary `location`, and all model/account settings have Bicep defaults that can be overridden with deployment parameters.
  - Official contract sources: https://learn.microsoft.com/azure/ai-services/multi-service-resource#create-a-foundry-resource ; https://learn.microsoft.com/azure/ai-services/content-understanding/concepts/models-deployments ; https://learn.microsoft.com/azure/ai-services/content-understanding/service-limits#supported-generative-models ; https://learn.microsoft.com/azure/azure-resource-manager/bicep/scenarios-secrets
Checks executed:
  - Bicep CLI 0.49.2 (`az bicep version`).
  - `az bicep build --file .\infra\main.bicep --outfile <temporary-file>` succeeded.
  - `az bicep lint --file .\infra\main.bicep` succeeded without diagnostics.
  - Parsed the generated ARM template locally and verified the AIServices account, two model deployments, RBAC-enabled Key Vault, direct `listKeys().key1` secret value expression, Key Vault module dependency on the Foundry module, and absence of key material in deployment outputs.
  - `git diff --check` passed.
Checks not executed:
  - No Azure authentication, subscription/resource lookup, provisioning, deployment, role assignment, or live validation.
  - Foundry region support, model catalog/deployment availability, SKU/capacity, subscription quota, globally unique names, provider registration, operator permissions, and actual Key Vault secret-write authorization remain unverified and are deferred to step 18.
Open issues: No blocker for the local step 13 acceptance criteria. Local compilation does not establish that the account or either model deployment can be provisioned in any target region.
Handoff: Step 14 may consume the endpoint/account and Key Vault metadata, add the Web App configuration and least-privilege scoped roles, keep managed identity as the default, and use the versionless Key Vault reference only for explicit API-key mode. Step 15 owns the idempotent model-default bootstrap and must map the supported aliases to these deployment names. Stop here for manual selection of the next step.
```
