using Azure.AI.ContentUnderstanding;
using Azure.Storage.Blobs;
using DocAlchemy.ContentUnderstanding;
using DocAlchemy.Storage;
using DocAlchemy.Web.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocAlchemy.Web.Tests;

public class AzureClientCompositionTests
{
    [Theory]
    [InlineData("ManagedIdentity", "ManagedIdentity")]
    [InlineData("ManagedIdentity", "AccountKey")]
    [InlineData("ApiKey", "ManagedIdentity")]
    [InlineData("ApiKey", "AccountKey")]
    public void CreatesClientsForIndependentAuthenticationModes(
        string contentUnderstandingMode,
        string storageMode)
    {
        using var provider = CreateProvider(contentUnderstandingMode, storageMode);

        Assert.IsType<ContentUnderstandingClient>(provider.GetRequiredService<ContentUnderstandingClient>());
        Assert.IsType<BlobServiceClient>(provider.GetRequiredService<BlobServiceClient>());
    }

    [Fact]
    public void UsesValidatedAnalysisAndUploadDefaults()
    {
        using var provider = CreateProvider("ManagedIdentity", "ManagedIdentity");

        var analysis = provider.GetRequiredService<IOptions<AnalysisOptions>>().Value;
        var uploads = provider.GetRequiredService<IOptions<UploadsOptions>>().Value;

        Assert.Equal(0.80, analysis.ConfidenceThreshold);
        Assert.Equal(TimeSpan.FromMinutes(10), analysis.OperationTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), analysis.PollInterval);
        Assert.Equal(20_971_520, uploads.MaxDocumentBytes);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("-0.01")]
    [InlineData("1.01")]
    public void RejectsNonfiniteOrOutOfRangeConfidenceThreshold(string threshold)
    {
        using var provider = CreateProvider(
            "ManagedIdentity",
            "ManagedIdentity",
            new Dictionary<string, string?>
            {
                ["Analysis:ConfidenceThreshold"] = threshold
            });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AnalysisOptions>>().Value);

        Assert.Contains("Analysis:ConfidenceThreshold", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("200000001")]
    public void RejectsInvalidUploadLimits(string maxDocumentBytes)
    {
        using var provider = CreateProvider(
            "ManagedIdentity",
            "ManagedIdentity",
            new Dictionary<string, string?>
            {
                ["Uploads:MaxDocumentBytes"] = maxDocumentBytes
            });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<UploadsOptions>>().Value);

        Assert.Contains("Uploads:MaxDocumentBytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidEndpointsWithoutEchoingConfigurationValues()
    {
        const string invalidEndpoint = "http://endpoint-with-private-info.invalid";
        using var provider = CreateProvider(
            "ManagedIdentity",
            "ManagedIdentity",
            new Dictionary<string, string?>
            {
                ["ContentUnderstanding:Endpoint"] = invalidEndpoint
            });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<ContentUnderstandingClient>());

        Assert.Contains("ContentUnderstanding:Endpoint", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidEndpoint, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ContentUnderstanding:Endpoint")]
    [InlineData("Storage:BlobServiceEndpoint")]
    public void RequiresBothServiceEndpoints(string endpointSetting)
    {
        using var provider = CreateProvider(
            "ManagedIdentity",
            "ManagedIdentity",
            new Dictionary<string, string?>
            {
                [endpointSetting] = string.Empty
            });

        var exception = endpointSetting.StartsWith("ContentUnderstanding:", StringComparison.Ordinal)
            ? Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<ContentUnderstandingClient>())
            : Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<BlobServiceClient>());

        Assert.Contains(endpointSetting, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidStorageEndpointAndContainer()
    {
        using var provider = CreateProvider(
            "ManagedIdentity",
            "ManagedIdentity",
            new Dictionary<string, string?>
            {
                ["Storage:BlobServiceEndpoint"] = "http://example.blob.core.windows.net",
                ["Storage:ContainerName"] = "Invalid--Container"
            });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<BlobServiceClient>());

        Assert.Contains("Storage:BlobServiceEndpoint", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Storage:ContainerName", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2025-11-01-preview")]
    [InlineData("2026-06-01-preview")]
    public void RejectsNonGaContentUnderstandingApiVersions(string apiVersion)
    {
        using var provider = CreateProvider(
            "ManagedIdentity",
            "ManagedIdentity",
            new Dictionary<string, string?>
            {
                ["ContentUnderstanding:ApiVersion"] = apiVersion
            });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<ContentUnderstandingClient>());

        Assert.Contains("ContentUnderstanding:ApiVersion", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(apiVersion, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidOperationTimeoutAndPollingBounds()
    {
        using var provider = CreateProvider(
            "ManagedIdentity",
            "ManagedIdentity",
            new Dictionary<string, string?>
            {
                ["Analysis:OperationTimeout"] = "02:00:01",
                ["Analysis:PollInterval"] = "02:00:02"
            });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AnalysisOptions>>().Value);

        Assert.Contains("Analysis:OperationTimeout", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Analysis:PollInterval", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresCredentialsForSelectedKeyModesWithoutEchoingSecrets()
    {
        const string suppliedButUnusedKey = "synthetic-test-key-not-for-identity-mode";
        using var provider = CreateProvider(
            "ManagedIdentity",
            "AccountKey",
            new Dictionary<string, string?>
            {
                ["ContentUnderstanding:ApiKey"] = suppliedButUnusedKey,
                ["Storage:AccountKey"] = string.Empty
            });

        var contentException = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<ContentUnderstandingClient>());
        var storageException = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<BlobServiceClient>());

        Assert.DoesNotContain(suppliedButUnusedKey, contentException.Message, StringComparison.Ordinal);
        Assert.Contains("Storage:AccountKey", storageException.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider CreateProvider(
        string contentUnderstandingMode,
        string storageMode,
        Dictionary<string, string?>? overrides = null)
    {
        Dictionary<string, string?> settings = new()
        {
            ["ContentUnderstanding:Endpoint"] = "https://example.services.ai.azure.com",
            ["ContentUnderstanding:AuthenticationMode"] = contentUnderstandingMode,
            ["ContentUnderstanding:ApiKey"] = contentUnderstandingMode is "ApiKey" ? "synthetic-content-key" : string.Empty,
            ["Storage:BlobServiceEndpoint"] = "https://examplestorage.blob.core.windows.net",
            ["Storage:ContainerName"] = "analysis-results",
            ["Storage:AuthenticationMode"] = storageMode,
            ["Storage:AccountName"] = storageMode is "AccountKey" ? "examplestorage" : string.Empty,
            ["Storage:AccountKey"] = storageMode is "AccountKey" ? "c2VjcmV0" : string.Empty
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDocAlchemyConfiguration(configuration);
        services.AddDocAlchemyContentUnderstanding(configuration);
        services.AddDocAlchemyStorage(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
