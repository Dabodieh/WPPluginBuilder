using System.IO.Compression;
using WPAIPlugin.Generator.Models;
using WPAIPlugin.Generator.Validation;
using WPAIPlugin.Templates;

namespace WPAIPlugin.Generator;

/// <summary>
/// Deterministic, template-driven builder that turns a <see cref="PluginSpec"/>
/// into an installable WordPress plugin ZIP.
/// </summary>
public sealed class PluginBuilder
{
    /// <summary>
    /// Validates the spec, generates the plugin files on disk under a temporary
    /// directory, packages them into a ZIP, and cleans up the temporary files.
    /// </summary>
    /// <exception cref="PluginBuildException">Thrown when the spec fails validation.</exception>
    public PluginBuildResult Build(PluginSpec spec)
    {
        var validation = PluginSpecValidator.Validate(spec);
        if (!validation.IsValid)
        {
            throw new PluginBuildException(validation.Errors);
        }

        var buildRoot = Path.Combine(Path.GetTempPath(), "wpaiplugin-build-" + Guid.NewGuid().ToString("N"));
        var pluginDir = Path.Combine(buildRoot, spec.Slug);

        try
        {
            Directory.CreateDirectory(pluginDir);

            GeneratePluginFiles(spec, pluginDir);

            var zipBytes = CreateZip(buildRoot, spec.Slug);

            return new PluginBuildResult
            {
                ZipBytes = zipBytes,
                FileName = $"{spec.Slug}.zip",
            };
        }
        finally
        {
            if (Directory.Exists(buildRoot))
            {
                Directory.Delete(buildRoot, recursive: true);
            }
        }
    }

    private static void GeneratePluginFiles(PluginSpec spec, string pluginDir)
    {
        var featureBlocks = new List<string>();

        if (spec.Features.Contains(PluginFeature.Shortcode, StringComparer.OrdinalIgnoreCase))
        {
            featureBlocks.Add(ShortcodeTemplate.Render(spec.Slug, spec.Name));
        }

        if (spec.Features.Contains(PluginFeature.CustomPostType, StringComparer.OrdinalIgnoreCase))
        {
            var cpt = spec.CustomPostType!;
            featureBlocks.Add(CustomPostTypeTemplate.Render(cpt.Slug, cpt.SingularName, cpt.PluralName, cpt.Public, cpt.HasArchive));
        }

        if (spec.Features.Contains(PluginFeature.SettingsPage, StringComparer.OrdinalIgnoreCase))
        {
            var settingsPage = spec.SettingsPage!;
            var fields = settingsPage.Fields
                .Select(f => (f.Key, f.Label, f.Type, f.DefaultValue))
                .ToList();
            featureBlocks.Add(SettingsPageTemplate.Render(spec.Slug, settingsPage.PageTitle, settingsPage.MenuTitle, fields));
        }

        if (spec.Features.Contains(PluginFeature.CustomFields, StringComparer.OrdinalIgnoreCase))
        {
            var customFields = spec.CustomFields!;
            var fields = customFields.Fields
                .Select(f => (f.Key, f.Label, f.Type))
                .ToList();
            featureBlocks.Add(CustomFieldsTemplate.Render(spec.Slug, customFields.PostType, fields));
        }

        if (spec.Features.Contains(PluginFeature.ScheduledTask, StringComparer.OrdinalIgnoreCase))
        {
            var task = spec.ScheduledTask!;
            featureBlocks.Add(ScheduledTaskTemplate.Render(spec.Slug, task.TaskName, task.Schedule, task.HookName));
        }

        var mainFileContent = MainPluginFileTemplate.Render(
            spec.Name,
            spec.Slug,
            spec.Description,
            spec.Version,
            spec.Author,
            featureBlocks);

        var mainFilePath = Path.Combine(pluginDir, $"{spec.Slug}.php");
        File.WriteAllText(mainFilePath, mainFileContent);
    }

    private static byte[] CreateZip(string buildRoot, string slug)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), "wpaiplugin-zip-" + Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            // Zip the plugin folder itself (not just its contents) so the archive
            // extracts to a single top-level "<slug>/" directory, as WordPress expects.
            ZipFile.CreateFromDirectory(buildRoot, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return File.ReadAllBytes(zipPath);
        }
        finally
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
        }
    }
}
