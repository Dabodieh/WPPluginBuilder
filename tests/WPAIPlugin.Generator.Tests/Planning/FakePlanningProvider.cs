using WPAIPlugin.Planning;

namespace WPAIPlugin.Generator.Tests.Planning;

/// <summary>
/// Test double for <see cref="IPlanningProvider"/>. Never calls a real AI API.
/// </summary>
internal sealed class FakePlanningProvider : IPlanningProvider
{
    public string Name { get; init; } = "fake";

    public Func<PlanningRequest, CancellationToken, Task<PlanningResult>>? Handler { get; init; }

    public Task<PlanningResult> PlanAsync(PlanningRequest request, CancellationToken cancellationToken = default)
    {
        if (Handler is not null)
        {
            return Handler(request, cancellationToken);
        }

        return Task.FromResult(new PlanningResult
        {
            Name = "Staff Directory",
            Slug = "staff-directory",
            Description = "A simple staff directory plugin.",
            Version = "1.0.0",
            Author = "WPAI Plugin Builder",
            Features = new[] { "shortcode" },
            UnsupportedRequirements = Array.Empty<string>(),
        });
    }
}
