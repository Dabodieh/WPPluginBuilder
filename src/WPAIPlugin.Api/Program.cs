var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<WPAIPlugin.Generator.PluginBuilder>();

// AI planning layer (Milestone 2): provider-agnostic, Anthropic is the first concrete provider.
builder.Services.Configure<WPAIPlugin.Planning.PlanningOptions>(
    builder.Configuration.GetSection(WPAIPlugin.Planning.PlanningOptions.SectionName));
builder.Services.AddHttpClient<WPAIPlugin.Planning.Providers.Anthropic.AnthropicPlanningProvider>();
builder.Services.AddSingleton<WPAIPlugin.Planning.IPlanningProvider>(sp =>
    sp.GetRequiredService<WPAIPlugin.Planning.Providers.Anthropic.AnthropicPlanningProvider>());
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

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the implicit Program class to WebApplicationFactory<Program> in integration tests.
public partial class Program { }
