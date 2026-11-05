using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.Core;
using Azure.Identity;
using DocAlchemy.ContentUnderstanding.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DocAlchemy.ContentUnderstanding;

/// <summary>
/// Registers the Azure Content Understanding client and its validated configuration.
/// </summary>
public static class ContentUnderstandingServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton Content Understanding client using the configured identity or API-key mode.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddDocAlchemyContentUnderstanding(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<ContentUnderstandingOptions>, ContentUnderstandingOptionsValidator>();
        services.AddOptions<ContentUnderstandingOptions>()
            .Bind(configuration.GetSection(ContentUnderstandingOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton<DefaultAzureCredential>(_ =>
            new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeInteractiveBrowserCredential = true
            }));

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ContentUnderstandingOptions>>().Value;
            var clientOptions = new ContentUnderstandingClientOptions(
                ContentUnderstandingClientOptions.ServiceVersion.V2025_11_01);
            clientOptions.Retry.MaxRetries = 3;
            clientOptions.Retry.Mode = RetryMode.Exponential;
            clientOptions.Diagnostics.IsLoggingContentEnabled = false;

            var endpoint = new Uri(options.Endpoint);

            return options.AuthenticationMode switch
            {
                "ManagedIdentity" => new ContentUnderstandingClient(
                    endpoint,
                    provider.GetRequiredService<DefaultAzureCredential>(),
                    clientOptions),
                "ApiKey" => new ContentUnderstandingClient(
                    endpoint,
                    new AzureKeyCredential(options.ApiKey),
                    clientOptions),
                _ => throw new InvalidOperationException(
                    "Content Understanding authentication configuration is invalid.")
            };
        });

        return services;
    }
}
