using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.Core;
using DocAlchemy.ContentUnderstanding.Configuration;
using DocAlchemy.Core.Interfaces;
using DocAlchemy.Core.Models;
using Microsoft.Extensions.Options;
using AnalysisResult = DocAlchemy.Core.Models.AnalysisResult;

namespace DocAlchemy.ContentUnderstanding.Services;

internal sealed class DocumentAnalysisService(
    ContentUnderstandingClient client,
    IOptions<AnalyzerOperationOptions> operationOptions,
    IOptions<DocumentUploadOptions> uploadOptions) : IDocumentAnalysisService
{
    public async Task<AnalysisResult> AnalyzeAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationCancellation.CancelAfter(operationOptions.Value.OperationTimeout);

        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = await request.Content.ReadAsync(chunk, operationCancellation.Token)) != 0)
            {
                total += read;
                if (total > uploadOptions.Value.MaxDocumentBytes)
                {
                    throw new InvalidDataException("The document exceeds the configured upload limit.");
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), operationCancellation.Token);
            }

            if (total == 0)
            {
                throw new InvalidDataException("The document is empty.");
            }

            buffer.Position = 0;
            using var body = RequestContent.Create(buffer);
            Operation<BinaryData> operation;
            try
            {
                operation = await client.AnalyzeBinaryAsync(
                    WaitUntil.Started,
                    request.AnalyzerId,
                    request.ContentType,
                    body,
                    context: new RequestContext { CancellationToken = operationCancellation.Token });
            }
            catch (RequestFailedException exception)
            {
                throw new AnalysisSubmissionException(exception);
            }

            var response = await operation.WaitForCompletionAsync(
                operationOptions.Value.PollInterval,
                operationCancellation.Token);
            var completedAt = DateTimeOffset.UtcNow;
            return AnalysisResultMapper.Map(response.Value, request.AnalyzerId, request.ConfidenceThreshold, completedAt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Document analysis did not complete within the configured timeout; its submission outcome may be unknown.");
        }
    }
}
