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

1. Use Azure Content Understanding, not Azure Document Intelligence. The documented .NET client is `Azure.AI.ContentUnderstanding`; current SDK documentation describes stable version `1.1.0`.
2. Prefer the GA data-plane API `2025-11-01`. Do not silently adopt `2026-06-01-preview`. Pin and verify the SDK/API combination before implementation proceeds beyond the integration contract.
3. Confidence is opt-in: analyzer configuration uses `estimateFieldSourceAndConfidence`, and individual fields can use `estimateSourceAndConfidence`. Inheritance and field overrides must be considered when reporting whether confidence is enabled.
4. Document inputs include PDF, DOCX, TXT, Markdown, and supported document-image formats. Images containing documents must follow the document-analysis path so document field-confidence behavior applies.
5. Use asynchronous analysis operations and polling. Do not use synchronous operations with their smaller input and page limits, which can truncate processing.
6. A Foundry resource alone is insufficient: provision supported model deployments and configure Content Understanding model-deployment defaults. This is part of environment readiness, not a manual undocumented prerequisite.
7. Current Content Understanding RBAC distinguishes Reader, Contributor, and Owner. The service-specific Contributor role does not permit deletion; the application's analyzer-management scenario requires the service-specific Owner capabilities.
8. Model compatibility, deployment versions, quota, and regional availability must be validated against the selected environment. SDK examples and current service documentation use different model baselines; do not copy a model deployment name without checking it.

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
- Tests reference the projects under test. Use one test project unless the actual tooling requires separation.
- Do not add a separate HTTP API, AppHost, Docker image, or agent framework: they are unnecessary for this server-rendered demo.

### Core contracts

- `IAnalyzerService`: asynchronously list, retrieve, create, and delete analyzers, with cancellation.
- `IDocumentAnalysisService`: analyze a validated document with the selected analyzer and return a normalized result.
- `IAnalysisResultStore`: persist the immutable normalized result and return a nonpublic storage reference.
- Shared models for analyzer metadata, recursive extracted fields, content-item grouping, analysis results, and storage references.
- Represent dynamic typed values with System.Text.Json types rather than flattening everything to strings. Preserve numbers, booleans, dates as returned, arrays, objects, nulls, and per-field nullable confidence.
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
| Web App managed identity | Foundry account | `Cognitive Services Content Understanding Owner`, or the verified least-privilege equivalent covering create/list/analyze/delete |
| Web App managed identity | Results container | `Storage Blob Data Contributor` |
| Web App managed identity | Key Vault | `Key Vault Secrets User` |
| Deployment/bootstrap operator | Deployment and service setup scopes | Resource provisioning, role-assignment permission, and the verified data-plane permission to configure Content Understanding defaults |
| Foundry identity, if required by the selected model path | Required model resource | Only the documented model-inference permissions |

Do not confuse Content Understanding Owner with subscription/resource-group `Owner`. Verify exact role definitions and scope compatibility before generating role assignments.

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
| 01 / `step-01-contract` | Verified SDK/API, schemas, model mappings, and access contracts | None | Source-backed research; compatibility reasoning | pending |
| 02 / `step-02-scaffold` | Compiling solution and test scaffolding | 01 | Precise .NET tooling and project setup | pending |
| 03 / `step-03-core` | Core interfaces, recursive models, and result contract | 02 | Domain modeling; type safety; boundary tests | pending |
| 04 / `step-04-config` | Validated settings, authentication modes, and DI | 03 | Azure SDK integration; credential correctness | pending |
| 05 / `step-05-analyzers` | Analyzer management adapter and tests | 04 | API semantics; asynchronous operations | pending |
| 06 / `step-06-extraction` | Analysis adapter and lossless result mapping | 04 | Complex mapping; polling and failure handling | pending |
| 07 / `step-07-storage` | Idempotent Blob persistence and tests | 04 | Persistence consistency; serialization | pending |
| 08 / `step-08-shell` | Accessible Blazor shell and palette | 04 | Blazor UI; CSS; accessibility | pending |
| 09 / `step-09-analyzer-ui` | Complete analyzer-management page | 05, 08 | Interactive forms; component state | pending |
| 10 / `step-10-results-ui` | Recursive results and confidence components | 03, 08 | Recursive UI; exact display semantics | pending |
| 11 / `step-11-analysis-ui` | Upload-to-analysis-to-save workflow | 05, 06, 07, 08, 10 | Cross-service state; cancellation; retry correctness | pending |
| 12 / `step-12-infra-base` | Naming, resource group, monitoring, and storage IaC | 04 | Bicep resource composition | pending |
| 13 / `step-13-infra-foundry` | Foundry models and Key Vault IaC | 01, 12 | Model deployment contracts; secret handling | pending |
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

**Read/change scope:** Root solution/SDK configuration, `.gitignore`, the three projects under `src`, and `tests/DocAlchemy.Tests`.

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

No implementation step has been executed yet. Append one record per attempted step using the structure below. Keep existing evidence; if a retry supersedes a failed attempt, explain what changed. Store no keys, tokens, document contents, or sensitive account details here.

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
