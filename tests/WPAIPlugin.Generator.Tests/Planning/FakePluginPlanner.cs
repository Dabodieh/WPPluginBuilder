using WPAIPlugin.Generator.Models;
using WPAIPlugin.Planning;

namespace WPAIPlugin.Generator.Tests.Planning;

/// <summary>
/// Test double for <see cref="IPluginPlanner"/> used by controller tests.
/// </summary>
internal sealed class FakePluginPlanner : IPluginPlanner
{
    public Func<string, string?, string?, CancellationToken, Task<PluginPlanResult>>? Handler { get; init; }

    public Task<PluginPlanResult> PlanAsync(
        string description,
        string? provider = null,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        if (Handler is not null)
        {
            return Handler(description, provider, model, cancellationToken);
        }

        return Task.FromResult(new PluginPlanResult
        {
            Spec = new PluginSpec
            {
                Name = "Staff Directory",
                Slug = "staff-directory",
                Description = "A simple staff directory plugin.",
                Version = "1.0.0",
                Author = "WPAI Plugin Builder",
                Features = new List<string> { "shortcode" },
            },
            UnsupportedRequirements = Array.Empty<string>(),
        });
    }
}
