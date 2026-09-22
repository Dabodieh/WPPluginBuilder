namespace WPAIPlugin.Api.Storage;

public sealed class ArtifactStorageOptions
{
    public const string SectionName = "Artifacts";

    public string RootPath { get; set; } = "App_Data/artifacts";
}
