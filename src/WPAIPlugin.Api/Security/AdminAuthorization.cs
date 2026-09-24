namespace WPAIPlugin.Api.Security;

/// <summary>
/// Shared constants for the Admin role/policy. AdminOnly requires the
/// ASP.NET Core Identity "Admin" role - never a hard-coded email address.
/// </summary>
public static class AdminAuthorization
{
    public const string AdminRole = "Admin";

    public const string AdminOnlyPolicy = "AdminOnly";
}
