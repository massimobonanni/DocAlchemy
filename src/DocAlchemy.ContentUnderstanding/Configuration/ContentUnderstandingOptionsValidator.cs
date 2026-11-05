using Microsoft.Extensions.Options;

namespace DocAlchemy.ContentUnderstanding.Configuration;

internal sealed class ContentUnderstandingOptionsValidator : IValidateOptions<ContentUnderstandingOptions>
{
    public ValidateOptionsResult Validate(string? name, ContentUnderstandingOptions options)
    {
        List<string> failures = [];

        if (!IsValidEndpoint(options.Endpoint))
        {
            failures.Add("ContentUnderstanding:Endpoint must be an absolute HTTPS URI without user information, query, or fragment.");
        }

        if (!string.Equals(options.ApiVersion, ContentUnderstandingOptions.GaApiVersion, StringComparison.Ordinal))
        {
            failures.Add($"ContentUnderstanding:ApiVersion must be {ContentUnderstandingOptions.GaApiVersion}.");
        }

        switch (options.AuthenticationMode)
        {
            case "ManagedIdentity" when !string.IsNullOrWhiteSpace(options.ApiKey):
                failures.Add("ContentUnderstanding:ApiKey must be omitted when AuthenticationMode is ManagedIdentity.");
                break;
            case "ManagedIdentity":
                break;
            case "ApiKey" when string.IsNullOrWhiteSpace(options.ApiKey):
                failures.Add("ContentUnderstanding:ApiKey is required when AuthenticationMode is ApiKey.");
                break;
            case "ApiKey":
                break;
            default:
                failures.Add("ContentUnderstanding:AuthenticationMode must be ManagedIdentity or ApiKey.");
                break;
        }

        return failures.Count is 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
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
}
