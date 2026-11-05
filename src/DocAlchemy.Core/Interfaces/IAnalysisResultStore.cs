using DocAlchemy.Core.Models;

namespace DocAlchemy.Core.Interfaces;

/// <summary>Persists immutable normalized analysis results.</summary>
public interface IAnalysisResultStore
{
    /// <summary>Persists a result and returns an opaque, nonpublic storage reference.</summary>
    /// <param name="result">The completed result to persist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<AnalysisStorageReference> SaveAsync(
        AnalysisResult result,
        CancellationToken cancellationToken = default);
}
