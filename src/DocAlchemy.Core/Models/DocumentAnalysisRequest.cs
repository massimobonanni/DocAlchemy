namespace DocAlchemy.Core.Models;

/// <summary>Describes a document already validated by the application boundary.</summary>
public sealed class DocumentAnalysisRequest
{
    /// <summary>Creates a request for analyzing a validated document stream.</summary>
    /// <param name="analyzerId">The selected analyzer identifier.</param>
    /// <param name="content">A readable stream positioned at the document's first byte.</param>
    /// <param name="contentType">The server-selected canonical content type.</param>
    /// <param name="confidenceThreshold">The threshold to capture for the analysis.</param>
    public DocumentAnalysisRequest(
        string analyzerId,
        Stream content,
        string contentType,
        double confidenceThreshold)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerId);
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
        {
            throw new ArgumentException("The document stream must be readable.", nameof(content));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        global::DocAlchemy.Core.Models.ConfidenceThreshold.ValidateThreshold(confidenceThreshold);

        AnalyzerId = analyzerId;
        Content = content;
        ContentType = contentType;
        ConfidenceThreshold = confidenceThreshold;
    }

    /// <summary>Gets the selected analyzer identifier.</summary>
    public string AnalyzerId { get; }

    /// <summary>Gets the document stream; its caller retains ownership.</summary>
    public Stream Content { get; }

    /// <summary>Gets the server-selected canonical content type.</summary>
    public string ContentType { get; }

    /// <summary>Gets the threshold captured for this analysis request.</summary>
    public double ConfidenceThreshold { get; }
}
