namespace DocAlchemy.Storage.Configuration;

internal sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string BlobServiceEndpoint { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string AuthenticationMode { get; set; } = "ManagedIdentity";
    public string AccountName { get; set; } = string.Empty;
    public string AccountKey { get; set; } = string.Empty;
}
