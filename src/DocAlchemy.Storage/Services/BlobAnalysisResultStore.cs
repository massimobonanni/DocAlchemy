using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DocAlchemy.Core.Interfaces;
using DocAlchemy.Core.Models;
using DocAlchemy.Core.Services;

namespace DocAlchemy.Storage.Services;

/// <summary>Persists immutable analysis snapshots as private JSON blobs.</summary>
public sealed class BlobAnalysisResultStore : IAnalysisResultStore
{
    private const string ContentType = "application/json";
    private readonly BlobContainerClient containerClient;

    /// <summary>Creates a result store for an already provisioned results container.</summary>
    /// <param name="blobServiceClient">The configured Blob service client.</param>
    /// <param name="containerName">The private results container name.</param>
    public BlobAnalysisResultStore(BlobServiceClient blobServiceClient, string containerName)
    {
        ArgumentNullException.ThrowIfNull(blobServiceClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        containerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    /// <inheritdoc />
    public async Task<AnalysisStorageReference> SaveAsync(
        AnalysisResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var payload = Encoding.UTF8.GetBytes(AnalysisJson.Serialize(result));
        var blobName = $"{result.AnalysisId:D}.json";
        var blobClient = containerClient.GetBlobClient(blobName);
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = ContentType },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        };

        try
        {
            await blobClient.UploadAsync(
                BinaryData.FromBytes(payload),
                uploadOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException exception) when (exception.Status is 412)
        {
            var existing = await blobClient.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            var existingPayload = existing.Value.Content.ToMemory();
            if (!CryptographicOperations.FixedTimeEquals(payload, existingPayload.Span))
            {
                throw new InvalidOperationException(
                    "A different analysis result already exists with this analysis identifier.",
                    exception);
            }
        }

        return new AnalysisStorageReference(blobName);
    }
}
