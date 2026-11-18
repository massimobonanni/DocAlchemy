namespace DocAlchemy.Web.Components.Shared;

/// <summary>Identifies the presentation and announcement behavior of a shared status message.</summary>
public enum StatusMessageKind
{
    /// <summary>A neutral informational message.</summary>
    Information,

    /// <summary>A non-urgent operation that is still in progress.</summary>
    Loading,

    /// <summary>A condition that requires review.</summary>
    Warning,

    /// <summary>A successful operation.</summary>
    Success,

    /// <summary>An error requiring immediate attention.</summary>
    Error
}
