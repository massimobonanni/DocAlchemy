namespace DocAlchemy.ContentUnderstanding.Services;

/// <summary>Indicates that analysis submission failed and its acceptance may be unknown.</summary>
public sealed class AnalysisSubmissionException : Exception
{
    /// <summary>Creates a submission error while preserving the original service failure.</summary>
    /// <param name="innerException">The underlying SDK failure.</param>
    public AnalysisSubmissionException(Exception innerException)
        : base("Document analysis submission failed; its outcome may be unknown. Do not retry automatically.", innerException)
    {
    }
}
