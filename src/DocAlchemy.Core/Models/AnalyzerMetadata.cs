using System.Collections.Immutable;

namespace DocAlchemy.Core.Models;

/// <summary>Describes an analyzer using service-independent metadata.</summary>
public sealed class AnalyzerMetadata
{
    /// <summary>Creates immutable analyzer metadata.</summary>
    /// <param name="id">The analyzer identifier.</param>
    /// <param name="description">An optional analyzer description.</param>
    /// <param name="modality">An optional modality such as document or image.</param>
    /// <param name="status">An optional service-reported status.</param>
    /// <param name="isBuiltIn">Whether the analyzer is a built-in analyzer.</param>
    /// <param name="isReadOnly">Whether the analyzer cannot be modified or deleted.</param>
    /// <param name="supportedOperations">Operations supported by the analyzer.</param>
    public AnalyzerMetadata(
        string id,
        string? description = null,
        string? modality = null,
        string? status = null,
        bool isBuiltIn = false,
        bool isReadOnly = false,
        ImmutableArray<string> supportedOperations = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
        Description = description;
        Modality = modality;
        Status = status;
        IsBuiltIn = isBuiltIn;
        IsReadOnly = isReadOnly;
        SupportedOperations = supportedOperations.IsDefault
            ? ImmutableArray<string>.Empty
            : supportedOperations;
    }

    /// <summary>Gets the analyzer identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the optional analyzer description.</summary>
    public string? Description { get; }

    /// <summary>Gets the optional analyzer modality.</summary>
    public string? Modality { get; }

    /// <summary>Gets the optional service-reported status.</summary>
    public string? Status { get; }

    /// <summary>Gets whether this is a built-in analyzer.</summary>
    public bool IsBuiltIn { get; }

    /// <summary>Gets whether this analyzer is read-only.</summary>
    public bool IsReadOnly { get; }

    /// <summary>Gets the operations supported by this analyzer.</summary>
    public ImmutableArray<string> SupportedOperations { get; }
}
