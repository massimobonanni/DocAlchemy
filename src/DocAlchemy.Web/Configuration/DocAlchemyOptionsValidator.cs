using Microsoft.Extensions.Options;

namespace DocAlchemy.Web.Configuration;

internal sealed class AnalysisOptionsValidator : IValidateOptions<AnalysisOptions>
{
    public ValidateOptionsResult Validate(string? name, AnalysisOptions options)
    {
        List<string> failures = [];

        if (!double.IsFinite(options.ConfidenceThreshold)
            || options.ConfidenceThreshold is < 0 or > 1)
        {
            failures.Add("Analysis:ConfidenceThreshold must be a finite number in the inclusive range [0, 1].");
        }

        if (options.OperationTimeout <= TimeSpan.Zero
            || options.OperationTimeout > TimeSpan.FromHours(2))
        {
            failures.Add("Analysis:OperationTimeout must be greater than zero and no more than 02:00:00.");
        }

        if (options.PollInterval <= TimeSpan.Zero
            || options.PollInterval > options.OperationTimeout)
        {
            failures.Add("Analysis:PollInterval must be greater than zero and no greater than OperationTimeout.");
        }

        return failures.Count is 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

internal sealed class UploadsOptionsValidator : IValidateOptions<UploadsOptions>
{
    private const long MaximumServiceUploadBytes = 200_000_000;

    public ValidateOptionsResult Validate(string? name, UploadsOptions options)
    {
        return options.MaxDocumentBytes is > 0 and <= MaximumServiceUploadBytes
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "Uploads:MaxDocumentBytes must be greater than zero and no more than 200000000.");
    }
}
