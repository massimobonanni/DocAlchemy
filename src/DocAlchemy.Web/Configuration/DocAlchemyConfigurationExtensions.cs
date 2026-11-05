using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocAlchemy.Web.Configuration;

/// <summary>
/// Registers validated DocAlchemy web settings.
/// </summary>
public static class DocAlchemyConfigurationExtensions
{
    /// <summary>
    /// Binds and validates analysis and upload settings when the host starts.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddDocAlchemyConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<AnalysisOptions>, AnalysisOptionsValidator>();
        services.AddSingleton<IValidateOptions<UploadsOptions>, UploadsOptionsValidator>();
        services.AddOptions<AnalysisOptions>()
            .Bind(configuration.GetSection(AnalysisOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<UploadsOptions>()
            .Bind(configuration.GetSection(UploadsOptions.SectionName))
            .ValidateOnStart();

        return services;
    }
}
