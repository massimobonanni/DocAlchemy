# Infrastructure composition

The subscription-scope Bicep entry point creates a resource group containing
monitoring, private results storage, a Foundry `AIServices` account with
Content Understanding model deployments, an RBAC-enabled Key Vault, and a
Linux App Service Web App with scoped access assignments. AZD service wiring
and the Content Understanding defaults bootstrap are step 15. No Azure
resource or live access has been validated.

## Entry point and naming

`main.bicep` accepts:

| Parameter | Contract |
| --- | --- |
| `environmentName` | Required AZD environment name, 1-64 characters; also used in the `azd-env-name` tag. |
| `location` | Required Azure region for the resource group, monitoring, storage, and Key Vault. |
| `foundryLocation` | Optional Foundry region; defaults to `location`. Override it with an authorized deployment parameter when the approved environment uses a different Foundry region. |
| `resultsContainerName` | Blob container name, 3-63 characters; default `analysis-results`. Use lowercase letters, digits and single hyphens, beginning and ending with a letter or digit. |
| `foundrySkuName` | Foundry account SKU; defaults to `S0`. |
| `completionDeploymentName`, `completionModelVersion`, `completionDeploymentSkuName`, `completionDeploymentCapacity` | GPT-5.2 deployment; defaults to `gpt-52`, `2025-12-11`, `GlobalStandard`, and `1`. |
| `embeddingDeploymentName`, `embeddingModelVersion`, `embeddingDeploymentSkuName`, `embeddingDeploymentCapacity` | Text Embedding 3 Large deployment; defaults to `text-embedding-3-large`, `1`, `GlobalStandard`, and `1`. |
| `appServicePlanSkuName` | App Service plan SKU; defaults to the single-instance `B1` Basic plan. Supported values are `B1`, `B2`, `B3`, `P0v3`, `P1v3`, and `P2v3`. |
| `contentUnderstandingAuthenticationMode` | Web App Content Understanding auth mode: `ManagedIdentity` (default) or `ApiKey`. API-key mode uses the Key Vault reference; no key value is a parameter. |
| `bootstrapPrincipalId` | Optional Entra principal object ID receiving the scoped `Cognitive Services User` role for step 15 defaults setup. Leave empty when an existing assignment will be used. |
| `confidenceThreshold` | Application threshold, default `0.8`, inclusive range `[0, 1]`. |
| `maxDocumentBytes` | Application upload limit, default `20,971,520`, range `1`-`200,000,000`. |

Bicep parameters support integer rather than decimal primitive types, so
`confidenceThreshold` is a bounded string (default `0.80`). The Web
application's startup options validation enforces that the value is finite and
within `[0, 1]`.

`main.parameters.json` maps `AZURE_ENV_NAME` and `AZURE_LOCATION` to the
required parameters. The container uses its Bicep default; the parameter can
be overridden by an ARM deployment parameter when the deployment is
authorized. The Bicep length decorators do not check the full container naming
rules; Azure rejects an invalid name.

