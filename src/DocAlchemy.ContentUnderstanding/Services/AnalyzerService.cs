using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.Core;
using DocAlchemy.ContentUnderstanding.Configuration;
using DocAlchemy.Core.Interfaces;
using DocAlchemy.Core.Models;
using Microsoft.Extensions.Options;

namespace DocAlchemy.ContentUnderstanding.Services;

internal sealed partial class AnalyzerService(
    ContentUnderstandingClient client,
    IOptions<AnalyzerOperationOptions> operationOptions) : IAnalyzerService
{
    private static readonly ImmutableArray<string> AnalyzeOnly =
    [
        "analyze"
    ];

    private static readonly ImmutableArray<string> AnalyzeAndDelete =
    [
        "analyze",
        "delete"
    ];

    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ImmutableArray<AnalyzerMetadata>> ListAsync(CancellationToken cancellationToken = default)
    {
        var analyzers = ImmutableArray.CreateBuilder<AnalyzerMetadata>();

        await foreach (var analyzer in client.GetAnalyzersAsync(cancellationToken))
        {
            analyzers.Add(ParseMetadata(
                JsonSerializer.SerializeToElement(analyzer, MetadataJsonOptions),
                ReadStatus(analyzer)));
        }

        return analyzers.ToImmutable();
    }

    public async Task<AnalyzerMetadata?> GetAsync(
        string analyzerId,
        CancellationToken cancellationToken = default)
    {
        ValidateAnalyzerId(analyzerId);

        try
        {
            using var response = await client.GetAnalyzerAsync(
                analyzerId,
                new RequestContext
                {
                    CancellationToken = cancellationToken
                });
            using var document = JsonDocument.Parse(
                response.ContentStream ??
                throw new InvalidDataException("The analyzer response did not contain a JSON body."));
            return ParseMetadata(document.RootElement);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task CreateAsync(
        string analyzerId,
        string createRequestJson,
        CancellationToken cancellationToken = default)
    {
        ValidateAnalyzerId(analyzerId);
        ArgumentNullException.ThrowIfNull(createRequestJson);
        ValidateCreateRequest(createRequestJson);

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationCancellation.CancelAfter(operationOptions.Value.OperationTimeout);

        try
        {
            var operation = await client.CreateAnalyzerAsync(
                WaitUntil.Started,
                analyzerId,
                RequestContent.Create(BinaryData.FromString(createRequestJson)),
                allowReplace: false,
                new RequestContext
                {
                    CancellationToken = operationCancellation.Token
                });
            await operation.WaitForCompletionAsync(
                operationOptions.Value.PollInterval,
                operationCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Analyzer creation did not complete within the configured timeout.");
        }
    }

    public async Task DeleteAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        ValidateAnalyzerId(analyzerId);
        if (IsBuiltInId(analyzerId))
        {
            throw new InvalidOperationException("Built-in analyzers cannot be deleted.");
        }

        await client.DeleteAnalyzerAsync(analyzerId, cancellationToken);
    }

    private static AnalyzerMetadata ParseMetadata(JsonElement analyzer, string? statusOverride = null)
    {
        if (analyzer.ValueKind is not JsonValueKind.Object)
        {
            throw new InvalidDataException("The analyzer response must be a JSON object.");
        }

        var id = ReadString(analyzer, "analyzerId");
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidDataException("The analyzer response is missing its analyzerId.");
        }

        var isBuiltIn = IsBuiltInId(id);
        var modality = ReadModality(analyzer, id);

        return new AnalyzerMetadata(
            id,
            ReadString(analyzer, "description"),
            modality,
            statusOverride ?? ReadStatus(analyzer),
            isBuiltIn,
            isBuiltIn,
            isBuiltIn ? AnalyzeOnly : AnalyzeAndDelete,
            HasConfidenceWarning(analyzer));
    }

    private static string? ReadModality(JsonElement analyzer, string analyzerId)
    {
        var baseAnalyzerId = ReadString(analyzer, "baseAnalyzerId");
        var sourceId = baseAnalyzerId ?? analyzerId;

        if (sourceId.StartsWith("prebuilt-document", StringComparison.OrdinalIgnoreCase) ||
            sourceId is "prebuilt-layout" or "prebuilt-read" or "prebuilt-invoice" or
                "prebuilt-receipt" or "prebuilt-idDocument" or "prebuilt-healthInsuranceCard" or
                "prebuilt-bankStatement" or "prebuilt-check" or "prebuilt-documentFields" or
                "prebuilt-documentFieldSchema")
        {
            return "document";
        }

        if (sourceId.StartsWith("prebuilt-image", StringComparison.OrdinalIgnoreCase))
        {
            return "image";
        }

        if (sourceId.StartsWith("prebuilt-audio", StringComparison.OrdinalIgnoreCase))
        {
            return "audio";
        }

        if (sourceId.StartsWith("prebuilt-video", StringComparison.OrdinalIgnoreCase))
        {
            return "video";
        }

        return null;
    }

    private static bool HasConfidenceWarning(JsonElement analyzer)
    {
        var analyzerLevelEnabled = analyzer.TryGetProperty("config", out var config) &&
            config.ValueKind is JsonValueKind.Object &&
            config.TryGetProperty("estimateFieldSourceAndConfidence", out var globalSetting) &&
            globalSetting.ValueKind is JsonValueKind.True;

        if (!analyzer.TryGetProperty("fieldSchema", out var schema) ||
            schema.ValueKind is not JsonValueKind.Object ||
            !schema.TryGetProperty("fields", out var fields))
        {
            return !analyzerLevelEnabled;
        }

        var foundField = false;
        return FieldsHaveConfidenceWarning(fields, analyzerLevelEnabled, ref foundField) ||
            !foundField && !analyzerLevelEnabled;
    }

    private static bool FieldsHaveConfidenceWarning(
        JsonElement fields,
        bool analyzerLevelEnabled,
        ref bool foundField)
    {
        if (fields.ValueKind is JsonValueKind.Object)
        {
            foreach (var field in fields.EnumerateObject())
            {
                if (FieldHasConfidenceWarning(field.Value, analyzerLevelEnabled, ref foundField))
                {
                    return true;
                }
            }

            return false;
        }

        if (fields.ValueKind is JsonValueKind.Array)
        {
            foreach (var field in fields.EnumerateArray())
            {
                if (FieldHasConfidenceWarning(field, analyzerLevelEnabled, ref foundField))
                {
                    return true;
                }
            }
        }
        else
        {
            return true;
        }

        return false;
    }

    private static bool FieldHasConfidenceWarning(
        JsonElement field,
        bool analyzerLevelEnabled,
        ref bool foundField)
    {
        if (field.ValueKind is not JsonValueKind.Object)
        {
            return true;
        }

        foundField = true;
        var enabled = field.TryGetProperty("estimateSourceAndConfidence", out var overrideSetting)
            ? overrideSetting.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => analyzerLevelEnabled,
                _ => false
            }
            : analyzerLevelEnabled;

        if (!enabled)
        {
            return true;
        }

        foreach (var nestedName in new[] { "fields", "properties" })
        {
            if (field.TryGetProperty(nestedName, out var nestedFields) &&
                FieldsHaveConfidenceWarning(nestedFields, analyzerLevelEnabled, ref foundField))
            {
                return true;
            }
        }

        if (field.TryGetProperty("itemDefinition", out var itemDefinition) &&
            itemDefinition.ValueKind is not JsonValueKind.Null &&
            FieldHasConfidenceWarning(itemDefinition, analyzerLevelEnabled, ref foundField))
        {
            return true;
        }

        if (field.TryGetProperty("items", out var items) &&
            items.ValueKind is JsonValueKind.Object &&
            FieldHasConfidenceWarning(items, analyzerLevelEnabled, ref foundField))
        {
            return true;
        }

        if (items.ValueKind is JsonValueKind.Array &&
            FieldsHaveConfidenceWarning(items, analyzerLevelEnabled, ref foundField))
        {
            return true;
        }

        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadStatus(JsonElement analyzer)
    {
        if (!analyzer.TryGetProperty("status", out var status))
        {
            return null;
        }

        return status.ValueKind is JsonValueKind.Object
            ? ReadString(status, "status")
            : status.ValueKind is JsonValueKind.String
                ? status.GetString()
                : null;
    }

    private static string? ReadStatus(ContentAnalyzer analyzer)
    {
        var status = analyzer.Status;
        if (status == ContentAnalyzerStatus.Creating)
        {
            return "creating";
        }

        if (status == ContentAnalyzerStatus.Ready)
        {
            return "ready";
        }

        if (status == ContentAnalyzerStatus.Deleting)
        {
            return "deleting";
        }

        return status == ContentAnalyzerStatus.Failed ? "failed" : null;
    }

    private static bool IsBuiltInId(string analyzerId) =>
        analyzerId.StartsWith("prebuilt-", StringComparison.OrdinalIgnoreCase);

    private static void ValidateAnalyzerId(string analyzerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerId);
        if (!AnalyzerIdPattern().IsMatch(analyzerId))
        {
            throw new ArgumentException(
                "Analyzer IDs must contain 1 to 64 ASCII letters, digits, periods, underscores, or hyphens.",
                nameof(analyzerId));
        }
    }

    private static void ValidateCreateRequest(string createRequestJson)
    {
        using var document = JsonDocument.Parse(createRequestJson);
        var root = document.RootElement;
        if (root.ValueKind is not JsonValueKind.Object)
        {
            throw new ArgumentException("The analyzer create request must be a JSON object.", nameof(createRequestJson));
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "analyzerId", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Supply the analyzer ID separately; analyzerId is not a create-request body property.",
                    nameof(createRequestJson));
            }
        }

        if (!root.TryGetProperty("baseAnalyzerId", out var baseAnalyzerId) ||
            baseAnalyzerId.ValueKind is not JsonValueKind.String ||
            string.IsNullOrWhiteSpace(baseAnalyzerId.GetString()))
        {
            throw new ArgumentException(
                "The analyzer create request must include a non-empty baseAnalyzerId.",
                nameof(createRequestJson));
        }
    }

    [GeneratedRegex("^[a-zA-Z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex AnalyzerIdPattern();
}
