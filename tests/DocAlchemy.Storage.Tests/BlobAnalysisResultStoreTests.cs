using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Azure;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using DocAlchemy.Core.Models;
using DocAlchemy.Storage.Services;

namespace DocAlchemy.Storage.Tests;

public sealed class BlobAnalysisResultStoreTests
{
    public sealed record CapturedRequest(
        HttpMethod Method,
        string Path,
        byte[] Body,
        string? ContentType,
        string? IfNoneMatch);

    [Fact]
    public async Task SavesFullSnapshotAsPrivateJsonAndRepeatedSaveIsIdempotent()
    {
        var result = CreateResult();
        using var handler = new MemoryBlobHandler();
        var store = CreateStore(handler);

        var firstReference = await store.SaveAsync(result);
        var secondReference = await store.SaveAsync(result);

        var uploadRequests = handler.Requests.Where(static request => request.Method == HttpMethod.Put).ToArray();
        Assert.Equal(2, uploadRequests.Length);
        Assert.All(uploadRequests, request =>
        {
            Assert.Equal("application/json", request.ContentType);
            Assert.Equal(uploadRequests[0].Path, request.Path);
            Assert.Equal(uploadRequests[0].Body, request.Body);
            Assert.Contains("*", request.IfNoneMatch);
        });
        Assert.Equal(HttpMethod.Get, handler.Requests[2].Method);

        var expectedBlobName = $"{result.AnalysisId:D}.json";
        Assert.Equal(expectedBlobName, firstReference.Value);
        Assert.Equal(firstReference.Value, secondReference.Value);
        Assert.DoesNotContain("://", firstReference.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("?", firstReference.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("sig=", firstReference.Value, StringComparison.OrdinalIgnoreCase);

        using var json = JsonDocument.Parse(handler.StoredPayload!);
        var root = json.RootElement;
        Assert.Equal(
            ["schemaVersion", "analysisId", "analyzerId", "confidenceThreshold", "analyzedAtUtc", "contents"],
            root.EnumerateObject().Select(static property => property.Name).ToArray());
        Assert.Equal(AnalysisResult.CurrentSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(result.AnalysisId, root.GetProperty("analysisId").GetGuid());
        Assert.Equal("invoice-v2", root.GetProperty("analyzerId").GetString());
        Assert.Equal(0.8, root.GetProperty("confidenceThreshold").GetDouble());
        Assert.Equal(result.AnalyzedAtUtc, root.GetProperty("analyzedAtUtc").GetDateTimeOffset());

        var content = Assert.Single(root.GetProperty("contents").EnumerateArray().ToArray());
        Assert.Equal(0, content.GetProperty("index").GetInt32());
        var fields = content.GetProperty("fields");
        var score = fields[0];
        Assert.Equal(
            ["name", "path", "type", "value", "confidence", "children"],
            score.EnumerateObject().Select(static property => property.Name).ToArray());
        Assert.Equal("score", score.GetProperty("name").GetString());
        Assert.Equal("number", score.GetProperty("type").GetString());
        Assert.Equal(0.75, score.GetProperty("value").GetDouble());
        Assert.Equal(JsonValueKind.Null, score.GetProperty("confidence").ValueKind);
        Assert.Empty(score.GetProperty("children").EnumerateArray());

        var nullable = fields[1];
        Assert.Equal(JsonValueKind.Null, nullable.GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.Null, nullable.GetProperty("confidence").ValueKind);

        var nested = fields[2];
        Assert.Equal(JsonValueKind.True, nested.GetProperty("value").GetProperty("items")[0].GetProperty("active").ValueKind);
        Assert.Equal("boolean", nested.GetProperty("children")[0].GetProperty("type").GetString());
        Assert.True(nested.GetProperty("children")[0].GetProperty("value").GetBoolean());
        Assert.Equal(0.96, nested.GetProperty("children")[0].GetProperty("confidence").GetDouble());
    }

    [Fact]
    public async Task RetryAfterLostUploadResponseVerifiesExistingPayload()
    {
        var result = CreateResult();
        using var handler = new MemoryBlobHandler { LoseFirstUploadResponse = true };
        var store = CreateStore(handler);

        var lostResponse = await Assert.ThrowsAsync<RequestFailedException>(() => store.SaveAsync(result));
        Assert.IsType<HttpRequestException>(lostResponse.InnerException);
        var reference = await store.SaveAsync(result);

        Assert.Equal($"{result.AnalysisId:D}.json", reference.Value);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(HttpMethod.Put, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal(HttpMethod.Get, handler.Requests[2].Method);
        Assert.Equal(handler.Requests[0].Path, handler.Requests[1].Path);
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        Assert.Equal(handler.Requests[0].Body, handler.StoredPayload);
    }

    [Fact]
    public async Task FailedUploadCanBeRetriedWithSameIdentityAndPayload()
    {
        var result = CreateResult();
        using var handler = new MemoryBlobHandler { FailFirstUploadWithServiceUnavailable = true };
        var store = CreateStore(handler);

        var failure = await Assert.ThrowsAsync<RequestFailedException>(() => store.SaveAsync(result));
        Assert.Equal(503, failure.Status);
        var reference = await store.SaveAsync(result);

        Assert.Equal($"{result.AnalysisId:D}.json", reference.Value);
        Assert.Equal(handler.Requests[0].Path, handler.Requests[1].Path);
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task StorageFailuresAreSurfaced(HttpStatusCode statusCode)
    {
        using var handler = new MemoryBlobHandler { FailNextUploadWith = statusCode };
        var store = CreateStore(handler);

        var failure = await Assert.ThrowsAsync<RequestFailedException>(
            () => store.SaveAsync(CreateResult()));

        Assert.Equal((int)statusCode, failure.Status);
    }

    [Fact]
    public async Task ExistingAnalysisIdWithDifferentPayloadFailsExplicitly()
    {
        using var handler = new MemoryBlobHandler();
        var store = CreateStore(handler);
        var original = CreateResult();
        var conflicting = new AnalysisResult(
            original.AnalysisId,
            "different-analyzer",
            original.ConfidenceThreshold,
            original.AnalyzedAtUtc,
            original.Contents);

        await store.SaveAsync(original);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveAsync(conflicting));

        Assert.Contains("different analysis result", failure.Message, StringComparison.Ordinal);
    }

    private static BlobAnalysisResultStore CreateStore(MemoryBlobHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = new BlobClientOptions
        {
            Transport = new HttpClientTransport(httpClient)
        };
        options.Retry.MaxRetries = 0;
        var serviceClient = new BlobServiceClient(
            new Uri("https://storage.example.test"),
            options);
        return new BlobAnalysisResultStore(serviceClient, "private-results");
    }

    private static AnalysisResult CreateResult()
    {
        using var document = JsonDocument.Parse("""{"items":[{"active":true}]}""");
        var nestedValue = document.RootElement.Clone();
        var booleanValue = document.RootElement.GetProperty("items")[0].GetProperty("active").Clone();

        return new AnalysisResult(
            Guid.Parse("5c073e45-5508-492e-9520-8d219fe60042"),
            "invoice-v2",
            0.8,
            new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
            ImmutableArray.Create(
                new AnalysisContent(
                    0,
                    ImmutableArray.Create(
                        new ExtractedField(
                            "score",
                            "/contents/0/fields/score",
                            "number",
                            ParseJsonValue("0.75"),
                            null,
                            ImmutableArray<ExtractedField>.Empty),
                        new ExtractedField(
                            "optional",
                            "/contents/0/fields/optional",
                            "string",
                            ParseJsonValue("null"),
                            null,
                            ImmutableArray<ExtractedField>.Empty),
                        new ExtractedField(
                            "details",
                            "/contents/0/fields/details",
                            "object",
                            nestedValue,
                            0.99,
                            ImmutableArray.Create(
                                new ExtractedField(
                                    "active",
                                    "/contents/0/fields/details/properties/items/items/0/properties/active",
                                    "boolean",
                                    booleanValue,
                                    0.96,
                                    ImmutableArray<ExtractedField>.Empty)))))));
    }

    private static JsonElement ParseJsonValue(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class MemoryBlobHandler : HttpMessageHandler
    {
        private readonly List<CapturedRequest> requests = [];
        private bool hasUploaded;

        public IReadOnlyList<CapturedRequest> Requests => requests;
        public byte[]? StoredPayload { get; private set; }
        public bool LoseFirstUploadResponse { get; init; }
        public bool FailFirstUploadWithServiceUnavailable { get; init; }
        public HttpStatusCode? FailNextUploadWith { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            request.Headers.TryGetValues("x-ms-blob-content-type", out var blobContentTypes);
            var capture = new CapturedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                body,
                blobContentTypes?.FirstOrDefault(),
                request.Headers.IfNoneMatch.FirstOrDefault()?.Tag);
            requests.Add(capture);

            if (request.Method == HttpMethod.Put)
            {
                if (FailNextUploadWith is { } failureStatus)
                {
                    FailNextUploadWith = null;
                    return new HttpResponseMessage(failureStatus);
                }

                if (FailFirstUploadWithServiceUnavailable && requests.Count(static item => item.Method == HttpMethod.Put) is 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }

                if (hasUploaded)
                {
                    return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
                }

                StoredPayload = body;
                hasUploaded = true;
                if (LoseFirstUploadResponse)
                {
                    throw new HttpRequestException("Simulated loss of the successful upload response.");
                }

                var created = new HttpResponseMessage(HttpStatusCode.Created);
                created.Headers.TryAddWithoutValidation("ETag", "\"result-etag\"");
                created.Headers.TryAddWithoutValidation("Last-Modified", "Wed, 04 Mar 2026 05:06:07 GMT");
                created.Headers.TryAddWithoutValidation("x-ms-request-id", "request-id");
                created.Headers.TryAddWithoutValidation("x-ms-version", "2023-11-03");
                return created;
            }

            if (request.Method == HttpMethod.Get && StoredPayload is not null)
            {
                var downloaded = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(StoredPayload)
                };
                downloaded.Headers.TryAddWithoutValidation("ETag", "\"result-etag\"");
                downloaded.Headers.TryAddWithoutValidation("Last-Modified", "Wed, 04 Mar 2026 05:06:07 GMT");
                downloaded.Headers.TryAddWithoutValidation("x-ms-request-id", "request-id");
                downloaded.Headers.TryAddWithoutValidation("x-ms-version", "2023-11-03");
                return downloaded;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
