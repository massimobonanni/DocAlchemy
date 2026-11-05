namespace DocAlchemy.ContentUnderstanding.Configuration;

internal sealed class ContentUnderstandingOptions
{
    public const string SectionName = "ContentUnderstanding";
    public const string GaApiVersion = "2025-11-01";

    public string Endpoint { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = GaApiVersion;
    public string AuthenticationMode { get; set; } = "ManagedIdentity";
    public string ApiKey { get; set; } = string.Empty;
}
