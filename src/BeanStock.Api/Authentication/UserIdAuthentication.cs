namespace BeanStock.Api.Authentication;

/// <summary>
/// Simplified identification of the current user, for the scope of the exercise: the caller states who it is
/// in the <see cref="HeaderName"/> header. Real authentication (JWT…) would replace this scheme only.
/// </summary>
internal static class UserIdAuthentication
{
    public const string SchemeName = "UserId";
    public const string HeaderName = "X-User-Id";
}
