# Infrastructure foundation

This is the Step 12 foundation, not a complete deployable application. The
subscription-scope Bicep entry point creates a resource group containing a Log
Analytics workspace, workspace-based Application Insights, and a StorageV2
account with a private results container. Foundry, Key Vault, App Service,
role assignments, AZD service wiring, and the Content Understanding defaults
bootstrap are later steps. No Azure resource or live access has been validated.

## Entry point and naming

`main.bicep` accepts:

| Parameter | Contract |
| --- | --- |
| `environmentName` | Required AZD environment name, 1-64 characters; also used in the `azd-env-name` tag. |
| `location` | Required Azure region for the resource group and these resources. |
| `resultsContainerName` | Blob container name, 3-63 characters; default `analysis-results`. Use lowercase letters, digits and single hyphens, beginning and ending with a letter or digit. |

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
most 24 characters. Other name portions are limited to 32 characters. Keep
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

The later frontend must map these values to app settings, keep
`Storage__AuthenticationMode` as `ManagedIdentity` by default, and omit
`Storage__AccountName` and `Storage__AccountKey` in that mode. Explicit key
mode requires both settings and secure key delivery; do not emit a key as an
output. The output names above are infrastructure contracts, not app settings
that are automatically bound.

## Local validation

With the Bicep CLI installed, compile and lint without connecting to Azure:

```powershell
bicep build .\infra\main.bicep --stdout
bicep lint .\infra\main.bicep
```

Compilation validates the local template, not region availability, resource
names' global availability, permissions, quotas, deployment, or application
readiness. Do not provision this partial composition. Cloud preflight and
provisioning require later plan steps and explicit authorization. Log
Analytics, Application Insights, and Storage can incur charges once deployed.
