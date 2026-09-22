using WPAIPlugin.Generator.Models;
using WPAIPlugin.Generator.Validation;

namespace WPAIPlugin.Planning;

/// <summary>
/// Default <see cref="IPluginPlanner"/>: validates input, delegates to the
/// resolved <see cref="IPlanningProvider"/>, maps its vendor-neutral
/// <see cref="PlanningResult"/> onto a <see cref="PluginSpec"/>, and enforces
/// that the spec passes <see cref="PluginSpecValidator"/> before it is trusted.
/// </summary>
public sealed class PluginPlanner : IPluginPlanner
{
    private readonly IPlanningProviderResolver _resolver;

    public PluginPlanner(IPlanningProviderResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<PluginPlanResult> PlanAsync(
        string description,
        string? provider = null,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDescription(description);

        var planningProvider = _resolver.Resolve(provider);

        PlanningResult result;
        try
        {
            result = await planningProvider.PlanAsync(
                new PlanningRequest { Description = description, Model = model },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new PluginPlanException(PluginPlanFailureReason.Cancelled, "Plugin planning was cancelled.");
        }
        catch (PluginPlanException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never leak raw provider/SDK exceptions (may contain payload/API details) to callers.
            throw new PluginPlanException(
                PluginPlanFailureReason.ProviderFailure,
                "The AI planning provider failed to produce a plugin plan.",
                innerException: ex);
        }

        var spec = new PluginSpec
        {
            Name = result.Name,
            Slug = result.Slug,
            Description = result.Description,
            Version = result.Version,
            Author = result.Author,
            Features = result.Features.ToList(),
            CustomPostType = result.CustomPostType,
            SettingsPage = result.SettingsPage,
            CustomFields = result.CustomFields,
            ScheduledTask = result.ScheduledTask,
        };

        var validation = PluginSpecValidator.Validate(spec);
        if (!validation.IsValid)
        {
            throw new PluginPlanException(
                PluginPlanFailureReason.GeneratedSpecInvalid,
                "The AI provider produced a plugin plan that failed validation.",
                validation.Errors);
        }

        return new PluginPlanResult
        {
            Spec = spec,
            UnsupportedRequirements = result.UnsupportedRequirements,
        };
    }

    private static void ValidateDescription(string description)
    {
        if (description is null)
        {
            throw new PluginPlanException(PluginPlanFailureReason.InvalidInput, "Description is required.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new PluginPlanException(PluginPlanFailureReason.InvalidInput, "Description cannot be blank.");
        }

        if (description.Length > PlanningConstants.MaxDescriptionLength)
        {
            throw new PluginPlanException(
                PluginPlanFailureReason.InvalidInput,
                $"Description must not exceed {PlanningConstants.MaxDescriptionLength} characters.");
        }
    }
}
