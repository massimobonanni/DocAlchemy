using System.Collections.Immutable;

namespace DocAlchemy.Core.Models;

/// <summary>An immutable snapshot of a completed document analysis.</summary>
public sealed class AnalysisResult
{
    /// <summary>The current persisted result schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Creates a completed analysis snapshot.</summary>
    /// <param name="analysisId">The stable identifier reused by persistence retries.</param>
    /// <param name="analyzerId">The analyzer used for this analysis.</param>
    /// <param name="confidenceThreshold">The threshold captured when analysis began.</param>
    /// <param name="analyzedAtUtc">The completion time, expressed in UTC.</param>
    /// <param name="contents">All content items returned by the analysis.</param>
    /// <param name="schemaVersion">The version of the persisted result contract.</param>
    public AnalysisResult(
        Guid analysisId,
        string analyzerId,
        double confidenceThreshold,
        DateTimeOffset analyzedAtUtc,
        ImmutableArray<AnalysisContent> contents,
        int schemaVersion = CurrentSchemaVersion)
    {
        if (analysisId == Guid.Empty)
        {
            throw new ArgumentException("The analysis identifier cannot be empty.", nameof(analysisId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerId);
        global::DocAlchemy.Core.Models.ConfidenceThreshold.ValidateThreshold(confidenceThreshold);
        if (analyzedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The analysis timestamp must be UTC.", nameof(analyzedAtUtc));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);

        AnalysisId = analysisId;
        AnalyzerId = analyzerId;
        ConfidenceThreshold = confidenceThreshold;
        AnalyzedAtUtc = analyzedAtUtc;
        SchemaVersion = schemaVersion;
        Contents = contents.IsDefault ? ImmutableArray<AnalysisContent>.Empty : contents;

        if (Contents.Any(static content => content is null))
        {
            throw new ArgumentException("Contents cannot contain null entries.", nameof(contents));
        }

        if (Contents.Select(static content => content.Index).Distinct().Count() != Contents.Length)
        {
            throw new ArgumentException("Content indexes must be unique.", nameof(contents));
        }
    }

    /// <summary>Gets the persisted result schema version.</summary>
    public int SchemaVersion { get; }

    /// <summary>Gets the stable analysis identifier.</summary>
    public Guid AnalysisId { get; }

    /// <summary>Gets the analyzer identifier used for this analysis.</summary>
    public string AnalyzerId { get; }

    /// <summary>Gets the confidence threshold captured at analysis start.</summary>
    public double ConfidenceThreshold { get; }

    /// <summary>Gets the completion timestamp in UTC.</summary>
    public DateTimeOffset AnalyzedAtUtc { get; }

    /// <summary>Gets all content items, including an empty array for a valid empty result.</summary>
    public ImmutableArray<AnalysisContent> Contents { get; }
}
