using System.Collections.Immutable;

namespace DocAlchemy.Core.Models;

/// <summary>Groups the extracted fields belonging to one service content item.</summary>
public sealed class AnalysisContent
{
    /// <summary>Creates a content item with its stable zero-based index.</summary>
    /// <param name="index">The item's stable position in the analysis result.</param>
    /// <param name="fields">The fields extracted from this item.</param>
    public AnalysisContent(int index, ImmutableArray<ExtractedField> fields)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        Index = index;
        Fields = fields.IsDefault ? ImmutableArray<ExtractedField>.Empty : fields;
        if (Fields.Any(static field => field is null))
        {
            throw new ArgumentException("Fields cannot contain null entries.", nameof(fields));
        }
    }

    /// <summary>Gets the stable zero-based content-item index.</summary>
    public int Index { get; }

    /// <summary>Gets the extracted fields; an empty array is a valid result.</summary>
    public ImmutableArray<ExtractedField> Fields { get; }
}
