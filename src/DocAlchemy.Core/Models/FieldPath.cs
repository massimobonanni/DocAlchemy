using System.Globalization;

namespace DocAlchemy.Core.Models;

/// <summary>Builds stable paths for fields, object properties, and array items.</summary>
public static class FieldPath
{
    /// <summary>Creates a path for a top-level field in a content item.</summary>
    /// <param name="contentIndex">The zero-based content-item index.</param>
    /// <param name="fieldName">The field name.</param>
    public static string ForContentField(int contentIndex, string fieldName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contentIndex);
        ArgumentNullException.ThrowIfNull(fieldName);
        return $"/contents/{contentIndex.ToString(CultureInfo.InvariantCulture)}/fields/{Escape(fieldName)}";
    }

    /// <summary>Appends an object property segment, escaping its name as an RFC 6901 token.</summary>
    /// <param name="parentPath">The path of the parent field.</param>
    /// <param name="propertyName">The object-property name.</param>
    public static string AppendProperty(string parentPath, string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPath);
        ArgumentNullException.ThrowIfNull(propertyName);
        return $"{parentPath}/properties/{Escape(propertyName)}";
    }

    /// <summary>Appends an array-item segment using its canonical zero-based index.</summary>
    /// <param name="parentPath">The path of the parent field.</param>
    /// <param name="itemIndex">The zero-based array-item index.</param>
    public static string AppendArrayItem(string parentPath, int itemIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPath);
        ArgumentOutOfRangeException.ThrowIfNegative(itemIndex);
        return $"{parentPath}/items/{itemIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string Escape(string segment)
    {
        return segment.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);
    }
}
