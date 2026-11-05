namespace DocAlchemy.Web.Configuration;

/// <summary>
/// Upload limits bound from the Uploads configuration section.
/// </summary>
public sealed class UploadsOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Uploads";

    /// <summary>
    /// Gets or sets the maximum accepted document size in bytes.
    /// </summary>
    public long MaxDocumentBytes { get; set; } = 20_971_520;
}
