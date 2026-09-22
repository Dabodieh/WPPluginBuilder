using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WPAIPlugin.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

// SaaS persistence + accounts (Milestone 10).
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        // Defaults are ASP.NET Core Identity's own password/lockout rules.
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<WPAIPlugin.Generator.PluginBuilder>();

// AI planning layer (Milestone 2): provider-agnostic. OpenAI is the default
// runtime provider; Anthropic remains supported as an explicit fallback.
builder.Services.Configure<WPAIPlugin.Planning.PlanningOptions>(
    builder.Configuration.GetSection(WPAIPlugin.Planning.PlanningOptions.SectionName));
builder.Services.AddHttpClient<WPAIPlugin.Planning.Providers.Anthropic.AnthropicPlanningProvider>();
builder.Services.AddHttpClient<WPAIPlugin.Planning.Providers.OpenAI.OpenAIPlanningProvider>();
builder.Services.AddSingleton<WPAIPlugin.Planning.IPlanningProvider>(sp =>
    sp.GetRequiredService<WPAIPlugin.Planning.Providers.Anthropic.AnthropicPlanningProvider>());
builder.Services.AddSingleton<WPAIPlugin.Planning.IPlanningProvider>(sp =>
    sp.GetRequiredService<WPAIPlugin.Planning.Providers.OpenAI.OpenAIPlanningProvider>());
builder.Services.AddSingleton<WPAIPlugin.Planning.IPlanningProviderResolver>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WPAIPlugin.Planning.PlanningOptions>>().Value;
    var providers = sp.GetServices<WPAIPlugin.Planning.IPlanningProvider>();
    return new WPAIPlugin.Planning.PlanningProviderResolver(providers, options.DefaultProvider);
});
builder.Services.AddSingleton<WPAIPlugin.Planning.IPluginPlanner, WPAIPlugin.Planning.PluginPlanner>();

// Optional Docker-based build validation (Milestone 9): reuses the existing
// PluginBuilder ZIP and docker/docker-compose.validate.yml. Never involves AI.
builder.Services.Configure<WPAIPlugin.Api.Validation.ValidationOptions>(
    builder.Configuration.GetSection(WPAIPlugin.Api.Validation.ValidationOptions.SectionName));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WPAIPlugin.Api.Validation.ValidationOptions>>().Value;
    // AppContext.BaseDirectory (the built DLL's own folder) resolves the same
    // way whether the API is launched via `dotnet run` or `dotnet <dll>`
    // directly, unlike ContentRootPath which differs between the two.
    var composeFilePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docker", "docker-compose.validate.yml");
    var logger = sp.GetRequiredService<ILogger<WPAIPlugin.Api.Validation.DockerPluginValidator>>();
    return new WPAIPlugin.Api.Validation.DockerPluginValidator(
        Path.GetFullPath(composeFilePath),
        TimeSpan.FromSeconds(options.TimeoutSeconds),
        logger);
});

// Authenticated SaaS plugin projects + saved builds (Milestone 11). Local
// filesystem artifact storage, outside wwwroot, never served by UseStaticFiles.
builder.Services.Configure<WPAIPlugin.Api.Storage.ArtifactStorageOptions>(
    builder.Configuration.GetSection(WPAIPlugin.Api.Storage.ArtifactStorageOptions.SectionName));
builder.Services.AddSingleton<WPAIPlugin.Api.Storage.PluginArtifactStore>();

// Test credit system (Milestone 12): server-authoritative balance/ledger.
// No Stripe/payments yet.
builder.Services.AddOptions<WPAIPlugin.Api.Credits.CreditOptions>()
    .Bind(builder.Configuration.GetSection(WPAIPlugin.Api.Credits.CreditOptions.SectionName))
    .Validate(o => o.SignupGrant >= 0 && o.StandardBuildCost > 0 && o.ValidatedBuildCost > 0,
        "Credit grant must be nonnegative and build costs must be positive.")
    .ValidateOnStart();
builder.Services.AddScoped<WPAIPlugin.Api.Credits.CreditService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the implicit Program class to WebApplicationFactory<Program> in integration tests.
public partial class Program { }
