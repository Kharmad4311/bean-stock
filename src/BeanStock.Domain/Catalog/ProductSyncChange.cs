namespace BeanStock.Domain.Catalog;

/// <summary>
/// Effect of the provider data on an existing product during a synchronization.
/// </summary>
public enum ProductSyncChange
{
    /// <summary>Same values, product already active: nothing is written.</summary>
    Unchanged,

    /// <summary>At least one value changed.</summary>
    Updated,

    /// <summary>The product was inactive and is offered again by the provider (values may also have changed).</summary>
    Reactivated,
}
