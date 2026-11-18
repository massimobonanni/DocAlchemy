using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DocAlchemy.Core.Models;

namespace DocAlchemy.ContentUnderstanding.Services;

internal static class AnalysisResultMapper
{
    public static AnalysisResult Map(BinaryData response, string analyzerId, double threshold, DateTimeOffset completedAt)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            var envelope = document.RootElement;
            if (envelope.ValueKind == JsonValueKind.Object)
            {
                EnsureUniqueProperties(envelope, "Analysis response");
            }

            if (envelope.ValueKind != JsonValueKind.Object ||
                !envelope.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Object ||
                !result.TryGetProperty("contents", out var contents) ||
                contents.ValueKind != JsonValueKind.Array)
            {
                throw Invalid("The analysis result must contain a contents array.");
            }

            EnsureUniqueProperties(result, "Analysis result");
            var mappedContents = ImmutableArray.CreateBuilder<AnalysisContent>();
            foreach (var content in contents.EnumerateArray())
            {
                var index = mappedContents.Count;
                if (content.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid($"Content item {index} must be an object.");
                }

                EnsureUniqueProperties(content, $"Content item {index}");
                if (content.TryGetProperty("kind", out var kind) &&
                    (kind.ValueKind != JsonValueKind.String || kind.GetString() != "document"))
                {
                    throw Invalid($"Content item {index} has an unsupported kind.");
                }

                var fields = ImmutableArray.CreateBuilder<ExtractedField>();
                if (content.TryGetProperty("fields", out var fieldObject))
                {
                    if (fieldObject.ValueKind != JsonValueKind.Object)
                    {
                        throw Invalid($"Content item {index} fields must be an object.");
                    }

                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var field in fieldObject.EnumerateObject())
                    {
                        if (!names.Add(field.Name))
                        {
                            throw Invalid($"Content item {index} contains duplicate field names.");
                        }

                        fields.Add(MapField(field.Name, FieldPath.ForContentField(index, field.Name), field.Value));
                    }
                }

                mappedContents.Add(new AnalysisContent(index, fields.ToImmutable()));
            }

            return new AnalysisResult(Guid.NewGuid(), analyzerId, threshold, completedAt, mappedContents.ToImmutable());
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The analysis result is not valid JSON.", exception);
        }
    }

    private static ExtractedField MapField(string name, string path, JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            EnsureUniqueProperties(node, $"Field at {path}");
        }

        if (node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty("type", out var discriminator) ||
            discriminator.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(discriminator.GetString()))
        {
            throw Invalid($"Field at {path} must have a nonempty type.");
        }

        var type = discriminator.GetString()!;
        double? confidence = null;
        if (node.TryGetProperty("confidence", out var confidenceNode) && confidenceNode.ValueKind != JsonValueKind.Null)
        {
            if (confidenceNode.ValueKind != JsonValueKind.Number ||
                !confidenceNode.TryGetDouble(out var number) ||
                !double.IsFinite(number) || number < 0 || number > 1)
            {
                throw Invalid($"Field at {path} has invalid confidence.");
            }

            confidence = number;
        }

        var propertyName = type switch
        {
            "string" => "valueString",
            "date" => "valueDate",
            "time" => "valueTime",
            "number" => "valueNumber",
            "integer" => "valueInteger",
            "boolean" => "valueBoolean",
            "array" => "valueArray",
            "object" => "valueObject",
            "json" => "valueJson",
            _ => null
        };

        if (propertyName is null)
        {
            return new ExtractedField(name, path, type, node, confidence, []);
        }

        if (!node.TryGetProperty(propertyName, out var value))
        {
            throw Invalid($"Field at {path} is missing {propertyName}.");
        }

        if (value.ValueKind is JsonValueKind.Null)
        {
            return new ExtractedField(name, path, type, value, confidence, []);
        }

        var valid = type switch
        {
            "string" or "date" or "time" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numeric) && double.IsFinite(numeric),
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "array" => value.ValueKind == JsonValueKind.Array,
            "object" => value.ValueKind == JsonValueKind.Object,
            "json" => true,
            _ => false
        };
        if (!valid)
        {
            throw Invalid($"Field at {path} has an invalid value for its type.");
        }

        var children = ImmutableArray.CreateBuilder<ExtractedField>();
        if (type == "array")
        {
            foreach (var item in value.EnumerateArray())
            {
                var index = children.Count;
                children.Add(MapField(index.ToString(CultureInfo.InvariantCulture), FieldPath.AppendArrayItem(path, index), item));
            }

            value = JsonSerializer.SerializeToElement(children.Select(static child => child.Value));
        }
        else if (type == "object")
        {
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (values.ContainsKey(property.Name))
                {
                    throw Invalid($"Field at {path} contains duplicate property names.");
                }

                var child = MapField(property.Name, FieldPath.AppendProperty(path, property.Name), property.Value);
                children.Add(child);
                values.Add(property.Name, child.Value);
            }

            value = JsonSerializer.SerializeToElement(values);
        }

        return new ExtractedField(name, path, type, value, confidence, children.ToImmutable());
    }

    private static void EnsureUniqueProperties(JsonElement element, string location)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw Invalid($"{location} contains duplicate properties.");
            }
        }
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
