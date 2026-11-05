using System.Collections.Immutable;
using DocAlchemy.Core.Models;

namespace DocAlchemy.Core.Interfaces;

/// <summary>Provides analyzer-management operations without exposing service SDK types.</summary>
public interface IAnalyzerService
{
    /// <summary>Lists all available analyzers.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<ImmutableArray<AnalyzerMetadata>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Retrieves one analyzer, or null when it does not exist.</summary>
    /// <param name="analyzerId">The analyzer identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<AnalyzerMetadata?> GetAsync(string analyzerId, CancellationToken cancellationToken = default);

    /// <summary>Creates an analyzer from the unchanged request JSON; it never replaces an existing analyzer.</summary>
    /// <param name="analyzerId">The separately supplied analyzer identifier.</param>
    /// <param name="createRequestJson">The create-request JSON body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task CreateAsync(
        string analyzerId,
        string createRequestJson,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes an analyzer.</summary>
    /// <param name="analyzerId">The analyzer identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task DeleteAsync(string analyzerId, CancellationToken cancellationToken = default);
}
