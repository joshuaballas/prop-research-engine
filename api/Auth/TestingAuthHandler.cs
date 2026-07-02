using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SportsBetting.Api.Auth;

public sealed class TestingAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestingAuth";
    public const string SubjectHeader = "X-Test-Auth-Subject";
    public const string EmailHeader = "X-Test-Auth-Email";
    public const string NameHeader = "X-Test-Auth-Name";
    public const string SessionHeader = "X-Test-Auth-Session";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubjectHeader, out var subjects))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var subject = subjects.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(subject))
        {
            return Task.FromResult(AuthenticateResult.Fail("Testing auth subject is missing."));
        }

        var claims = new List<Claim>
        {
            new("sub", subject),
            new(ClaimTypes.NameIdentifier, subject)
        };

        AddOptionalClaim(claims, "email", EmailHeader);
        AddOptionalClaim(claims, ClaimTypes.Email, EmailHeader);
        AddOptionalClaim(claims, "name", NameHeader);
        AddOptionalClaim(claims, ClaimTypes.Name, NameHeader);
        AddOptionalClaim(claims, "sid", SessionHeader);

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private void AddOptionalClaim(ICollection<Claim> claims, string type, string header)
    {
        if (Request.Headers.TryGetValue(header, out var values))
        {
            var value = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
            {
                claims.Add(new Claim(type, value));
            }
        }
    }
}
