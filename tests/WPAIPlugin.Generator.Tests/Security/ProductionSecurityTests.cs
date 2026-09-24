using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WPAIPlugin.Api.Security;
using WPAIPlugin.Generator.Tests.Planning;
using WPAIPlugin.Generator.Tests.Projects;
using WPAIPlugin.Planning;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Security;

public class ProductionSecurityTests
{
    private const string Secret = "sk-private-regression-marker";
    private static readonly object Spec = new { name = "Security Test", slug = "security-test",
        description = "Security check", version = "1.0.0", author = "Test", features = new[] { "shortcode" } };
    private static WebApplicationFactory<Program> Production(ProjectsTestFactory factory, Action<IServiceCollection>? configure = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=test",
                ["Planning:DefaultProvider"] = "openai", ["Planning:OpenAI:ApiKey"] = Secret,
                // Required alongside the above in Production since the
                // account-recovery milestone (App/Support/Email/Resend) -
                // none of these are secret-shaped test values.
                ["App:PublicBaseUrl"] = "https://modulemint.test",
                ["Support:Email"] = "support@modulemint.test",
                ["Email:FromAddress"] = "no-reply@modulemint.test",
                ["Email:FromName"] = "ModuleMint",
                ["Resend:ApiKey"] = "re_test_marker",
            }));
            if (configure is not null) builder.ConfigureServices(configure);
        });
    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static Task<HttpResponseMessage> Register(HttpClient client) => client.PostJsonWithCsrfAsync(
        "/api/account/register", new { email = $"security-{Guid.NewGuid()}@example.com", password = "Str0ng!Passw0rd" });

    [Theory]
    [InlineData("/api/plugins/build")]
    [InlineData("/api/plugins/build-validated")]
    [InlineData("/API/PLUGINS/BUILD/")]
    public async Task LegacyActionsAreAbsentEvenWhenAuthenticated(string path)
    {
        using var factory = new ProjectsTestFactory(); using var production = Production(factory); using var client = Client(production);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(path, Spec)).StatusCode);
        (await Register(client)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostJsonWithCsrfAsync(path, Spec)).StatusCode);
    }

    [Theory]
    [InlineData("/api/plugins/plan", true)]
    [InlineData("/api/projects/build", true)]
    [InlineData("/api/projects", false)]
    [InlineData("/api/credits", false)]
    [InlineData("/api/projects/11111111-1111-1111-1111-111111111111/versions/22222222-2222-2222-2222-222222222222/download", false)]
    public async Task ProductionRequiresAuthentication(string path, bool post)
    {
        using var factory = new ProjectsTestFactory(); using var production = Production(factory); using var client = Client(production);
        var response = post ? await client.PostAsJsonAsync(path, new { }) : await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SecureCookieCsrfAndCreditBuildWorkTogether()
    {
        using var factory = new ProjectsTestFactory(); using var production = Production(factory); using var client = Client(production);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/account/register", new { email = "csrf@example.com", password = "Str0ng!Passw0rd" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/account/login", new { email = "csrf@example.com", password = "Str0ng!Passw0rd" })).StatusCode);
        var registered = await Register(client); registered.EnsureSuccessStatusCode();
        var cookie = registered.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Identity.Application="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Secret, cookie);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/projects/build", new { spec = Spec })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/account/logout", null)).StatusCode);
        var build = await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec, creditCost = 0 });
        build.EnsureSuccessStatusCode();
        var body = await build.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("creditsCharged").GetInt32());
        Assert.Equal(99, (await client.GetFromJsonAsync<JsonElement>("/api/credits")).GetProperty("balance").GetInt32());
        var download = await client.GetAsync(body.GetProperty("downloadUrl").GetString());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        var text = await build.Content.ReadAsStringAsync();
        foreach (var forbidden in new[] { "artifactKey", "userId", Secret, "App_Data" }) Assert.DoesNotContain(forbidden, text);
        foreach (var path in new[] { "/App_Data/artifacts/plugin.zip", "/artifacts/plugin.zip", "/../App_Data/artifacts/plugin.zip" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/credits")]
    [InlineData("PATCH", "/api/credits")]
    [InlineData("DELETE", "/api/credits/transactions")]
    [InlineData("POST", "/api/credits/refund")]
    public async Task CreditMutationRoutesDoNotExist(string method, string path)
    {
        using var factory = new ProjectsTestFactory(); using var production = Production(factory); using var client = Client(production);
        (await Register(client)).EnsureSuccessStatusCode();
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }

    [Theory]
    [InlineData("planning")]
    [InlineData("build")]
    [InlineData("validated")]
    [InlineData("account")]
    public async Task BuiltInLimitsRejectNextRequestWithoutCharging(string kind)
    {
        using var factory = new ProjectsTestFactory();
        using var production = Production(factory, services => services.PostConfigure<SecurityOptions>(o =>
        {
            o.AccountRequestsPerFiveMinutes = kind == "account" ? 1 : 20;
            o.PlanningPerMinute = 1; o.BuildsPerMinute = kind == "validated" ? 10 : 1; o.ValidatedBuildsPerMinute = 1;
        }));
        using var client = Client(production); (await Register(client)).EnsureSuccessStatusCode();
        var path = kind == "planning" ? "/api/plugins/plan" : kind == "account" ? "/api/account/login" : "/api/projects/build";
        object payload = kind == "planning" ? new { description = "A shortcode plugin" }
            : kind == "account" ? new { email = "invalid@example.com", password = "wrong" }
            : new { spec = Spec, validated = kind == "validated" };
        if (kind != "account") (await client.PostJsonWithCsrfAsync(path, payload)).EnsureSuccessStatusCode();
        var rejected = await client.PostJsonWithCsrfAsync(path, payload);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        var balance = (await client.GetFromJsonAsync<JsonElement>("/api/credits")).GetProperty("balance").GetInt32();
        Assert.Equal(kind == "build" ? 99 : kind == "validated" ? 98 : 100, balance);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/site.css")).StatusCode);
    }

    [Fact]
    public async Task HeadersScriptsAndProviderFailuresDoNotExposeSecrets()
    {
        using var factory = new ProjectsTestFactory();
        using var production = Production(factory, services =>
        {
            services.RemoveAll<IPluginPlanner>();
            services.AddSingleton<IPluginPlanner>(new FakePluginPlanner
            {
                Handler = (_, _, _, _) => throw new PluginPlanException(PluginPlanFailureReason.ProviderFailure, Secret),
            });
        });
        using var client = Client(production);
        var page = await client.GetAsync("/dashboard.html");
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("script-src 'self';", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("camera=()", page.Headers.GetValues("Permissions-Policy").Single());
        Assert.DoesNotContain("<script>", await page.Content.ReadAsStringAsync());
        (await Register(client)).EnsureSuccessStatusCode();
        var failure = await client.PostJsonWithCsrfAsync("/api/plugins/plan", new { description = "A shortcode" });
        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
        Assert.DoesNotContain(Secret, await failure.Content.ReadAsStringAsync());
        foreach (var file in Directory.GetFiles(Path.Combine(production.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath), "*", SearchOption.AllDirectories))
        {
            var text = await File.ReadAllTextAsync(file);
            foreach (var forbidden in new[] { Secret, "/api/plugins/build", "localhost:", "OPENAI_API_KEY" }) Assert.DoesNotContain(forbidden, text);
        }
    }

    [Theory]
    [InlineData("ConnectionStrings:DefaultConnection")]
    [InlineData("Planning:OpenAI:ApiKey")]
    [InlineData("Artifacts:RootPath")]
    [InlineData("App:PublicBaseUrl")]
    [InlineData("Support:Email")]
    [InlineData("Email:FromAddress")]
    [InlineData("Email:FromName")]
    [InlineData("Resend:ApiKey")]
    public void ProductionMissingCriticalConfigurationFailsWithoutSecrets(string key)
    {
        using var factory = new ProjectsTestFactory(); using var production = Production(factory);
        using var missing = production.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { [key] = "" })));
        var error = Assert.Throws<InvalidOperationException>(() => Client(missing));
        Assert.DoesNotContain(Secret, error.Message);
        Assert.Contains("Production requires", error.Message);
    }

    [Fact]
    public void ArtifactRootUnderWwwrootIsRejected()
    {
        using var factory = new ProjectsTestFactory();
        using var production = Production(factory, s => s.PostConfigure<WPAIPlugin.Api.Storage.ArtifactStorageOptions>(o => o.RootPath = "wwwroot/artifacts"));
        var error = Assert.Throws<InvalidOperationException>(() => Client(production));
        Assert.Contains("outside the web root", error.Message);
    }

    [Fact]
    public async Task ProductionUnexpectedExceptionsAreGeneric()
    {
        using var factory = new ProjectsTestFactory(); using var production = Production(factory, services =>
        {
            services.RemoveAll<IPluginPlanner>();
            services.AddSingleton<IPluginPlanner>(new FakePluginPlanner { Handler = (_, _, _, _) => throw new Exception(Secret) });
        });
        using var client = Client(production); (await Register(client)).EnsureSuccessStatusCode();
        var response = await client.PostJsonWithCsrfAsync("/api/plugins/plan", new { description = "A shortcode" });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("The request could not be completed. Please try again.",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task HealthEndpointsAreAnonymousAndExposeNoInternalDetails()
    {
        using var factory = new ProjectsTestFactory(); using var production = Production(factory); using var client = Client(production);

        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var readyBody = await ready.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", readyBody);
        foreach (var forbidden in new[] { Secret, "Host=", "Database", "Npgsql", "Exception" }) Assert.DoesNotContain(forbidden, readyBody);
    }
}
