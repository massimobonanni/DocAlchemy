using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.Core;
using Azure.Core.Pipeline;
using DocAlchemy.ContentUnderstanding.Configuration;
using DocAlchemy.Core.Interfaces;
using DocAlchemy.ContentUnderstanding.Services;
using Microsoft.Extensions.Options;

namespace DocAlchemy.ContentUnderstanding.Tests;

public sealed class AnalyzerServiceTests
{
    [Fact]
    public async Task CreateSendsUnchangedJsonAndDoesNotAllowReplacement()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                var response = CreateResponse(HttpStatusCode.OK, "{\"analyzerId\":\"test-analyzer\"}");
                response.Headers.TryAddWithoutValidation(
                    "Operation-Location",
                    "https://example.services.ai.azure.com/contentunderstanding/analyzers/test-analyzer/operations/op-1?api-version=2025-11-01");
                return Task.FromResult(response);
            }

            return Task.FromResult(CreateResponse(HttpStatusCode.OK, "{\"status\":\"Succeeded\"}"));
        });
        using var provider = CreateProvider(handler);
        var service = provider.Service;
        const string requestJson = "{\n  \"baseAnalyzerId\" : \"prebuilt-document\", \"config\":{\"estimateFieldSourceAndConfidence\":true}, \"x-extension\": {\"enabled\":true}\n}";

        await service.CreateAsync("test-analyzer", requestJson);

        var request = handler.Requests.First();
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(requestJson, request.Body);
        Assert.Contains("/analyzers/test-analyzer", request.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("allowReplace=false", request.Uri.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("invalid/id", "{\"baseAnalyzerId\":\"prebuilt-document\"}")]
    [InlineData("valid-id", "{\"analyzerId\":\"other-id\",\"baseAnalyzerId\":\"prebuilt-document\"}")]
    [InlineData("valid-id", "[]")]
    [InlineData("valid-id", "{\"description\":\"missing base\"}")]
    public async Task RejectsInvalidCreateRequestWithoutSending(
        string analyzerId,
        string requestJson)
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Unexpected HTTP request."));
        using var provider = CreateProvider(handler);
        var service = provider.Service;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.CreateAsync(analyzerId, requestJson));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DuplicateCreateIsAConflictAndNeverRetriesAsReplacement()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.Conflict, "{\"error\":{\"code\":\"Conflict\"}}")));
        using var provider = CreateProvider(handler);
        var service = provider.Service;

        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => service.CreateAsync("already-exists", "{\"baseAnalyzerId\":\"prebuilt-document\"}"));

        Assert.Equal(409, exception.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("allowReplace=false", request.Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePropagatesAsynchronousOperationFailure()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                var accepted = CreateResponse(HttpStatusCode.OK, "{\"analyzerId\":\"test-analyzer\"}");
                accepted.Headers.TryAddWithoutValidation(
                    "Operation-Location",
                    "https://example.services.ai.azure.com/contentunderstanding/analyzers/test-analyzer/operations/op-1?api-version=2025-11-01");
                return Task.FromResult(accepted);
            }

            return Task.FromResult(CreateResponse(
                HttpStatusCode.InternalServerError,
                "{\"error\":{\"code\":\"OperationFailed\"}}"));
        });
        using var provider = CreateProvider(handler);

        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => provider.Service.CreateAsync(
                "test-analyzer",
                "{\"baseAnalyzerId\":\"prebuilt-document\"}"));

        Assert.Equal(500, exception.Status);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ThrottlingFailureIsSurfacedAfterSdkRetries()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var response = CreateResponse(HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":\"TooManyRequests\"}}");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        using var provider = CreateProvider(handler);

        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => provider.Service.GetAsync("throttled-analyzer"));

        Assert.Equal(429, exception.Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ListingConsumesEveryPageAndComputesConfidenceWarnings()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal))
            {
                return Task.FromResult(CreateResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "value": [
                        {
                          "analyzerId": "prebuilt-invoice",
                          "description": "Invoice",
                          "status": "ready",
                          "config": { "estimateFieldSourceAndConfidence": true },
                          "fieldSchema": { "fields": { "amount": { "type": "number" } } }
                        },
                        {
                          "analyzerId": "field-override",
                          "baseAnalyzerId": "prebuilt-document",
                          "status": "ready",
                          "config": { "estimateFieldSourceAndConfidence": false },
                          "fieldSchema": { "fields": { "amount": { "estimateSourceAndConfidence": true } } }
                        }
                      ]
                    }
                    """));
            }

            return Task.FromResult(CreateResponse(
                HttpStatusCode.OK,
                """
                {
                  "value": [
                    {
                      "analyzerId": "invoice-custom",
                      "baseAnalyzerId": "prebuilt-document",
                      "description": "Custom invoice",
                      "status": "ready",
                      "config": { "estimateFieldSourceAndConfidence": true },
                      "fieldSchema": { "fields": { "total": { "estimateSourceAndConfidence": false } } }
                    }
                  ],
                  "nextLink": "https://example.services.ai.azure.com/contentunderstanding/analyzers?api-version=2025-11-01&page=2"
                }
                """));
        });
        using var provider = CreateProvider(handler);
        var service = provider.Service;

        var analyzers = await service.ListAsync();

        Assert.Equal(3, analyzers.Length);
        Assert.Equal("document", analyzers[0].Modality);
        Assert.Equal("ready", analyzers[0].Status);
        Assert.True(analyzers[0].HasConfidenceWarning);
        Assert.True(analyzers[1].IsBuiltIn);
        Assert.True(analyzers[1].IsReadOnly);
        Assert.Equal("document", analyzers[1].Modality);
        Assert.False(analyzers[1].HasConfidenceWarning);
        Assert.Equal("field-override", analyzers[2].Id);
        Assert.False(analyzers[2].HasConfidenceWarning);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetMapsMissingAnalyzerToNullButPropagatesOtherFailures()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"NotFound\"}}")));
        using var provider = CreateProvider(handler);
        var service = provider.Service;

        Assert.Null(await service.GetAsync("missing-analyzer"));

        var failedHandler = new RecordingHandler((_, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"Forbidden\"}}")));
        using var failedProvider = CreateProvider(failedHandler);
        var failedService = failedProvider.Service;
        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => failedService.GetAsync("restricted-analyzer"));

        Assert.Equal(403, exception.Status);
    }

    [Fact]
    public async Task DeleteRejectsBuiltInsBeforeSendingAndPropagatesMissingAnalyzer()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"NotFound\"}}")));
        using var provider = CreateProvider(handler);
        var service = provider.Service;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync("prebuilt-invoice"));
        Assert.Empty(handler.Requests);

        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => service.DeleteAsync("missing-analyzer"));

        Assert.Equal(404, exception.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CreatePollingHonorsCancellation()
    {
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                var response = CreateResponse(HttpStatusCode.OK, "{\"analyzerId\":\"test-analyzer\"}");
                response.Headers.TryAddWithoutValidation(
                    "Operation-Location",
                    "https://example.services.ai.azure.com/contentunderstanding/analyzers/test-analyzer/operations/op-1?api-version=2025-11-01");
                return response;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return CreateResponse(HttpStatusCode.OK);
        });
        using var provider = CreateProvider(handler);
        var service = provider.Service;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CreateAsync(
                "test-analyzer",
                "{\"baseAnalyzerId\":\"prebuilt-document\"}",
                cancellation.Token));

        Assert.True(handler.Requests.Count >= 2);
    }

    [Fact]
    public async Task CreatePollingTimesOutAtTheConfiguredBound()
    {
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                var response = CreateResponse(HttpStatusCode.OK, "{\"analyzerId\":\"test-analyzer\"}");
                response.Headers.TryAddWithoutValidation(
                    "Operation-Location",
                    "https://example.services.ai.azure.com/contentunderstanding/analyzers/test-analyzer/operations/op-1?api-version=2025-11-01");
                return response;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return CreateResponse(HttpStatusCode.OK);
        });
        using var provider = CreateProvider(handler, TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TimeoutException>(
            () => provider.Service.CreateAsync(
                "test-analyzer",
                "{\"baseAnalyzerId\":\"prebuilt-document\"}"));

        Assert.True(handler.Requests.Count >= 2);
    }

    private static TestServices CreateProvider(RecordingHandler handler, TimeSpan? timeout = null)
    {
        var options = new ContentUnderstandingClientOptions
        {
            Transport = new HttpClientTransport(new HttpClient(handler))
        };
        options.Retry.MaxRetries = 1;
        var client = new ContentUnderstandingClient(
            new Uri("https://example.services.ai.azure.com"),
            new AzureKeyCredential("synthetic-test-key"),
            options);
        var analyzerOptions = Options.Create(new AnalyzerOperationOptions
        {
            OperationTimeout = timeout ?? TimeSpan.FromMinutes(10),
            PollInterval = timeout is null ? TimeSpan.FromSeconds(2) : TimeSpan.FromMilliseconds(10)
        });
        return new TestServices(client, analyzerOptions);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string body = "")
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return response;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri!, body));
            return await responseFactory(request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body);

    private sealed class TestServices(
        ContentUnderstandingClient client,
        IOptions<AnalyzerOperationOptions> options) : IDisposable
    {
        public IAnalyzerService Service { get; } = new AnalyzerService(client, options);

        public void Dispose() => (client as IDisposable)?.Dispose();
    }
}
