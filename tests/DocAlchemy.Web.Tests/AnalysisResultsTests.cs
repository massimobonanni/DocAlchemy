using System.Collections.Immutable;
using System.Text.Json;
using Bunit;
using DocAlchemy.Core.Models;
using DocAlchemy.Web.Components.Shared;

namespace DocAlchemy.Web.Tests;

public sealed class AnalysisResultsTests : BunitContext
{
    [Fact]
    public void ConfidenceWarningUsesStrictBoundaryAndShowsMissingValuesExplicitly()
    {
        var below = Render<ConfidenceIndicator>(parameters => parameters
            .Add(component => component.Confidence, 0.79)
            .Add(component => component.Threshold, 0.80));
        var equal = Render<ConfidenceIndicator>(parameters => parameters
            .Add(component => component.Confidence, 0.80)
            .Add(component => component.Threshold, 0.80));
        var above = Render<ConfidenceIndicator>(parameters => parameters
            .Add(component => component.Confidence, 0.81)
            .Add(component => component.Threshold, 0.80));
        var missing = Render<ConfidenceIndicator>(parameters => parameters
            .Add(component => component.Confidence, (double?)null)
            .Add(component => component.Threshold, 0.80));

        Assert.Contains("confidence--warning", below.Markup, StringComparison.Ordinal);
        Assert.Contains("Low confidence: 79%", below.Markup, StringComparison.Ordinal);
        Assert.Contains("aria-hidden=\"true\"", below.Markup, StringComparison.Ordinal);
        Assert.Contains("Confidence: 80%", equal.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("confidence--warning", equal.Markup, StringComparison.Ordinal);
        Assert.Contains("Confidence: 81%", above.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("confidence--warning", above.Markup, StringComparison.Ordinal);
        Assert.Contains("Not available", missing.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("confidence--warning", missing.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersMultipleContentItemsRecursivelyAndEncodesExtractedMarkup()
    {
        var scriptValue = ParseJson("\"<script>alert(1)</script>\"");
        var displayField = new ExtractedField(
            "display",
            FieldPath.AppendProperty(
                FieldPath.ForContentField(0, "profile"),
                "display"),
            "string",
            scriptValue,
            0.79,
            ImmutableArray<ExtractedField>.Empty);
        var arrayPath = FieldPath.AppendProperty(
            FieldPath.ForContentField(0, "profile"),
            "scores");
        var arrayField = new ExtractedField(
            "scores",
            arrayPath,
            "array",
            ParseJson("[0.79,null]"),
            null,
            [
                new ExtractedField(
                    "0",
                    FieldPath.AppendArrayItem(arrayPath, 0),
                    "number",
                    ParseJson("0.79"),
                    0.81,
                    ImmutableArray<ExtractedField>.Empty),
                new ExtractedField(
                    "1",
                    FieldPath.AppendArrayItem(arrayPath, 1),
                    "null",
                    ParseJson("null"),
                    null,
                    ImmutableArray<ExtractedField>.Empty)
            ]);
        var profileField = new ExtractedField(
            "profile",
            FieldPath.ForContentField(0, "profile"),
            "object",
            ParseJson("""{"display":"<script>alert(1)</script>","scores":[0.79,null]}"""),
            0.80,
            [displayField, arrayField]);
        var result = CreateResult(
            new AnalysisContent(0, [profileField]),
            new AnalysisContent(1,
            [
                new ExtractedField(
                    "identifier",
                    FieldPath.ForContentField(1, "identifier"),
                    "future-custom-type",
                    ParseJson("42"),
                    0.81,
                    ImmutableArray<ExtractedField>.Empty)
            ]));

        var component = Render<AnalysisResults>(parameters => parameters
            .Add(view => view.Result, result));

        Assert.Equal(2, component.FindAll(".result-content").Count);
        Assert.Contains("Content item 1", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Content item 2", component.Markup, StringComparison.Ordinal);
        Assert.Contains("/contents/0/fields/profile/properties/scores/items/0", component.Markup, StringComparison.Ordinal);
        Assert.Contains("/contents/0/fields/profile/properties/scores/items/1", component.Markup, StringComparison.Ordinal);
        Assert.Contains("future-custom-type", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Low confidence: 79%", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Confidence: 81%", component.Markup, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", component.Markup, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("script"));
    }

    [Fact]
    public void ShowsEmptyResultAndContentWithoutFields()
    {
        var noContent = Render<AnalysisResults>(parameters => parameters
            .Add(view => view.Result, CreateResult()));
        var noFields = Render<AnalysisResults>(parameters => parameters
            .Add(view => view.Result, CreateResult(new AnalysisContent(0, ImmutableArray<ExtractedField>.Empty))));

        Assert.Contains("The analysis completed without content items.", noContent.Markup, StringComparison.Ordinal);
        Assert.Contains("No fields were extracted from this content item.", noFields.Markup, StringComparison.Ordinal);
    }

    private static AnalysisResult CreateResult(params AnalysisContent[] contents)
    {
        return new AnalysisResult(
            Guid.NewGuid(),
            "test-analyzer",
            0.80,
            DateTimeOffset.UtcNow,
            [.. contents]);
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
