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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