The checked-in `abbreviations.json` is the
[canonical AZD abbreviation list](https://github.com/Azure/azure-dev/blob/main/cli/azd/resources/scaffold/base/abbreviations.json).
Resource names use its prefixes, a lowercase alphanumeric portion of the
environment name, and a 13-character `uniqueString` token derived from the
subscription ID and original environment name. Non-alphanumeric characters
are removed from the name portion, but remain part of the token and tag. The
storage name limits that portion to nine characters so the entire name is at
most 24 characters. The Foundry name portion is limited to 32 characters; the
Key Vault portion is limited to eight so its `kv-` prefix and uniqueness token
fit the 24-character vault-name limit. Keep
the subscription and environment name stable to retain resource names.
Changing the environment name or subscription produces different names; the
resource group location is fixed when the group is created.

Resources that support tags receive `project: DocAlchemy` and
`azd-env-name: <environmentName>`. The Blob service and container do not have
resource tags.

## Module contracts

| Module | Inputs | Nonsecret outputs |
| --- | --- | --- |
| `modules/monitoring.bicep` | `location`, `workspaceName`, `applicationInsightsName`, `tags` | `workspaceId`, `applicationInsightsId`, internal `applicationInsightsConnectionString` |
| `modules/storage.bicep` | `location`, `storageAccountName`, `containerName`, `tags` | `storageAccountId`, `storageAccountName`, `blobServiceEndpoint`, `containerId`, `containerName` |
| `modules/foundry.bicep` | Foundry `location`, account SKU/name, pinned model versions, deployment names/SKUs/capacities, `tags` | Foundry account ID/name/endpoint and nonsecret model/deployment metadata |
| `modules/keyvault.bicep` | Primary `location`, vault name, Foundry account name, `tags` | Vault ID/name/URI and versionless secret name/URI |
| `modules/frontend.bicep` | `location`, plan/Web App names and SKU, tags, monitoring connection string, service endpoints, container, versionless API-key secret URI, threshold, upload limit | Web App ID/name/URL and system-assigned principal ID |
| `modules/access.bicep` | Foundry/storage/container/Key Vault names, Web App principal ID, auth mode, optional bootstrap principal ID | No outputs |

The monitoring module uses a 30-day Log Analytics retention period and
connects Application Insights to that workspace. It passes the connection
string internally to the frontend module, which sets
`APPLICATIONINSIGHTS_CONNECTION_STRING`; it is not a top-level deployment
output.

The storage module creates a Standard_LRS StorageV2 account, requires HTTPS
and TLS 1.2 or newer, disables anonymous Blob access at account level, and
sets container access to `None`. It permits shared-key authorization only to
preserve the explicitly supported `AccountKey` mode; no account key is
returned. Managed identity is the application default, but a later step must
assign `Storage Blob Data Contributor` at the results-container scope.
Private means no anonymous Blob access, **not** a private endpoint or a
network-isolated account. Public network access remains enabled for this demo.

The Foundry module creates an `AIServices` Cognitive Services account with
local key authentication enabled, a custom subdomain, and two pinned OpenAI
format deployments: GPT-5.2 version `2025-12-11` and
`text-embedding-3-large` version `1`. Deployment names, model versions, SKU
names, capacities, the account SKU, and the Foundry region are Bicep parameters.
The defaults bootstrap (step 15) must map the model/alias contract from step 01
to the emitted deployment names. Model support in Content Understanding is not
proof that a specific deployment is available in the selected region.

The Key Vault module enables Azure RBAC, soft delete, and purge protection. It
retrieves Foundry key 1 through the account `listKeys()` function and writes it
directly as the `ContentUnderstandingApiKey` secret. The key is not passed
through a Bicep parameter or output. The module outputs only nonsecret vault
and secret-reference metadata. In `ApiKey` mode only, App Service receives a
versionless `@Microsoft.KeyVault(SecretUri=...)` setting and its identity gets
`Key Vault Secrets User` at the vault scope. In the default
`ManagedIdentity` mode, the API-key setting and Key Vault role are omitted.
The deployment operator must be authorized to write the secret.

The dependency graph is one-way: the Key Vault module receives the Foundry
account name from the Foundry module; the frontend consumes monitoring,
Foundry, storage, and Key Vault outputs; and the access module consumes the
frontend principal and target resource names. The Foundry module does not
depend on Key Vault or the Web App, so the secret reference and role
assignments do not create a deployment cycle.

## App Service configuration and access

The frontend module creates a Linux App Service plan and a .NET 10 Web App
with a system-assigned identity. The default B1 Basic SKU supports Always On.
The app requires HTTPS, TLS 1.2 or newer for both the app and SCM site,
WebSockets, HTTP/2, Always On, and session affinity for Interactive Server.
FTPS is disabled. The Web App is tagged `azd-service-name: web` for the
subsequent AZD service declaration.

The module maps the application options directly to App Service settings:

| App setting | Value |
| --- | --- |
| `ContentUnderstanding__Endpoint` | Foundry account endpoint |
| `ContentUnderstanding__ApiVersion` | `2025-11-01` |
| `ContentUnderstanding__AuthenticationMode` | `ManagedIdentity` by default; `ApiKey` only when explicitly selected |
| `ContentUnderstanding__ApiKey` | Omitted in identity mode; versionless Key Vault reference in API-key mode |
| `Storage__BlobServiceEndpoint` | Storage Blob service endpoint |
| `Storage__ContainerName` | Private results container name |
| `Storage__AuthenticationMode` | `ManagedIdentity` |
| `Analysis__ConfidenceThreshold` | `0.8` by default |
| `Analysis__OperationTimeout` | `00:10:00` |
| `Analysis__PollInterval` | `00:00:02` |
| `Uploads__MaxDocumentBytes` | `20971520` by default |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Workspace-based Application Insights connection string |

`Storage__AccountName` and `Storage__AccountKey` are omitted. Account-key
storage auth remains available only for explicit local configuration; no
storage or Foundry key is emitted in a setting value, parameter, or output.

The access module assigns only the roles needed by the selected runtime mode:

| Principal | Scope | Role | Condition |
| --- | --- | --- | --- |
| Web App system identity | Foundry account | `Cognitive Services Content Understanding Owner` (`4b42bd01-da42-4c92-9b07-15ea5bd6a602`) | `ManagedIdentity` mode |
| Web App system identity | Results container | `Storage Blob Data Contributor` (`ba92f5b4-2d11-453d-a403-e96b0029c9fe`) | Always; Blob auth is managed identity |
| Web App system identity | Key Vault | `Key Vault Secrets User` (`4633458b-17de-408a-b874-0445c86b69e6`) | `ApiKey` mode |
| Optional bootstrap principal | Foundry account | `Cognitive Services User` (`a97b65f3-24c7-4388-baec-2e87135dc908`) | Only when `bootstrapPrincipalId` is nonempty |

Role assignments use deterministic GUIDs and resource-specific scopes, not
subscription/resource-group-wide grants. The Web App's Foundry role includes
analyzer deletion, unlike Content Understanding Contributor. The optional
bootstrap role is for step 15's Entra-authenticated defaults operation; it
does not grant deployment control-plane rights. If no bootstrap principal ID
is supplied, that operator must already have the required data-plane role.
Confirm role availability, assignment permissions, and live authorization
during the approved environment preflight; local compilation does not validate
them.

The public outputs `WEB_APP_ID`, `WEB_APP_NAME`, and `WEB_APP_URL` are
nonsecret deployment metadata for step 15. The app URL is HTTPS.

## Composition outputs and application mapping

| Output | Later consumer |
| --- | --- |
| `AZURE_RESOURCE_GROUP_NAME` | Resource group for later modules and deployment. |
| `LOG_ANALYTICS_WORKSPACE_ID` | Monitoring workspace resource ID. |
| `APPLICATION_INSIGHTS_ID` | Application Insights component resource ID. |
| `STORAGE_ACCOUNT_ID`, `STORAGE_ACCOUNT_NAME` | Storage resource identity; account name is needed only for explicit application key mode. |
| `STORAGE_BLOB_SERVICE_ENDPOINT` | `Storage__BlobServiceEndpoint`. |
| `STORAGE_RESULTS_CONTAINER_ID` | Container-scoped role assignment. |
| `STORAGE_RESULTS_CONTAINER_NAME` | `Storage__ContainerName`. |
| `CONTENT_UNDERSTANDING_ENDPOINT` | `ContentUnderstanding__Endpoint`; GA API version remains `2025-11-01`. |
| `CONTENT_UNDERSTANDING_ACCOUNT_ID`, `CONTENT_UNDERSTANDING_ACCOUNT_NAME` | Foundry identity and account scope for step 14 role assignment. |
| `COMPLETION_MODEL_NAME`, `COMPLETION_MODEL_VERSION`, `COMPLETION_DEPLOYMENT_NAME`, `COMPLETION_DEPLOYMENT_CAPACITY` | Step 15 defaults bootstrap inputs; model name is distinct from deployment name. |
| `EMBEDDING_MODEL_NAME`, `EMBEDDING_MODEL_VERSION`, `EMBEDDING_DEPLOYMENT_NAME`, `EMBEDDING_DEPLOYMENT_CAPACITY` | Step 15 defaults bootstrap inputs. |
| `KEY_VAULT_ID`, `KEY_VAULT_NAME`, `KEY_VAULT_URI` | Vault identity and App Service reference metadata. |
| `CONTENT_UNDERSTANDING_API_KEY_SECRET_NAME`, `CONTENT_UNDERSTANDING_API_KEY_SECRET_URI` | Versionless secret reference metadata for the step 14 App Service key setting. |

The frontend module maps these values to app settings, keeps
`Storage__AuthenticationMode` as `ManagedIdentity`, and omits
`Storage__AccountName` and `Storage__AccountKey`. Explicit storage account-key
mode remains available for local configuration only; do not emit a key as an
output. The output names above are infrastructure contracts, not app settings
that are automatically bound.

In Foundry API-key mode, App Service uses a Key Vault reference to
`CONTENT_UNDERSTANDING_API_KEY_SECRET_URI`; in managed-identity mode it omits
`ContentUnderstanding__ApiKey`. Managed identity remains the default.

## Local validation

With the Bicep CLI installed, compile and lint without connecting to Azure:

```powershell
az bicep build --file .\infra\main.bicep --outfile <temporary-file>
az bicep lint --file .\infra\main.bicep
```

Compilation validates the local template, not region availability, resource
names' global availability, model/version/SKU support, quota/capacity,
permissions, deployment, or application readiness. Specifically defer Foundry
region support and capacity for both pinned deployments, model catalog state,
subscription quota, resource-name availability, provider registration, and
operator permissions to step 18. Do not infer deployability from Bicep
compilation and do not provision this composition before the authorized
deployment step. Cloud preflight
and provisioning require later plan steps and explicit authorization. Foundry
model tokens, Content Understanding use, Log Analytics, Application Insights,
Key Vault, and Storage can incur charges once deployed.
