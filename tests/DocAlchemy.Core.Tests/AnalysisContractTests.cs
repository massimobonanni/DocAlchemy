using System.Collections.Immutable;
using System.Text.Json;
using DocAlchemy.Core.Models;
using DocAlchemy.Core.Services;

namespace DocAlchemy.Core.Tests;

public class AnalysisContractTests
{
    [Theory]
    [InlineData(0.79, 0.8, true)]
    [InlineData(0.8, 0.8, false)]
    [InlineData(0.81, 0.8, false)]
    public void ConfidenceThresholdUsesStrictLessThan(double? confidence, double threshold, bool expected)
    {
        Assert.Equal(expected, ConfidenceThreshold.IsBelow(confidence, threshold));
    }

    [Fact]
    public void ConfidenceThresholdDoesNotWarnWhenConfidenceIsMissing()
    {
        Assert.False(ConfidenceThreshold.IsBelow(null, 0.8));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void ConfidenceThresholdRejectsInvalidThreshold(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfidenceThreshold.IsBelow(0.5, threshold));
    }

    [Fact]
    public void FieldPathEscapesNamesAndDistinguishesPropertiesFromArrayItems()
    {
        var root = FieldPath.ForContentField(2, "a~/b");

        Assert.Equal("/contents/2/fields/a~0~1b", root);
        Assert.Equal($"{root}/properties/0", FieldPath.AppendProperty(root, "0"));
        Assert.Equal($"{root}/items/0", FieldPath.AppendArrayItem(root, 0));
    }

    [Fact]
    public void AnalysisJsonRoundTripsTypedNestedValuesAndMissingConfidence()
    {
        using var document = JsonDocument.Parse(
            """{"object":{"number":42,"boolean":true,"date":"2026-11-05","array":[null,{"text":"value"}]}}""");
        var objectField = new ExtractedField(
            "object",
            FieldPath.ForContentField(0, "object"),
            "object",
            document.RootElement.GetProperty("object"),
            0.93,
            [
                new ExtractedField(
                    "number",
                    FieldPath.AppendProperty(FieldPath.ForContentField(0, "object"), "number"),
                    "number",
                    document.RootElement.GetProperty("object").GetProperty("number"),
                    null,
                    []),
                new ExtractedField(
                    "boolean",
                    FieldPath.AppendProperty(FieldPath.ForContentField(0, "object"), "boolean"),
                    "boolean",
                    document.RootElement.GetProperty("object").GetProperty("boolean"),
                    0.8,
                    []),
                new ExtractedField(
                    "date",
                    FieldPath.AppendProperty(FieldPath.ForContentField(0, "object"), "date"),
                    "date",
                    document.RootElement.GetProperty("object").GetProperty("date"),
                    null,
                    []),
                new ExtractedField(
                    "array",
                    FieldPath.AppendProperty(FieldPath.ForContentField(0, "object"), "array"),
                    "array",
                    document.RootElement.GetProperty("object").GetProperty("array"),
                    null,
                    [
                        new ExtractedField(
                            "0",
                            FieldPath.AppendArrayItem(
                                FieldPath.AppendProperty(FieldPath.ForContentField(0, "object"), "array"),
                                0),
                            "json",
                            document.RootElement.GetProperty("object").GetProperty("array")[0],
                            null,
                            []),
                        new ExtractedField(
                            "1",
                            FieldPath.AppendArrayItem(
                                FieldPath.AppendProperty(FieldPath.ForContentField(0, "object"), "array"),
                                1),
                            "object",
                            document.RootElement.GetProperty("object").GetProperty("array")[1],
                            null,
                            [
                                new ExtractedField(
                                    "text",
                                    FieldPath.AppendProperty(
                                        FieldPath.AppendArrayItem(
                                            FieldPath.AppendProperty(FieldPath.ForContentField(0, "object"), "array"),
                                            1),
                                        "text"),
                                    "string",
                                    document.RootElement.GetProperty("object").GetProperty("array")[1].GetProperty("text"),
                                    null,
                                    [])
                            ])
                    ])
            ]);
        var expected = new AnalysisResult(
            Guid.Parse("dfbb43d0-e3ec-46c0-a462-73b88332e83f"),
            "analyzer",
            0.8,
            new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero),
            [
                new AnalysisContent(0, [objectField]),
                new AnalysisContent(1, [])
            ]);

        var json = AnalysisJson.Serialize(expected);
        var actual = AnalysisJson.Deserialize(json);

        Assert.Contains("\"confidence\":null", json, StringComparison.Ordinal);
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.AnalysisId, actual.AnalysisId);
        Assert.Equal(expected.AnalyzerId, actual.AnalyzerId);
        Assert.Equal(expected.ConfidenceThreshold, actual.ConfidenceThreshold);
        Assert.Equal(expected.AnalyzedAtUtc, actual.AnalyzedAtUtc);
        Assert.Equal([0, 1], actual.Contents.Select(static content => content.Index));

        var actualObject = Assert.Single(actual.Contents[0].Fields);
        Assert.Equal("object", actualObject.Type);
        Assert.Equal(JsonValueKind.Object, actualObject.Value.ValueKind);
        Assert.Equal(4, actualObject.Children.Length);
        Assert.Null(actualObject.Children[0].Confidence);
        Assert.Equal(JsonValueKind.Number, actualObject.Children[0].Value.ValueKind);
        Assert.Equal(JsonValueKind.True, actualObject.Children[1].Value.ValueKind);
        Assert.Equal("2026-11-05", actualObject.Children[2].Value.GetString());
        Assert.Equal(JsonValueKind.Null, actualObject.Children[3].Children[0].Value.ValueKind);
        Assert.Equal("value", actualObject.Children[3].Children[1].Children[0].Value.GetString());
        Assert.Empty(actual.Contents[1].Fields);
    }

    [Fact]
    public void ExtractedFieldClonesJsonValueAndPreservesUnknownType()
    {
        ExtractedField field;
        using (var document = JsonDocument.Parse("""{"future":true}"""))
        {
            field = new ExtractedField(
                "field",
                FieldPath.ForContentField(0, "field"),
                "future-type",
                document.RootElement,
                null,
                ImmutableArray<ExtractedField>.Empty);
        }

        Assert.Equal("future-type", field.Type);
        Assert.Equal(JsonValueKind.Object, field.Value.ValueKind);
        Assert.True(field.Value.GetProperty("future").GetBoolean());
    }

    [Fact]
    public void AnalysisResultRejectsNonUtcTimestampAndDuplicateContentIndexes()
    {
        var id = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new AnalysisResult(
            id,
            "analyzer",
            0.8,
            new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.FromHours(1)),
            []));
        Assert.Throws<ArgumentException>(() => new AnalysisResult(
            id,
            "analyzer",
            0.8,
            DateTimeOffset.UtcNow,
            [new AnalysisContent(0, []), new AnalysisContent(0, [])]));
    }
}
