using Azure.Core;
using Azure.Core.Pipeline;

namespace DocAlchemy.ContentUnderstanding.Services;

// Analysis submission can have succeeded even when its response was lost.
internal sealed class AnalysisRetryPolicy()
    : RetryPolicy(3, DelayStrategy.CreateExponentialDelayStrategy(TimeSpan.FromSeconds(0.8), TimeSpan.FromMinutes(1)))
{
    protected override void OnSendingRequest(HttpMessage message)
    {
        if (IsAnalysisSubmission(message))
        {
            RedirectPolicy.SetAllowAutoRedirect(message, false);
        }

        base.OnSendingRequest(message);
    }

    protected override ValueTask OnSendingRequestAsync(HttpMessage message)
    {
        if (IsAnalysisSubmission(message))
        {
            RedirectPolicy.SetAllowAutoRedirect(message, false);
        }

        return base.OnSendingRequestAsync(message);
    }

    protected override bool ShouldRetry(HttpMessage message, Exception? exception)
    {
        return !IsAnalysisSubmission(message) && base.ShouldRetry(message, exception);
    }

    protected override ValueTask<bool> ShouldRetryAsync(HttpMessage message, Exception? exception)
    {
        return IsAnalysisSubmission(message)
            ? ValueTask.FromResult(false)
            : base.ShouldRetryAsync(message, exception);
    }

    private static bool IsAnalysisSubmission(HttpMessage message)
    {
        return message.Request.Method == RequestMethod.Post &&
            message.Request.Uri.ToUri().AbsolutePath.EndsWith(":analyzeBinary", StringComparison.OrdinalIgnoreCase);
    }
}
