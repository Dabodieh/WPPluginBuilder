using WPAIPlugin.Generator.Models;

namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Request body for POST /api/projects/build.
/// </summary>
public sealed class ProjectBuildRequest
{
    public PluginSpec? Spec { get; set; }

    public bool Validated { get; set; }
}
