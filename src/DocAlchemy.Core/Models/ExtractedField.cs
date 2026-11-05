using System.Collections.Immutable;
using System.Text.Json;

namespace DocAlchemy.Core.Models;

/// <summary>A typed extracted value and its recursively mapped children.</summary>
public sealed class ExtractedField
{
    /// <summary>Creates an extracted field and detaches its JSON value from its source document.</summary>
    /// <param name="name">The service field name or object-property name.</param>
    /// <param name="path">The stable application field path.</param>
    /// <param name="type">The service-provided discriminator, including unknown future types.</param>
    /// <param name="value">The original typed JSON value.</param>
    /// <param name="confidence">The optional confidence for this value.</param>
    /// <param name="children">Nested object properties or array items.</param>
    public ExtractedField(
        string name,
        string path,
        string type,
        JsonElement value,
        double? confidence,
        ImmutableArray<ExtractedField> children)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        if (value.ValueKind is JsonValueKind.Undefined)
        {
            throw new ArgumentException("The field value must be a defined JSON value.", nameof(value));
        }

        ConfidenceThreshold.ValidateConfidence(confidence);

        Name = name;
        Path = path;
        Type = type;
        Value = value.Clone();
        Confidence = confidence;
        Children = children.IsDefault ? ImmutableArray<ExtractedField>.Empty : children;
        if (Children.Any(static child => child is null))
        {
            throw new ArgumentException("Children cannot contain null entries.", nameof(children));
        }
    }

    /// <summary>Gets the field or property name.</summary>
    public string Name { get; }

    /// <summary>Gets the stable path distinguishing properties and array items.</summary>
    public string Path { get; }

    /// <summary>Gets the service-provided field type.</summary>
    public string Type { get; }

    /// <summary>Gets the original typed JSON value, including explicit JSON null.</summary>
    public JsonElement Value { get; }

    /// <summary>Gets the optional confidence without replacing missing confidence with zero.</summary>
    public double? Confidence { get; }

    /// <summary>Gets recursively mapped object properties or array items.</summary>
    public ImmutableArray<ExtractedField> Children { get; }
}
