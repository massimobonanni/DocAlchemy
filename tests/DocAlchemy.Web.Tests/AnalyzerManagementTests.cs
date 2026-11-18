using System.Collections.Immutable;
using System.Text;
using Azure;
using Bunit;
using DocAlchemy.Core.Interfaces;
using DocAlchemy.Core.Models;
using DocAlchemy.Web.Components.Pages;
using DocAlchemy.Web.Configuration;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocAlchemy.Web.Tests;

public sealed class AnalyzerManagementTests : BunitContext
{
    [Fact]
    public void ListsMetadataAndRestrictsDeleteToSupportedMutableAnalyzers()
    {
        var service = new FakeAnalyzerService
        {
            Analyzers =
            [
                new AnalyzerMetadata(
                    "custom-document",
                    "Custom definition",
                    "document",
                    "ready",
                    supportedOperations: ["analyze", "delete"]),
                new AnalyzerMetadata(
                    "prebuilt-layout",
                    "Built-in definition",
                    "document",
                    "ready",
                    isBuiltIn: true,
                    isReadOnly: true,
                    supportedOperations: ["analyze"]),
                new AnalyzerMetadata(
                    "no-delete-operation",
                    supportedOperations: ["analyze"])
            ]
        };
        RegisterService(service);

        var page = Render<Analyzers>();

        Assert.Contains("Custom definition", page.Markup, StringComparison.Ordinal);
        Assert.Contains("document", page.Markup, StringComparison.Ordinal);
        Assert.Contains("ready", page.Markup, StringComparison.Ordinal);
        Assert.Contains("analyze, delete", page.Markup, StringComparison.Ordinal);
        Assert.Single(page.FindAll("#delete-analyzer-custom-document"));
        Assert.Empty(page.FindAll("#delete-analyzer-prebuilt-layout"));
        Assert.Empty(page.FindAll("#delete-analyzer-no-delete-operation"));
        Assert.Single(page.FindAll(".analyzer-badge"));

        page.Find("#refresh-analyzers").Click();

        Assert.Equal(2, service.ListCalls);
    }

