namespace SportsBetting.Api.Auth;

public sealed class AppAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string Provider { get; set; } = "Clerk";
}

public sealed class ClerkOptions
{
    public const string SectionName = "Clerk";

    public string Issuer { get; set; } = string.Empty;
    public string JwksUrl { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string[] AuthorizedParties { get; set; } = [];
}

public sealed class AuthRuntimeOptions
{
    public const string SectionName = "Auth";

    public bool AllowFirstUserOwnerBootstrapInDevelopment { get; set; } = false;
    public string[] BootstrapOwnerEmails { get; set; } = [];
    public string[] BootstrapOwnerExternalSubjectIds { get; set; } = [];
    public bool AllowAnonymousDebugInDevelopment { get; set; } = false;
    public bool RequireAuthenticationInTesting { get; set; } = false;
}

public static class AuthPolicies
{
    public const string AuthenticatedMember = "AuthenticatedMember";
    public const string OwnerOrAdmin = "OwnerOrAdmin";
    public const string DebugAccess = "DebugAccess";
}
