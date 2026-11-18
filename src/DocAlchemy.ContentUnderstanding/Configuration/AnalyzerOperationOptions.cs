namespace DocAlchemy.ContentUnderstanding.Configuration;

internal sealed class AnalyzerOperationOptions
{
    public const string SectionName = "Analysis";

    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
}
