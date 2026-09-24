using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WPAIPlugin.Api.Data;

namespace WPAIPlugin.Generator.Tests.Planning;

/// <summary>
/// Real UserManager backed by a UserStore over the same InMemory AppDbContext
/// a test already constructs - never a mock, so EmailConfirmed lookups behave
/// exactly like production. Tests that need a specific user to be found seed
/// it directly via db.Users before calling the controller.
/// </summary>
public static class TestUserManagerFactory
{
    public static UserManager<IdentityUser> Create(AppDbContext db) => new(
        new UserStore<IdentityUser>(db),
        null,
        new PasswordHasher<IdentityUser>(),
        Array.Empty<IUserValidator<IdentityUser>>(),
        Array.Empty<IPasswordValidator<IdentityUser>>(),
        new UpperInvariantLookupNormalizer(),
        new IdentityErrorDescriber(),
        null!,
        NullLogger<UserManager<IdentityUser>>.Instance);
}
