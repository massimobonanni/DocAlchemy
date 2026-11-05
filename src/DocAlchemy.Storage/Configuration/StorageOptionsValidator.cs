using Microsoft.Extensions.Options;

namespace DocAlchemy.Storage.Configuration;

internal sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        List<string> failures = [];

        if (!IsValidEndpoint(options.BlobServiceEndpoint))
        {
            failures.Add("Storage:BlobServiceEndpoint must be an absolute HTTPS URI without user information, query, or fragment.");
        }

        if (!IsValidContainerName(options.ContainerName))
        {
            failures.Add("Storage:ContainerName must be a valid Azure Blob container name.");
        }

        switch (options.AuthenticationMode)
        {
            case "ManagedIdentity" when !string.IsNullOrWhiteSpace(options.AccountName)
                || !string.IsNullOrWhiteSpace(options.AccountKey):
                failures.Add("Storage:AccountName and Storage:AccountKey must be omitted when AuthenticationMode is ManagedIdentity.");
                break;
            case "ManagedIdentity":
                break;
            case "AccountKey":
                ValidateAccountKeyMode(options, failures);
                break;
            default:
                failures.Add("Storage:AuthenticationMode must be ManagedIdentity or AccountKey.");
                break;
        }

        return failures.Count is 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateAccountKeyMode(StorageOptions options, List<string> failures)
    {
        if (!IsValidAccountName(options.AccountName))
        {
            failures.Add("Storage:AccountName must be a valid Azure Storage account name when AuthenticationMode is AccountKey.");
        }

        if (string.IsNullOrWhiteSpace(options.AccountKey)
            || !Convert.TryFromBase64String(options.AccountKey, new byte[512], out _))
        {
            failures.Add("Storage:AccountKey must be a valid Base64 key when AuthenticationMode is AccountKey.");
        }
    }

    private static bool IsValidEndpoint(string endpoint)
    {
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool IsValidContainerName(string name)
    {
        return name.Length is >= 3 and <= 63
            && IsLowercaseAlphaNumeric(name[0])
            && IsLowercaseAlphaNumeric(name[^1])
            && name.All(character => IsLowercaseAlphaNumeric(character) || character is '-')
            && !name.Contains("--", StringComparison.Ordinal);
    }

    private static bool IsValidAccountName(string name)
    {
        return name.Length is >= 3 and <= 24
            && name.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9');
    }

    private static bool IsLowercaseAlphaNumeric(char character)
    {
        return character is >= 'a' and <= 'z' or >= '0' and <= '9';
    }
}
