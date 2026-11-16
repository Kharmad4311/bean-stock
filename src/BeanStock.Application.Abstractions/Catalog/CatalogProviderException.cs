namespace BeanStock.Application.Abstractions.Catalog;

/// <summary>
/// The catalog provider could not be read: unreachable, error response, timeout or inconsistent data.
/// </summary>
/// <remarks>
/// Implementations of <see cref="ICatalogProvider"/> translate their technical failures into this exception,
/// so the application handles an incident at the provider without knowing its transport.
/// </remarks>
public sealed class CatalogProviderException : Exception
{
    public CatalogProviderException()
    {
    }

    public CatalogProviderException(string message)
        : base(message)
    {
    }

    public CatalogProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
