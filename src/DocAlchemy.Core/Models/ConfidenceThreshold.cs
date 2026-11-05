namespace DocAlchemy.Core.Models;

/// <summary>Applies the shared confidence-warning rule.</summary>
public static class ConfidenceThreshold
{
    /// <summary>Returns true only when a present confidence is strictly below the threshold.</summary>
    /// <param name="confidence">The optional numeric confidence.</param>
    /// <param name="threshold">The inclusive-range threshold.</param>
    public static bool IsBelow(double? confidence, double threshold)
    {
        ValidateThreshold(threshold);
        ValidateConfidence(confidence);
        return confidence is double value && value < threshold;
    }

    internal static void ValidateThreshold(double threshold)
    {
        if (!double.IsFinite(threshold) || threshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold),
                "The confidence threshold must be finite and in the inclusive range [0, 1].");
        }
    }

    internal static void ValidateConfidence(double? confidence)
    {
        if (confidence is double value && (!double.IsFinite(value) || value is < 0 or > 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                "Confidence must be finite and in the inclusive range [0, 1].");
        }
    }
}
