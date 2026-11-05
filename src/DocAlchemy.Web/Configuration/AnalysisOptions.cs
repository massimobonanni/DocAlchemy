namespace DocAlchemy.Web.Configuration;

/// <summary>
/// Analysis-operation settings bound from the Analysis configuration section.
/// </summary>
public sealed class AnalysisOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Analysis";

    /// <summary>
    /// Gets or sets the warning threshold for field confidence.
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.80;

    /// <summary>
    /// Gets or sets the maximum duration for a service long-running operation.
    /// </summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets the interval between service operation status polls.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
}
