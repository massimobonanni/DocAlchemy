# DocAlchemy

DocAlchemy is an interactive demo that showcases how Azure AI Content Understanding transforms unstructured documents into meaningful, structured data. Create custom analyzers from JSON definitions, upload a document, and explore extracted fields alongside their confidence scores.

## Repository structure

- `src/DocAlchemy.Core` contains service contracts and domain models.
- `src/DocAlchemy.ContentUnderstanding` contains the Content Understanding adapter.
- `src/DocAlchemy.Storage` contains the Blob Storage adapter.
- `src/DocAlchemy.Web` is the Interactive Server web application.
- `tests/DocAlchemy.Core.Tests` contains tests for Core.
- `tests/DocAlchemy.Storage.Tests` contains tests for Blob Storage.
- `tests/DocAlchemy.Web.Tests` contains tests for the web application.
- `tests/DocAlchemy.ContentUnderstanding.Tests` contains tests for Content Understanding.

The projects are scaffolds; application and Azure service behavior will be added in later implementation steps.

## Local checks

```powershell
dotnet build DocAlchemy.slnx
dotnet test DocAlchemy.slnx
dotnet run --project src/DocAlchemy.Web
```
