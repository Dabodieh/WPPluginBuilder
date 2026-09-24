using Microsoft.AspNetCore.Identity;

namespace WPAIPlugin.Api.Security;

/// <summary>
/// One-time Admin role grant, run once at startup. If Admin:BootstrapEmail is
/// configured and no user currently holds the Admin role, and that email
/// matches an existing registered user, that user is granted Admin. Never
/// nominates any other user, never re-grants once any Admin exists, and does
/// nothing if the configured email has not registered yet (grant happens on
/// the next startup after they do).
/// </summary>
public static class AdminBootstrapper
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.BootstrapEmail))
        {
            return;
        }

        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync(AdminAuthorization.AdminRole))
        {
            await roleManager.CreateAsync(new IdentityRole(AdminAuthorization.AdminRole));
        }

        var existingAdmins = await userManager.GetUsersInRoleAsync(AdminAuthorization.AdminRole);
        if (existingAdmins.Count > 0)
        {
            return;
        }

        var candidate = await userManager.FindByEmailAsync(options.BootstrapEmail);
        if (candidate is null)
        {
            return;
        }

        await userManager.AddToRoleAsync(candidate, AdminAuthorization.AdminRole);
    }
}