    [Fact]
    public void ShowsLoadingAndEmptyStatesForAnEmptyList()
    {
        var service = new FakeAnalyzerService
        {
            ListCompletion = new TaskCompletionSource<ImmutableArray<AnalyzerMetadata>>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        RegisterService(service);

        var page = Render<Analyzers>();
        Assert.Contains("Loading analyzers.", page.Markup, StringComparison.Ordinal);

        service.ListCompletion.SetResult(ImmutableArray<AnalyzerMetadata>.Empty);
        page.WaitForAssertion(() =>
            Assert.Contains("No analyzers are available yet.", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportsUnchangedJsonWithSeparateIdAndShowsConfidenceWarning()
    {
        const string rawJson = """
            {
              "baseAnalyzerId": "prebuilt-document",
              "description": "Keep  spaces and casing",
              "config": { "estimateFieldSourceAndConfidence": false },
              "fieldSchema": { "fields": { "title": { "type": "string" } } }
            }
            """;
        var service = new FakeAnalyzerService();
        RegisterService(service);

        var page = Render<Analyzers>();
        page.Find("#analyzer-id").Input("custom-from-json");
        await SelectJsonAsync(page, "definition.json", rawJson);

        Assert.Contains("Confidence estimation is not enabled", page.Markup, StringComparison.Ordinal);
        page.Find("form").Submit();

        Assert.Equal("custom-from-json", service.CreatedAnalyzerId);
        Assert.Equal(rawJson, service.CreatedJson);
        Assert.Equal(2, service.ListCalls);
        Assert.Contains("Analyzer custom-from-json was created.", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Confidence estimation is not enabled", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreventsDuplicateCreateSubmissions()
    {
        var service = new FakeAnalyzerService
        {
            CreateCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        RegisterService(service);
        var page = Render<Analyzers>();
        page.Find("#analyzer-id").Input("custom");
        await SelectJsonAsync(
            page,
            "definition.json",
            """{"baseAnalyzerId":"prebuilt-document"}""");

        var firstSubmission = page.Find("form").SubmitAsync();
        await page.Find("form").SubmitAsync();
        Assert.Equal(1, service.CreateCalls);

        service.CreateCompletion.SetResult();
        await firstSubmission;
        Assert.Contains("Analyzer custom was created.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsMalformedAndOversizedJsonBeforeCallingService()
    {
        var service = new FakeAnalyzerService();
        RegisterService(service, maximumUploadBytes: 32);
        var page = Render<Analyzers>();

        await SelectJsonAsync(page, "invalid.json", "{ not json }");
        Assert.Contains("not valid JSON", page.Markup, StringComparison.Ordinal);

        await SelectJsonAsync(
            page,
            "large.json",
            """{"baseAnalyzerId":"prebuilt-document","description":"This request is larger than the configured limit."}""");
        Assert.Contains("exceeds the 32 bytes limit", page.Markup, StringComparison.Ordinal);

        page.Find("#analyzer-id").Input("custom");
        page.Find("form").Submit();

        Assert.Null(service.CreatedJson);
        Assert.Contains("exceeds the 32 bytes limit", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsAnalyzerIdInBodyAndInvalidAnalyzerIds()
    {
        var service = new FakeAnalyzerService();
        RegisterService(service);
        var page = Render<Analyzers>();
        page.Find("#analyzer-id").Input("has spaces");
        await SelectJsonAsync(
            page,
            "definition.json",
            """{"analyzerId":"body-id","baseAnalyzerId":"prebuilt-document"}""");

        Assert.Contains("Provide the analyzer ID separately", page.Markup, StringComparison.Ordinal);
        page.Find("form").Submit();

        Assert.Null(service.CreatedAnalyzerId);
        Assert.Contains("1–64 ASCII letters", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresExactIdConfirmationAndRefreshesAfterDelete()
    {
        var service = new FakeAnalyzerService
        {
            Analyzers =
            [
                new AnalyzerMetadata(
                    "custom-document",
                    supportedOperations: ["analyze", "delete"])
            ]
        };
        RegisterService(service);
        var page = Render<Analyzers>();

        page.Find("#delete-analyzer-custom-document").Click();
        Assert.Contains("Type", page.Markup, StringComparison.Ordinal);
        Assert.Contains("custom-document", page.Markup, StringComparison.Ordinal);
        Assert.True(page.Find("#confirm-delete-analyzer").HasAttribute("disabled"));

        page.Find("#delete-confirmation-input").Input("CUSTOM-DOCUMENT");
        Assert.True(page.Find("#confirm-delete-analyzer").HasAttribute("disabled"));
        page.Find("#delete-confirmation-input").Input("custom-document");
        Assert.False(page.Find("#confirm-delete-analyzer").HasAttribute("disabled"));
        page.Find("#confirm-delete-analyzer").Click();

        Assert.Equal("custom-document", service.DeletedAnalyzerId);
        Assert.Equal(2, service.ListCalls);
        Assert.Contains("Analyzer custom-document was deleted.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsDeleteFailuresWithoutRemovingTheAnalyzer()
    {
        var service = new FakeAnalyzerService
        {
            Analyzers =
            [
                new AnalyzerMetadata(
                    "custom-document",
                    supportedOperations: ["analyze", "delete"])
            ],
            DeleteException = new InvalidOperationException("delete unavailable")
        };
        RegisterService(service);
        var page = Render<Analyzers>();
        page.Find("#delete-analyzer-custom-document").Click();
        page.Find("#delete-confirmation-input").Input("custom-document");
        page.Find("#confirm-delete-analyzer").Click();

        Assert.Contains("Could not delete analyzer custom-document: delete unavailable", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Analyzer custom-document was deleted.", page.Markup, StringComparison.Ordinal);
        Assert.Contains("custom-document", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowsServiceFailuresWithoutReportingSuccess()
    {
        var service = new FakeAnalyzerService
        {
            ListException = new InvalidOperationException("list unavailable")
        };
        RegisterService(service);

        var page = Render<Analyzers>();
        Assert.Contains("Could not load analyzers: list unavailable", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("No analyzers are available yet", page.Markup, StringComparison.Ordinal);

        service.ListException = null;
        await SelectJsonAsync(
            page,
            "definition.json",
            """{"baseAnalyzerId":"prebuilt-document"}""");
        page.Find("#analyzer-id").Input("custom");
        service.CreateException = new RequestFailedException(409, "conflict");
        page.Find("form").Submit();

        Assert.Contains("already exists", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("was created.", page.Markup, StringComparison.Ordinal);
    }

    private void RegisterService(FakeAnalyzerService service, long maximumUploadBytes = 20_971_520)
    {
        Services.AddSingleton<IAnalyzerService>(service);
        Services.AddSingleton<IOptions<UploadsOptions>>(
            Options.Create(new UploadsOptions { MaxDocumentBytes = maximumUploadBytes }));
    }

    private static async Task SelectJsonAsync(
        IRenderedComponent<Analyzers> page,
        string fileName,
        string json)
    {
        var file = new TestBrowserFile(fileName, Encoding.UTF8.GetBytes(json));
        var input = page.FindComponent<InputFile>();
        await page.InvokeAsync(() => input.Instance.OnChange.InvokeAsync(
            new InputFileChangeEventArgs([file])));
    }

    private sealed class FakeAnalyzerService : IAnalyzerService
    {
        public ImmutableArray<AnalyzerMetadata> Analyzers { get; set; } = ImmutableArray<AnalyzerMetadata>.Empty;
        public Exception? ListException { get; set; }
        public Exception? CreateException { get; set; }
        public Exception? DeleteException { get; set; }
        public TaskCompletionSource<ImmutableArray<AnalyzerMetadata>>? ListCompletion { get; set; }
        public TaskCompletionSource? CreateCompletion { get; set; }
        public int ListCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public string? CreatedAnalyzerId { get; private set; }
        public string? CreatedJson { get; private set; }
        public string? DeletedAnalyzerId { get; private set; }

        public Task<ImmutableArray<AnalyzerMetadata>> ListAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ListCalls++;
            if (ListException is not null)
            {
                return Task.FromException<ImmutableArray<AnalyzerMetadata>>(ListException);
            }

            return ListCompletion?.Task ?? Task.FromResult(Analyzers);
        }

        public Task<AnalyzerMetadata?> GetAsync(string analyzerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Analyzers.FirstOrDefault(analyzer =>
                string.Equals(analyzer.Id, analyzerId, StringComparison.Ordinal)));

        public Task CreateAsync(
            string analyzerId,
            string createRequestJson,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateCalls++;
            if (CreateException is not null)
            {
                return Task.FromException(CreateException);
            }

            CreatedAnalyzerId = analyzerId;
            CreatedJson = createRequestJson;
            return CreateCompletion?.Task ?? Task.CompletedTask;
        }

        public Task DeleteAsync(string analyzerId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DeleteException is not null)
            {
                return Task.FromException(DeleteException);
            }

            DeletedAnalyzerId = analyzerId;
            return Task.CompletedTask;
        }
    }

    private sealed class TestBrowserFile(string name, byte[] bytes) : IBrowserFile
    {
        public string Name { get; } = name;
        public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;
        public long Size { get; } = bytes.LongLength;
        public string ContentType { get; } = "application/json";

        public Stream OpenReadStream(long maxAllowedSize = 512_000, CancellationToken cancellationToken = default)
        {
            if (Size > maxAllowedSize)
            {
                throw new IOException("The file is larger than the allowed size.");
            }

            return new MemoryStream(bytes, writable: false);
        }
    }
}
