namespace WPAIPlugin.Planning;

public static class PlanningConstants
{
    /// <summary>
    /// Maximum allowed length, in characters, of a user-supplied plugin description.
    /// </summary>
    public const int MaxDescriptionLength = 2000;

    /// <summary>
    /// Fixed, client-facing message for a request the planner has determined is
    /// not a WordPress-plugin request (off-topic, or an attempt to redirect the
    /// model away from planning a plugin). Always returned as-is - the model's
    /// own output is never surfaced to the client for a rejected request.
    /// </summary>
    public const string OutOfScopeMessage = "ModuleMint can only process requests related to creating or modifying WordPress plugins.";
}
