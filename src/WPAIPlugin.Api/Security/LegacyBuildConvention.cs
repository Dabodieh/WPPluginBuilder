using Microsoft.AspNetCore.Mvc.ApplicationModels;
using WPAIPlugin.Api.Controllers;

namespace WPAIPlugin.Api.Security;

// Remove legacy actions from endpoint discovery, including Swagger, outside
// Development. URL casing/trailing slashes cannot bypass routing availability.
public sealed class LegacyBuildConvention(bool development) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        if (development) return;
        foreach (var controller in application.Controllers.Where(c => c.ControllerType == typeof(PluginsController)))
            foreach (var action in controller.Actions.Where(a => a.ActionName is "Build" or "BuildValidated").ToList())
                controller.Actions.Remove(action);
    }
}
