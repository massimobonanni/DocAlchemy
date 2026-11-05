using DocAlchemy.Core.Models;

namespace DocAlchemy.Core.Interfaces;

/// <summary>Analyzes validated document input and returns a normalized immutable snapshot.</summary>
public interface IDocumentAnalysisService
{
    /// <summary>Analyzes a document with the analyzer and threshold captured in the request.</summary>
    /// <param name="request">The validated document request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<AnalysisResult> AnalyzeAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken = default);
}
