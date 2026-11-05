using Azure.Core;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using DocAlchemy.Storage.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DocAlchemy.Storage;

/// <summary>
/// Registers the Azure Blob Storage client and its validated configuration.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton Blob service client using the configured identity or account-key mode.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddDocAlchemyStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton<DefaultAzureCredential>(_ =>
            new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeInteractiveBrowserCredential = true
            }));

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
            var clientOptions = new BlobClientOptions();
            clientOptions.Retry.MaxRetries = 3;
            clientOptions.Retry.Mode = RetryMode.Exponential;
            clientOptions.Diagnostics.IsLoggingContentEnabled = false;

            var endpoint = new Uri(options.BlobServiceEndpoint);

            return options.AuthenticationMode switch
            {
                "ManagedIdentity" => new BlobServiceClient(
                    endpoint,
                    provider.GetRequiredService<DefaultAzureCredential>(),
                    clientOptions),
                "AccountKey" => new BlobServiceClient(
                    endpoint,
                    new StorageSharedKeyCredential(options.AccountName, options.AccountKey),
                    clientOptions),
                _ => throw new InvalidOperationException("Storage authentication configuration is invalid.")
            };
        });

        return services;
    }
}
