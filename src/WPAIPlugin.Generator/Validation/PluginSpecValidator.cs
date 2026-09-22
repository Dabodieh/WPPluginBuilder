using System.Text.RegularExpressions;
using WPAIPlugin.Generator.Models;

namespace WPAIPlugin.Generator.Validation;

/// <summary>
/// Validates a <see cref="PluginSpec"/>, including rejection of unsafe slugs
/// and filesystem path/directory-traversal attempts.
/// </summary>
public static partial class PluginSpecValidator
{
    // Lowercase letters, digits, hyphens only. Must start/end with a letter or digit.
    // No dots, slashes, backslashes, underscores-as-traversal, or other path-meaningful chars.
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"^\d+\.\d+(\.\d+)?$")]
    private static partial Regex VersionPattern();

    // Lowercase letters, digits, underscores only. WordPress option/field keys
    // conventionally use snake_case rather than the hyphenated slug format.
    [GeneratedRegex(@"^[a-z][a-z0-9_]*$")]
    private static partial Regex FieldKeyPattern();

    private const int MaxSlugLength = 100;
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 1000;
    private const int MaxAuthorLength = 200;
    private const int MaxFieldKeyLength = 100;
    private const int MaxFieldDefaultValueLength = 1000;
    private const int MaxSettingsFields = 50;

    public static ValidationResult Validate(PluginSpec? spec)
    {
        var result = ValidationResult.Success();

        if (spec is null)
        {
            result.AddError("Plugin spec is required.");
            return result;
        }

        ValidateName(spec.Name, result);
        ValidateSlug(spec.Slug, result);
        ValidateDescription(spec.Description, result);
        ValidateVersion(spec.Version, result);
        ValidateAuthor(spec.Author, result);
        ValidateFeatures(spec.Features, result);

        if (spec.Features?.Contains(PluginFeature.CustomPostType, StringComparer.OrdinalIgnoreCase) == true)
        {
            ValidateCustomPostType(spec.CustomPostType, result);
        }

        if (spec.Features?.Contains(PluginFeature.SettingsPage, StringComparer.OrdinalIgnoreCase) == true)
        {
            ValidateSettingsPage(spec.SettingsPage, result);
        }

        if (spec.Features?.Contains(PluginFeature.CustomFields, StringComparer.OrdinalIgnoreCase) == true)
        {
            ValidateCustomFields(spec.CustomFields, spec.CustomPostType, result);
        }

        if (spec.Features?.Contains(PluginFeature.ScheduledTask, StringComparer.OrdinalIgnoreCase) == true)
        {
            ValidateScheduledTask(spec.ScheduledTask, result);
        }

        return result;
    }

    private static void ValidateName(string name, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            result.AddError("Name is required.");
        }
        else if (name.Length > MaxNameLength)
        {
            result.AddError($"Name must not exceed {MaxNameLength} characters.");
        }
    }

    private static void ValidateSlug(string slug, ValidationResult result) => ValidateSlugField(slug, "Slug", result);

    private static void ValidateSlugField(string slug, string fieldName, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            result.AddError($"{fieldName} is required.");
            return;
        }

        // Explicit directory traversal / path injection checks (defense in depth,
        // even though the regex below would already reject these characters).
        if (slug.Contains("..") ||
            slug.Contains('/') ||
            slug.Contains('\\') ||
            slug.Contains('\0') ||
            Path.IsPathRooted(slug))
        {
            result.AddError($"{fieldName} contains unsafe path characters.");
            return;
        }

        if (slug.Length > MaxSlugLength)
        {
            result.AddError($"{fieldName} must not exceed {MaxSlugLength} characters.");
            return;
        }

        if (!SlugPattern().IsMatch(slug))
        {
            result.AddError($"{fieldName} must contain only lowercase letters, digits, and hyphens, and cannot start or end with a hyphen.");
        }
    }

    private static void ValidateDescription(string description, ValidationResult result)
    {
        if (description is not null && description.Length > MaxDescriptionLength)
        {
            result.AddError($"Description must not exceed {MaxDescriptionLength} characters.");
        }
    }

    private static void ValidateVersion(string version, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            result.AddError("Version is required.");
            return;
        }

        if (!VersionPattern().IsMatch(version))
        {
            result.AddError("Version must be in the form X.Y or X.Y.Z (e.g. 1.0.0).");
        }
    }

    private static void ValidateAuthor(string author, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            result.AddError("Author is required.");
        }
        else if (author.Length > MaxAuthorLength)
        {
            result.AddError($"Author must not exceed {MaxAuthorLength} characters.");
        }
    }

    private static void ValidateCustomPostType(CustomPostTypeSpec? cpt, ValidationResult result)
    {
        if (cpt is null)
        {
            result.AddError("Custom post type details are required when the 'custom-post-type' feature is requested.");
            return;
        }

        if (string.IsNullOrWhiteSpace(cpt.SingularName))
        {
            result.AddError("Custom post type singular name is required.");
        }
        else if (cpt.SingularName.Length > MaxNameLength)
        {
            result.AddError($"Custom post type singular name must not exceed {MaxNameLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(cpt.PluralName))
        {
            result.AddError("Custom post type plural name is required.");
        }
        else if (cpt.PluralName.Length > MaxNameLength)
        {
            result.AddError($"Custom post type plural name must not exceed {MaxNameLength} characters.");
        }

        ValidateSlugField(cpt.Slug, "Custom post type slug", result);
    }

    private static void ValidateSettingsPage(SettingsPageSpec? settingsPage, ValidationResult result)
    {
        if (settingsPage is null)
        {
            result.AddError("Settings page details are required when the 'settings-page' feature is requested.");
            return;
        }

        if (string.IsNullOrWhiteSpace(settingsPage.PageTitle))
        {
            result.AddError("Settings page title is required.");
        }
        else if (settingsPage.PageTitle.Length > MaxNameLength)
        {
            result.AddError($"Settings page title must not exceed {MaxNameLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(settingsPage.MenuTitle))
        {
            result.AddError("Settings page menu title is required.");
        }
        else if (settingsPage.MenuTitle.Length > MaxNameLength)
        {
            result.AddError($"Settings page menu title must not exceed {MaxNameLength} characters.");
        }

        if (settingsPage.Fields is null || settingsPage.Fields.Count == 0)
        {
            result.AddError("Settings page must define at least one field.");
            return;
        }

        if (settingsPage.Fields.Count > MaxSettingsFields)
        {
            result.AddError($"Settings page must not define more than {MaxSettingsFields} fields.");
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in settingsPage.Fields)
        {
            ValidateSettingsField(field, result);

            if (!string.IsNullOrWhiteSpace(field.Key) && !seenKeys.Add(field.Key))
            {
                result.AddError($"Duplicate settings field key: '{field.Key}'.");
            }
        }
    }

    private static void ValidateSettingsField(SettingsFieldSpec field, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(field.Key))
        {
            result.AddError("Settings field key is required.");
        }
        else if (field.Key.Length > MaxFieldKeyLength)
        {
            result.AddError($"Settings field key must not exceed {MaxFieldKeyLength} characters.");
        }
        else if (!FieldKeyPattern().IsMatch(field.Key))
        {
            result.AddError($"Settings field key '{field.Key}' must contain only lowercase letters, digits, and underscores, and must start with a letter.");
        }

        if (string.IsNullOrWhiteSpace(field.Label))
        {
            result.AddError("Settings field label is required.");
        }
        else if (field.Label.Length > MaxNameLength)
        {
            result.AddError($"Settings field label must not exceed {MaxNameLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(field.Type))
        {
            result.AddError("Settings field type is required.");
        }
        else if (!SettingsFieldType.Known.Contains(field.Type))
        {
            result.AddError($"Unsupported settings field type: '{field.Type}'.");
        }

        if (field.DefaultValue is not null && field.DefaultValue.Length > MaxFieldDefaultValueLength)
        {
            result.AddError($"Settings field default value must not exceed {MaxFieldDefaultValueLength} characters.");
        }
    }

    private static void ValidateCustomFields(CustomFieldsSpec? customFields, CustomPostTypeSpec? customPostType, ValidationResult result)
    {
        if (customFields is null)
        {
            result.AddError("Custom fields details are required when the 'custom-fields' feature is requested.");
            return;
        }

        if (string.IsNullOrWhiteSpace(customFields.PostType))
        {
            result.AddError("Custom fields must specify the post type they attach to.");
        }
        else if (customPostType is null || !string.Equals(customPostType.Slug, customFields.PostType, StringComparison.OrdinalIgnoreCase))
        {
            result.AddError($"Custom fields reference post type '{customFields.PostType}', but no matching custom post type is defined. Add the 'custom-post-type' feature with a matching slug.");
        }

        if (customFields.Fields is null || customFields.Fields.Count == 0)
        {
            result.AddError("Custom fields must define at least one field.");
            return;
        }

        if (customFields.Fields.Count > MaxSettingsFields)
        {
            result.AddError($"Custom fields must not define more than {MaxSettingsFields} fields.");
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in customFields.Fields)
        {
            ValidateCustomField(field, result);

            if (!string.IsNullOrWhiteSpace(field.Key) && !seenKeys.Add(field.Key))
            {
                result.AddError($"Duplicate custom field key: '{field.Key}'.");
            }
        }
    }

    private static void ValidateCustomField(CustomFieldSpec field, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(field.Key))
        {
            result.AddError("Custom field key is required.");
        }
        else if (field.Key.Length > MaxFieldKeyLength)
        {
            result.AddError($"Custom field key must not exceed {MaxFieldKeyLength} characters.");
        }
        else if (!FieldKeyPattern().IsMatch(field.Key))
        {
            result.AddError($"Custom field key '{field.Key}' must contain only lowercase letters, digits, and underscores, and must start with a letter.");
        }

        if (string.IsNullOrWhiteSpace(field.Label))
        {
            result.AddError("Custom field label is required.");
        }
        else if (field.Label.Length > MaxNameLength)
        {
            result.AddError($"Custom field label must not exceed {MaxNameLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(field.Type))
        {
            result.AddError("Custom field type is required.");
        }
        else if (!SettingsFieldType.Known.Contains(field.Type))
        {
            result.AddError($"Unsupported custom field type: '{field.Type}'.");
        }
    }

    private static void ValidateScheduledTask(ScheduledTaskSpec? task, ValidationResult result)
    {
        if (task is null)
        {
            result.AddError("Scheduled task details are required when the 'scheduled-task' feature is requested.");
            return;
        }

        if (string.IsNullOrWhiteSpace(task.TaskName))
        {
            result.AddError("Scheduled task name is required.");
        }
        else if (task.TaskName.Length > MaxNameLength)
        {
            result.AddError($"Scheduled task name must not exceed {MaxNameLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(task.Schedule))
        {
            result.AddError("Scheduled task schedule is required.");
        }
        else if (!CronSchedule.Known.Contains(task.Schedule))
        {
            result.AddError($"Unsupported scheduled task schedule: '{task.Schedule}'.");
        }

        if (string.IsNullOrWhiteSpace(task.HookName))
        {
            result.AddError("Scheduled task hook name is required.");
        }
        else if (task.HookName.Length > MaxFieldKeyLength)
        {
            result.AddError($"Scheduled task hook name must not exceed {MaxFieldKeyLength} characters.");
        }
        else if (!FieldKeyPattern().IsMatch(task.HookName))
        {
            result.AddError($"Scheduled task hook name '{task.HookName}' must contain only lowercase letters, digits, and underscores, and must start with a letter.");
        }
    }

    private static void ValidateFeatures(List<string>? features, ValidationResult result)
    {
        if (features is null)
        {
            return;
        }

        foreach (var feature in features)
        {
            if (string.IsNullOrWhiteSpace(feature))
            {
                result.AddError("Feature names cannot be empty.");
                continue;
            }

            if (!PluginFeature.Known.Contains(feature))
            {
                result.AddError($"Unknown feature: '{feature}'.");
            }
        }
    }
}
