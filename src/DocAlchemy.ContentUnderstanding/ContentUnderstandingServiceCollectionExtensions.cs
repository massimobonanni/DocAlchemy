using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.Core;
using Azure.Identity;
using DocAlchemy.ContentUnderstanding.Configuration;
using DocAlchemy.ContentUnderstanding.Services;
using DocAlchemy.Core.Interfaces;
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
        services.AddOptions<AnalyzerOperationOptions>()
            .Bind(configuration.GetSection(AnalyzerOperationOptions.SectionName))
            .Validate(
                options =>
                    options.OperationTimeout > TimeSpan.Zero &&
                    options.OperationTimeout <= TimeSpan.FromHours(2) &&
                    options.PollInterval > TimeSpan.Zero &&
                    options.PollInterval <= options.OperationTimeout,
                "Analysis:OperationTimeout and Analysis:PollInterval must be positive, with polling no longer than the operation timeout.")
            .ValidateOnStart();
        services.AddOptions<DocumentUploadOptions>()
            .Bind(configuration.GetSection(DocumentUploadOptions.SectionName))
            .Validate(options => options.MaxDocumentBytes > 0 && options.MaxDocumentBytes <= 200_000_000,
                "Uploads:MaxDocumentBytes must be positive and no greater than 200000000.")
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
            clientOptions.RetryPolicy = new AnalysisRetryPolicy();
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
        services.AddSingleton<IAnalyzerService, AnalyzerService>();
        services.AddSingleton<IDocumentAnalysisService, DocumentAnalysisService>();

        return services;
    }
}
