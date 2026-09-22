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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }
}
