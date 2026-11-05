using System.Text.Json;
using System.Text.Json.Serialization;
using DocAlchemy.Core.Models;

namespace DocAlchemy.Core.Services;

/// <summary>Serializes the stable persisted analysis-result contract.</summary>
public static class AnalysisJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>Serializes a result using camel-case names and explicit nullable properties.</summary>
    /// <param name="result">The immutable result to serialize.</param>
    public static string Serialize(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return JsonSerializer.Serialize(result, SerializerOptions);
    }

    /// <summary>Deserializes a persisted result, rejecting JSON null.</summary>
    /// <param name="json">The JSON document.</param>
    public static AnalysisResult Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<AnalysisResult>(json, SerializerOptions)
            ?? throw new JsonException("The analysis result JSON cannot be null.");
    }
}
