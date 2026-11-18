namespace DocAlchemy.ContentUnderstanding.Configuration;

internal sealed class DocumentUploadOptions
{
    public const string SectionName = "Uploads";

    public long MaxDocumentBytes { get; set; } = 20_971_520;
}
