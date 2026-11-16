using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using BeanStock.Domain.Carts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BeanStock.Api.Authentication;

/// <summary>
/// Turns the <c>X-User-Id</c> header into the current user (<see cref="ClaimTypes.NameIdentifier"/> claim).
/// </summary>
/// <remarks>
/// Endpoints and use cases only see a <see cref="ClaimsPrincipal"/>: switching to a real authentication scheme
/// does not change them. Without a valid header, protected endpoints answer <c>401</c> with a ProblemDetails.
/// </remarks>
internal sealed partial class UserIdAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IProblemDetailsService problemDetailsService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdAuthentication.HeaderName, out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Several header values are joined with commas, which the format refuses.
        var userId = values.ToString();
        if (userId.Length > CartRules.UserIdMaxLength || !UserIdFormat().IsMatch(userId))
        {
            return Task.FromResult(AuthenticateResult.Fail($"Invalid {UserIdAuthentication.HeaderName} header."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            UserIdAuthentication.SchemeName);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), UserIdAuthentication.SchemeName)));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "The current user is not identified.",
                Detail = $"Send the {UserIdAuthentication.HeaderName} header: 1 to {CartRules.UserIdMaxLength} " +
                    "characters among letters, digits and . _ - @",
            },
        });
    }

    [GeneratedRegex("^[A-Za-z0-9._@-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UserIdFormat();
}
