using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.Core.Pipeline;
using DocAlchemy.ContentUnderstanding.Configuration;
using DocAlchemy.ContentUnderstanding.Services;
using DocAlchemy.Core.Models;
using Microsoft.Extensions.Options;

namespace DocAlchemy.ContentUnderstanding.Tests;

public sealed class DocumentAnalysisServiceTests
{
    [Fact]
    public async Task SubmitsBinaryOncePollsAndMapsAllRecursiveValues()
    {
        var handler = new RecordingHandler((request, _) =>
            Task.FromResult(request.Method == HttpMethod.Post
                ? Accepted()
                : Response(HttpStatusCode.OK, """
                    {"status":"Succeeded","result":{"contents":[
                      {"kind":"document","fields":{
                        "text":{"type":"string","valueString":"hello","confidence":0.9},
                        "date":{"type":"date","valueDate":"2026-01-02"},
                        "time":{"type":"time","valueTime":"12:34:56"},
                        "number":{"type":"number","valueNumber":1.25},
                        "integer":{"type":"integer","valueInteger":42},
                        "boolean":{"type":"boolean","valueBoolean":true},
                        "json":{"type":"json","valueJson":{"a":[1,null]}},
                        "nil":{"type":"string","valueString":null,"confidence":null},
                        "nested/~":{"type":"object","confidence":0.7,"valueObject":{
                          "rows":{"type":"array","valueArray":[
                            {"type":"integer","valueInteger":4,"confidence":0.5},
                            {"type":"object","valueObject":{"flag":{"type":"boolean","valueBoolean":false,"confidence":0.8}}}
                          ]}
                        }},
                        "future":{"type":"future","valueFuture":{"x":1},"extra":"keep","confidence":0.4}
                      }},
                      {"kind":"document"},
                      {"fields":{}}
                    ]}}
                    """)));
        var service = CreateService(handler);
        using var input = new MemoryStream([1, 2, 3]);
        var started = DateTimeOffset.UtcNow;

        var result = await service.AnalyzeAsync(new DocumentAnalysisRequest("my-analyzer", input, "application/pdf", 0.8));

        Assert.Equal("my-analyzer", result.AnalyzerId);
        Assert.Equal(0.8, result.ConfidenceThreshold);
        Assert.NotEqual(Guid.Empty, result.AnalysisId);
        Assert.InRange(result.AnalyzedAtUtc, started, DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.Zero, result.AnalyzedAtUtc.Offset);
        Assert.Equal(3, result.Contents.Length);
        Assert.Empty(result.Contents[1].Fields);
        Assert.Empty(result.Contents[2].Fields);
        var fields = result.Contents[0].Fields.ToDictionary(field => field.Name);
        Assert.Equal("hello", fields["text"].Value.GetString());
        Assert.Equal("2026-01-02", fields["date"].Value.GetString());
        Assert.Equal("12:34:56", fields["time"].Value.GetString());
        Assert.Equal(1.25, fields["number"].Value.GetDouble());
        Assert.Equal(42, fields["integer"].Value.GetInt64());
        Assert.True(fields["boolean"].Value.GetBoolean());
        Assert.Equal(JsonValueKind.Null, fields["json"].Value.GetProperty("a")[1].ValueKind);
        Assert.Equal(JsonValueKind.Null, fields["nil"].Value.ValueKind);
        Assert.Null(fields["nil"].Confidence);
        Assert.Null(fields["date"].Confidence);
        var nested = fields["nested/~"];
        Assert.Equal("/contents/0/fields/nested~1~0", nested.Path);
        Assert.Equal(4, nested.Value.GetProperty("rows")[0].GetInt32());
        Assert.False(nested.Value.GetProperty("rows")[1].GetProperty("flag").GetBoolean());
        var rows = Assert.Single(nested.Children);
        Assert.Equal(0.5, rows.Children[0].Confidence);
        Assert.Equal("/contents/0/fields/nested~1~0/properties/rows/items/1/properties/flag", rows.Children[1].Children[0].Path);
        Assert.Equal(0.8, rows.Children[1].Children[0].Confidence);
        Assert.Equal("keep", fields["future"].Value.GetProperty("extra").GetString());
        Assert.Equal(0.4, fields["future"].Confidence);
        Assert.True(input.CanRead);
        var sent = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Post);
        Assert.Equal([1, 2, 3], sent.Body);
        Assert.Equal("application/pdf", sent.ContentType);
        Assert.Contains(":analyzeBinary", sent.Uri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(handler.Requests, request => request.Method == HttpMethod.Get);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"contents\":null}")]
    [InlineData("{\"contents\":[null]}")]
    [InlineData("{\"contents\":[{\"kind\":\"audioVisual\"}]}")]
    [InlineData("{\"contents\":[{\"fields\":null}]}")]
    [InlineData("{\"contents\":[{\"fields\":{\"x\":{}}}]}")]
    [InlineData("{\"contents\":[{\"fields\":{\"x\":{\"type\":\"number\"}}}]}")]
    [InlineData("{\"contents\":[{\"fields\":{\"x\":{\"type\":\"number\",\"valueNumber\":\"4\"}}}]}")]
    [InlineData("{\"contents\":[{\"fields\":{\"x\":{\"type\":\"integer\",\"valueInteger\":1.5}}}]}")]
    [InlineData("{\"contents\":[{\"fields\":{\"x\":{\"type\":\"string\",\"valueString\":\"x\",\"confidence\":1.1}}}]}")]
    [InlineData("{\"contents\":[{\"fields\":{\"x\":{\"type\":\"array\",\"valueArray\":[null]}}}]}")]
    [InlineData("{\"contents\":[{\"fields\":{\"x\":{\"type\":\"string\",\"type\":\"number\",\"valueNumber\":2}}}]}")]
    [InlineData("{\"contents\":[{\"fields\":{\"x\":{\"type\":\"string\",\"valueString\":\"a\"},\"x\":{\"type\":\"string\",\"valueString\":\"b\"}}}]}")]
    public void MalformedResultsFailExplicitly(string result)
    {
        Assert.Throws<InvalidDataException>(() =>
            AnalysisResultMapper.Map(WrapResult(result), "analyzer", 0.8, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void EmptyContentsAndNullCompositeValuesRemainValid()
    {
        var empty = AnalysisResultMapper.Map(WrapResult("{\"contents\":[]}"), "a", 0.8, DateTimeOffset.UtcNow);
        Assert.Empty(empty.Contents);

        var result = AnalysisResultMapper.Map(WrapResult("""
            {"contents":[{"fields":{"x":{"type":"array","valueArray":null},"y":{"type":"object","valueObject":null}}}]}
            """), "a", 0.8, DateTimeOffset.UtcNow);
        Assert.All(result.Contents[0].Fields, field =>
        {
            Assert.Equal(JsonValueKind.Null, field.Value.ValueKind);
            Assert.Empty(field.Children);
        });
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    public async Task NeverRetriesFailedSubmission(int status)
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var response = Response((HttpStatusCode)status, "{\"error\":{\"code\":\"failure\"}}");
            response.Headers.TryAddWithoutValidation("Retry-After", "0");
            return Task.FromResult(response);
        });

        var exception = await Assert.ThrowsAsync<AnalysisSubmissionException>(() => Analyze(CreateService(handler)));

        Assert.Equal(status, Assert.IsType<RequestFailedException>(exception.InnerException).Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NeverRetriesSubmissionWhenResponseIsLost()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("Connection lost."));

        var exception = await Assert.ThrowsAsync<AnalysisSubmissionException>(() => Analyze(CreateService(handler)));

        var serviceFailure = Assert.IsType<RequestFailedException>(exception.InnerException);
        Assert.IsType<HttpRequestException>(serviceFailure.InnerException);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NeverFollowsSubmissionRedirect()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var response = Response(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri("https://example.services.ai.azure.com/redirected:analyzeBinary");
            return Task.FromResult(response);
        });

        await Assert.ThrowsAsync<AnalysisSubmissionException>(() => Analyze(CreateService(handler)));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PollingRetriesThrottlingWithoutRepeatingSubmission()
    {
        var polls = 0;
        var stopwatch = Stopwatch.StartNew();
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Accepted());
            }

            if (Interlocked.Increment(ref polls) == 1)
            {
                var throttled = Response(HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":\"throttled\"}}");
                throttled.Headers.TryAddWithoutValidation("Retry-After", "1");
                return Task.FromResult(throttled);
            }

            return Task.FromResult(Response(HttpStatusCode.OK, "{\"status\":\"Succeeded\",\"result\":{\"contents\":[]}}"));
        });

        var result = await Analyze(CreateService(handler));

        Assert.Empty(result.Contents);
        Assert.Equal(1, handler.Requests.Count(request => request.Method == HttpMethod.Post));
        Assert.Equal(2, polls);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task PollsRunningOperationUntilItCompletes()
    {
        var polls = 0;
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Accepted());
            }

            var body = Interlocked.Increment(ref polls) == 1
                ? "{\"status\":\"Running\"}"
                : "{\"status\":\"Succeeded\",\"result\":{\"contents\":[]}}";
            return Task.FromResult(Response(HttpStatusCode.OK, body));
        });

        var result = await Analyze(CreateService(handler));

        Assert.Empty(result.Contents);
        Assert.Equal(2, polls);
        Assert.Equal(1, handler.Requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task PollingRetryLimitAndFailedOperationAreExplicit()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Accepted());
            }

            var response = Response(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"code\":\"unavailable\"}}");
            response.Headers.TryAddWithoutValidation("Retry-After", "0");
            return Task.FromResult(response);
        });
        var failure = await Assert.ThrowsAsync<RequestFailedException>(() => Analyze(CreateService(handler)));
        Assert.Equal(503, failure.Status);
        Assert.Equal(1, handler.Requests.Count(request => request.Method == HttpMethod.Post));
        Assert.Equal(4, handler.Requests.Count(request => request.Method == HttpMethod.Get));

        var failedOperation = new RecordingHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? Accepted()
            : Response(HttpStatusCode.OK, "{\"status\":\"Failed\",\"error\":{\"code\":\"AnalysisFailed\",\"message\":\"failed\"}}")));
        await Assert.ThrowsAsync<RequestFailedException>(() => Analyze(CreateService(failedOperation)));
        Assert.Equal(1, failedOperation.Requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PollingHonorsCallerCancellationAndTimeout(bool callerCancels)
    {
        var handler = new RecordingHandler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Accepted();
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK);
        });
        using var cancellation = new CancellationTokenSource();
        if (callerCancels)
        {
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(150));
        }

        var task = Analyze(CreateService(handler, TimeSpan.FromMilliseconds(callerCancels ? 3000 : 150)), cancellation.Token);
        if (callerCancels)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }
        else
        {
            await Assert.ThrowsAsync<TimeoutException>(() => task);
        }

        Assert.Equal(1, handler.Requests.Count(request => request.Method == HttpMethod.Post));
        Assert.Contains(handler.Requests, request => request.Method == HttpMethod.Get);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task EnforcesActualStreamLengthBeforeSubmission(int length, bool accepted)
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? Accepted()
            : Response(HttpStatusCode.OK, "{\"status\":\"Succeeded\",\"result\":{\"contents\":[]}}")));
        var service = CreateService(handler, maxBytes: 3);
        using var input = new MemoryStream(new byte[length]);
        var task = service.AnalyzeAsync(new DocumentAnalysisRequest("analyzer", input, "text/plain", 0.8));
        if (accepted)
        {
            await task;
            Assert.Contains(handler.Requests, request => request.Method == HttpMethod.Post);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => task);
            Assert.Empty(handler.Requests);
        }

        Assert.True(input.CanRead);
    }

    private static Task<DocAlchemy.Core.Models.AnalysisResult> Analyze(
        DocumentAnalysisService service, CancellationToken cancellationToken = default)
    {
        return service.AnalyzeAsync(
            new DocumentAnalysisRequest("analyzer", new MemoryStream([1]), "application/pdf", 0.8),
            cancellationToken);
    }

    private static BinaryData WrapResult(string result) => BinaryData.FromString($"{{\"result\":{result}}}");

    private static DocumentAnalysisService CreateService(RecordingHandler handler, TimeSpan? timeout = null, long maxBytes = 100)
    {
        var options = new ContentUnderstandingClientOptions
        {
            Transport = new HttpClientTransport(new HttpClient(handler)),
            RetryPolicy = new AnalysisRetryPolicy()
        };
        var client = new ContentUnderstandingClient(
            new Uri("https://example.services.ai.azure.com"), new AzureKeyCredential("synthetic-test-key"), options);
        return new DocumentAnalysisService(
            client,
            Options.Create(new AnalyzerOperationOptions
            {
                OperationTimeout = timeout ?? TimeSpan.FromSeconds(10),
                PollInterval = TimeSpan.FromMilliseconds(10)
            }),
            Options.Create(new DocumentUploadOptions { MaxDocumentBytes = maxBytes }));
    }

    private static HttpResponseMessage Accepted()
    {
        var response = Response(HttpStatusCode.Accepted);
        response.Headers.TryAddWithoutValidation(
            "Operation-Location",
            "https://example.services.ai.azure.com/contentunderstanding/analyzerResults/op-1?api-version=2025-11-01");
        return response;
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body = "")
    {
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri!, body, request.Content?.Headers.ContentType?.MediaType));
            return await respond(request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, byte[] Body, string? ContentType);
}
