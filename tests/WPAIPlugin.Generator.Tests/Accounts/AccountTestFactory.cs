using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WPAIPlugin.Api.Data;

namespace WPAIPlugin.Generator.Tests.Accounts;

/// <summary>
/// Swaps the real Npgsql-backed AppDbContext for an isolated EF Core
/// InMemory database, so account/Identity tests never require a live
/// PostgreSQL instance. Each factory instance gets its own database name so
/// tests don't share state.
/// </summary>
public sealed class AccountTestFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public FakeTransactionalEmailSender FakeEmailSender { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<WPAIPlugin.Api.Security.SecurityOptions>(o =>
            {
                o.AccountRequestsPerFiveMinutes = 1000;
                o.PlanningPerMinute = 1000; o.BuildsPerMinute = 1000; o.ValidatedBuildsPerMinute = 1000;
                o.PasswordRecoveryPerFiveMinutes = 1000;
                o.EmailVerificationResendPerFiveMinutes = 1000;
            });
            services.PostConfigure<WPAIPlugin.Api.Configuration.AppOptions>(o => o.PublicBaseUrl = "https://modulemint.test");
            services.RemoveAll<WPAIPlugin.Api.Email.ITransactionalEmailSender>();
            services.AddSingleton<WPAIPlugin.Api.Email.ITransactionalEmailSender>(FakeEmailSender);
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }
}
