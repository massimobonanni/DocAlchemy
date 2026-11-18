using System.Collections.Immutable;
using Bunit;
using DocAlchemy.Core.Interfaces;
using DocAlchemy.Core.Models;
using DocAlchemy.Web.Components.Pages;
using DocAlchemy.Web.Configuration;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocAlchemy.Web.Tests;

public sealed class AnalysisWorkflowTests : BunitContext
{
    [Fact]
    public async Task FiltersAnalyzerTypesAndAcceptsExactConfiguredUploadLimit()
    {
        var analyzers = new FakeAnalyzerService
        {
            Analyzers =
            [
                new AnalyzerMetadata("document-analyzer", modality: "document", supportedOperations: ["analyze"]),
                new AnalyzerMetadata("image-analyzer", modality: "image", supportedOperations: ["analyze"]),
                new AnalyzerMetadata("audio-analyzer", modality: "audio", supportedOperations: ["analyze"]),
                new AnalyzerMetadata("not-analyzeable", modality: "document", supportedOperations: ["delete"])
            ]
        };
        var analysis = new FakeDocumentAnalysisService();
        var store = new FakeAnalysisResultStore();
        RegisterServices(analyzers, analysis, store, maximumBytes: 8);

        var page = Render<Analyze>();

        Assert.Contains("document-analyzer", page.Markup, StringComparison.Ordinal);
        Assert.Contains("image-analyzer", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("audio-analyzer", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("not-analyzeable", page.Markup, StringComparison.Ordinal);

        page.Find("#document-analyzer").Change("document-analyzer");
        await SelectFileAsync(page, new TestBrowserFile("memo.md", "12345678"u8.ToArray(), "text/plain"));
        await page.Find("form").SubmitAsync();

        Assert.Equal(1, analysis.Calls);
        Assert.Equal("document-analyzer", analysis.AnalyzerId);
        Assert.Equal(0.80, analysis.ConfidenceThreshold);
        Assert.Equal("text/markdown", analysis.ContentType);
        Assert.Equal("12345678", System.Text.Encoding.UTF8.GetString(analysis.DocumentBytes!));
        Assert.Equal(1, store.Calls);
        Assert.Contains("The analysis completed without content items.", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Analysis results were saved.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsOversizedEmptyUnsupportedAndMismatchedUploadsBeforeAnalysis()
    {
        var analysis = new FakeDocumentAnalysisService();
        RegisterServices(new FakeAnalyzerService(), analysis, new FakeAnalysisResultStore(), maximumBytes: 8);
        var page = Render<Analyze>();

        await SelectFileAsync(page, new TestBrowserFile("too-large.pdf", new byte[9], "application/pdf"));
        Assert.Contains("exceeds the 8 bytes limit", page.Markup, StringComparison.Ordinal);
        Assert.Equal(0, analysis.Calls);

        await SelectFileAsync(page, new TestBrowserFile("empty.txt", [], "text/plain"));
        Assert.Contains("document is empty", page.Markup, StringComparison.Ordinal);

        await SelectFileAsync(page, new TestBrowserFile("wrong.pdf", [1], "image/png"));
        Assert.Contains("does not match its declared content type", page.Markup, StringComparison.Ordinal);

        await SelectFileAsync(page, new TestBrowserFile("unsupported.csv", [1], "text/csv"));
        Assert.Contains("supported document file type", page.Markup, StringComparison.Ordinal);
        Assert.Equal(0, analysis.Calls);
    }

    [Fact]
    public async Task StorageRetryUsesTheSameSnapshotAndNeverRepeatsAnalysis()
    {
        var analyzerSettings = new AnalysisOptions { ConfidenceThreshold = 0.42 };
        var analysis = new FakeDocumentAnalysisService();
        var store = new FakeAnalysisResultStore
        {
            SaveException = new InvalidOperationException("storage unavailable")
        };
        RegisterServices(
            new FakeAnalyzerService
            {
                Analyzers = [new AnalyzerMetadata("document-analyzer", modality: "document", supportedOperations: ["analyze"])]
            },
            analysis,
            store,
            analysisSettings: analyzerSettings);
        var page = Render<Analyze>();
        page.Find("#document-analyzer").Change("document-analyzer");
        await SelectFileAsync(page, new TestBrowserFile("first.pdf", [1, 2], "application/pdf"));
        await page.Find("form").SubmitAsync();

        var originalSnapshot = Assert.Single(store.Snapshots);
        Assert.Equal(0.42, originalSnapshot.ConfidenceThreshold);
        Assert.Contains("Analysis completed, but saving failed", page.Markup, StringComparison.Ordinal);
        Assert.True(page.Find("fieldset").HasAttribute("disabled"));

        analyzerSettings.ConfidenceThreshold = 0.91;
        store.SaveException = null;
        await page.Find("#retry-save-analysis").ClickAsync();

        Assert.Equal(1, analysis.Calls);
        Assert.Equal(2, store.Calls);
        Assert.Same(originalSnapshot, store.Snapshots[1]);
        Assert.Equal(0.42, store.Snapshots[1].ConfidenceThreshold);
        Assert.Contains("Analysis results were saved.", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("#retry-save-analysis"));
    }

    [Fact]
    public async Task CircuitDisposalCancelsAnalysisAndDoesNotSave()
    {
        var analysis = new FakeDocumentAnalysisService { WaitForCancellation = true };
        var store = new FakeAnalysisResultStore();
        RegisterServices(
            new FakeAnalyzerService
            {
                Analyzers = [new AnalyzerMetadata("document-analyzer", modality: "document", supportedOperations: ["analyze"])]
            },
            analysis,
            store);
        var page = Render<Analyze>();
        page.Find("#document-analyzer").Change("document-analyzer");
        await SelectFileAsync(page, new TestBrowserFile("document.txt", [1], "text/plain"));

        var submission = page.Find("form").SubmitAsync();
        Assert.Equal(1, analysis.Calls);
        page.Instance.Dispose();
        await submission;

        Assert.True(analysis.CancellationObserved);
        Assert.Equal(0, store.Calls);
    }

    private void RegisterServices(
        FakeAnalyzerService analyzers,
        FakeDocumentAnalysisService analysis,
        FakeAnalysisResultStore store,
        long maximumBytes = 20_971_520,
        AnalysisOptions? analysisSettings = null)
    {
        Services.AddSingleton<IAnalyzerService>(analyzers);
        Services.AddSingleton<IDocumentAnalysisService>(analysis);
        Services.AddSingleton<IAnalysisResultStore>(store);
        Services.AddSingleton<IOptions<UploadsOptions>>(
            Options.Create(new UploadsOptions { MaxDocumentBytes = maximumBytes }));
        Services.AddSingleton<IOptions<AnalysisOptions>>(
            Options.Create(analysisSettings ?? new AnalysisOptions()));
    }

    private static async Task SelectFileAsync(IRenderedComponent<Analyze> page, IBrowserFile file)
    {
        var input = page.FindComponent<InputFile>();
        await page.InvokeAsync(() => input.Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([file])));
    }

    private sealed class FakeAnalyzerService : IAnalyzerService
    {
        public ImmutableArray<AnalyzerMetadata> Analyzers { get; init; } = ImmutableArray<AnalyzerMetadata>.Empty;

        public Task<ImmutableArray<AnalyzerMetadata>> ListAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Analyzers);
        }

        public Task<AnalyzerMetadata?> GetAsync(string analyzerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AnalyzerMetadata?>(null);

        public Task CreateAsync(
            string analyzerId,
            string createRequestJson,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string analyzerId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeDocumentAnalysisService : IDocumentAnalysisService
    {
        private readonly TaskCompletionSource<AnalysisResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }
        public string? AnalyzerId { get; private set; }
        public string? ContentType { get; private set; }
        public double ConfidenceThreshold { get; private set; }
        public byte[]? DocumentBytes { get; private set; }
        public bool WaitForCancellation { get; init; }
        public bool CancellationObserved { get; private set; }

        public async Task<AnalysisResult> AnalyzeAsync(
            DocumentAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            AnalyzerId = request.AnalyzerId;
            ContentType = request.ContentType;
            ConfidenceThreshold = request.ConfidenceThreshold;
            using var buffer = new MemoryStream();
            await request.Content.CopyToAsync(buffer, cancellationToken);
            DocumentBytes = buffer.ToArray();

            if (WaitForCancellation)
            {
                using var registration = cancellationToken.Register(() =>
                {
                    CancellationObserved = true;
                    completion.TrySetCanceled(cancellationToken);
                });
                return await completion.Task;
            }

            return new AnalysisResult(
                Guid.NewGuid(),
                request.AnalyzerId,
                request.ConfidenceThreshold,
                DateTimeOffset.UtcNow,
                ImmutableArray<AnalysisContent>.Empty);
        }
    }

    private sealed class FakeAnalysisResultStore : IAnalysisResultStore
    {
        public int Calls { get; private set; }
        public Exception? SaveException { get; set; }
        public List<AnalysisResult> Snapshots { get; } = [];

        public Task<AnalysisStorageReference> SaveAsync(
            AnalysisResult result,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Snapshots.Add(result);
            return SaveException is null
                ? Task.FromResult(new AnalysisStorageReference("opaque-test-reference"))
                : Task.FromException<AnalysisStorageReference>(SaveException);
        }
    }

    private sealed class TestBrowserFile(string name, byte[] bytes, string contentType) : IBrowserFile
    {
        public string Name { get; } = name;
        public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;
        public long Size { get; } = bytes.LongLength;
        public string ContentType { get; } = contentType;

        public Stream OpenReadStream(long maxAllowedSize = 512_000, CancellationToken cancellationToken = default)
        {
            if (Size > maxAllowedSize)
            {
                throw new IOException("The file is larger than the allowed size.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new MemoryStream(bytes, writable: false);
        }
    }
}
