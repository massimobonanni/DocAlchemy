# Infrastructure foundation

This is the Step 13 infrastructure composition, not a complete deployable
application. The subscription-scope Bicep entry point creates a resource group
containing monitoring, private results storage, a Foundry `AIServices` account
with Content Understanding model deployments, and an RBAC-enabled Key Vault
holding the Foundry API key. App Service, role assignments, AZD service wiring,
and the Content Understanding defaults bootstrap are later steps. No Azure
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
| `modules/monitoring.bicep` | `location`, `workspaceName`, `applicationInsightsName`, `tags` | `workspaceId`, `applicationInsightsId` |
| `modules/storage.bicep` | `location`, `storageAccountName`, `containerName`, `tags` | `storageAccountId`, `storageAccountName`, `blobServiceEndpoint`, `containerId`, `containerName` |
| `modules/foundry.bicep` | Foundry `location`, account SKU/name, pinned model versions, deployment names/SKUs/capacities, `tags` | Foundry account ID/name/endpoint and nonsecret model/deployment metadata |
| `modules/keyvault.bicep` | Primary `location`, vault name, Foundry account name, `tags` | Vault ID/name/URI and versionless secret name/URI |

The monitoring module uses a 30-day Log Analytics retention period and
connects Application Insights to that workspace. A later frontend module must
retrieve the Application Insights connection string from the component and
set `APPLICATIONINSIGHTS_CONNECTION_STRING`; it is not an output of this
foundation.

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
and secret-reference metadata. The App Service Key Vault reference and
least-privilege `Key Vault Secrets User` assignment are for step 14; the
deployment operator must be authorized to write the secret.

The dependency graph is one-way: the Key Vault module receives the Foundry
account name from the Foundry module, and later Web/access modules may consume
the vault metadata and Web identity. The Foundry module does not depend on
Key Vault or the Web App, so adding the later secret reference and role
assignment does not create a deployment cycle.

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

The later frontend must map these values to app settings, keep
`Storage__AuthenticationMode` as `ManagedIdentity` by default, and omit
`Storage__AccountName` and `Storage__AccountKey` in that mode. Explicit key
mode requires both settings and secure key delivery; do not emit a key as an
output. The output names above are infrastructure contracts, not app settings
that are automatically bound.

In the Foundry key mode, App Service should use a Key Vault reference to
`CONTENT_UNDERSTANDING_API_KEY_SECRET_URI`; in managed-identity mode it must
omit `ContentUnderstanding__ApiKey`. Managed identity remains the default.

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
compilation and do not provision this composition in step 13. Cloud preflight
and provisioning require later plan steps and explicit authorization. Foundry
model tokens, Content Understanding use, Log Analytics, Application Insights,
Key Vault, and Storage can incur charges once deployed.
