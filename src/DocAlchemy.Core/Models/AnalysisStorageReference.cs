namespace DocAlchemy.Core.Models;

/// <summary>An opaque reference to a persisted result, not a public URL.</summary>
public sealed class AnalysisStorageReference
{
    /// <summary>Creates an opaque storage reference.</summary>
    /// <param name="value">The storage adapter's nonpublic identifier.</param>
    public AnalysisStorageReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>Gets the opaque storage identifier.</summary>
    public string Value { get; }
}
