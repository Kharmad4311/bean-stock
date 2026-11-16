using System.Security.Claims;

namespace BeanStock.Api.Authentication;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>Identifier of the current user. Only valid on endpoints that require authorization.</summary>
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The endpoint must require an authenticated user.");
}
