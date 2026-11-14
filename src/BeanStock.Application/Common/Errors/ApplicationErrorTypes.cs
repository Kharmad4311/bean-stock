namespace BeanStock.Application.Common.Errors;

/// <summary>
/// Custom <c>ErrorOr</c> error types, for expected failures that the built-in types do not describe.
/// </summary>
/// <remarks>Values start at 100 to stay clear of the built-in <c>ErrorType</c> values.</remarks>
public static class ApplicationErrorTypes
{
    /// <summary>
    /// An external dependency (the catalog provider) failed or answered unusably. Translated to
    /// <c>502 Bad Gateway</c> by the API.
    /// </summary>
    public const int Upstream = 100;
}
