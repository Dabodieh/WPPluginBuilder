using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Storage;
using WPAIPlugin.Api.Validation;
using WPAIPlugin.Generator.Tests.Planning;
using WPAIPlugin.Generator.Tests.Validation;
using WPAIPlugin.Planning;

namespace WPAIPlugin.Generator.Tests.Projects;

/// <summary>
/// Full DI-pipeline test factory for the SaaS project/version/download flow
/// (Milestone 11): isolated EF Core InMemory database, isolated temp-directory
/// artifact store, and a fake planning provider/Docker validator so tests
/// never touch a live database, filesystem outside temp, OpenAI/Anthropic, or
/// real Docker.
/// </summary>
public sealed class ProjectsTestFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();
    public readonly string ArtifactRoot = Path.Combine(Path.GetTempPath(), "wpaiplugin-test-artifacts-" + Guid.NewGuid().ToString("N"));

    public Action<DbContextOptionsBuilder>? ConfigureDatabase { get; set; }

    public FakeDockerPluginValidator FakeValidator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_databaseName);
                    ConfigureDatabase?.Invoke(options);
                });

            services.RemoveAll<DockerPluginValidator>();
            services.AddSingleton<DockerPluginValidator>(FakeValidator);

            services.RemoveAll<IPlanningProvider>();
            services.AddSingleton<IPlanningProvider>(new FakePlanningProvider());
            services.RemoveAll<IPlanningProviderResolver>();
            services.AddSingleton<IPlanningProviderResolver>(sp =>
                new PlanningProviderResolver(sp.GetServices<IPlanningProvider>(), "fake"));

            services.PostConfigure<ArtifactStorageOptions>(options => options.RootPath = ArtifactRoot);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (Directory.Exists(ArtifactRoot))
            {
                Directory.Delete(ArtifactRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup only.
        }
    }
}
