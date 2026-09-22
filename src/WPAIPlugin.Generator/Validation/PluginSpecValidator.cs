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

    private const int MaxSlugLength = 100;
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 1000;
    private const int MaxAuthorLength = 200;

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

    private static void ValidateSlug(string slug, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            result.AddError("Slug is required.");
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
            result.AddError("Slug contains unsafe path characters.");
            return;
        }

        if (slug.Length > MaxSlugLength)
        {
            result.AddError($"Slug must not exceed {MaxSlugLength} characters.");
            return;
        }

        if (!SlugPattern().IsMatch(slug))
        {
            result.AddError("Slug must contain only lowercase letters, digits, and hyphens, and cannot start or end with a hyphen.");
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
