namespace WPAIPlugin.Generator;

/// <summary>
/// Successful result of a plugin build: the ZIP bytes and the suggested file name.
/// </summary>
public sealed class PluginBuildResult
{
    public required byte[] ZipBytes { get; init; }

    public required string FileName { get; init; }
}
