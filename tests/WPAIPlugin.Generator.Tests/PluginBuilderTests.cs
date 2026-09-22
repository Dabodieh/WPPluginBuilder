using System.IO.Compression;
using WPAIPlugin.Generator.Models;
using Xunit;

namespace WPAIPlugin.Generator.Tests;

public class PluginBuilderTests
{
    private static PluginSpec StaffDirectorySpec() => new()
    {
        Name = "Staff Directory",
        Slug = "staff-directory",
        Description = "Simple staff directory plugin",
        Version = "1.0.0",
        Author = "AI Plugin Builder",
        Features = new List<string> { "shortcode" },
    };

    [Fact]
    public void Build_ValidSpec_ReturnsNonEmptyZip()
    {
        var builder = new PluginBuilder();

        var result = builder.Build(StaffDirectorySpec());

        Assert.NotNull(result.ZipBytes);
        Assert.True(result.ZipBytes.Length > 0);
        Assert.Equal("staff-directory.zip", result.FileName);
    }

    [Fact]
    public void Build_InvalidSpec_ThrowsPluginBuildException()
    {
        var builder = new PluginBuilder();
        var spec = StaffDirectorySpec();
        spec.Slug = "../evil";

        var ex = Assert.Throws<PluginBuildException>(() => builder.Build(spec));
        Assert.NotEmpty(ex.ValidationErrors);
    }

    [Fact]
    public void Build_CleansUpTemporaryFiles()
    {
        var builder = new PluginBuilder();
        var tempBefore = Directory.GetDirectories(Path.GetTempPath(), "wpaiplugin-build-*").Length;

        builder.Build(StaffDirectorySpec());

        var tempAfter = Directory.GetDirectories(Path.GetTempPath(), "wpaiplugin-build-*").Length;
        Assert.Equal(tempBefore, tempAfter);
    }

    [Fact]
    public void Build_Zip_ContainsExpectedTopLevelDirectoryAndPhpFile()
    {
        var builder = new PluginBuilder();

        var result = builder.Build(StaffDirectorySpec());

        using var ms = new MemoryStream(result.ZipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var entryNames = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("staff-directory/staff-directory.php", entryNames);
    }

    [Fact]
    public void Build_MainPhpFile_HasValidPluginHeaderAndAbspathGuard()
    {
        var builder = new PluginBuilder();

        var result = builder.Build(StaffDirectorySpec());

        var phpContent = ReadEntryText(result.ZipBytes, "staff-directory/staff-directory.php");

        Assert.StartsWith("<?php", phpContent);
        Assert.Contains("Plugin Name: Staff Directory", phpContent);
        Assert.Contains("Description: Simple staff directory plugin", phpContent);
        Assert.Contains("Version: 1.0.0", phpContent);
        Assert.Contains("Author: AI Plugin Builder", phpContent);
        Assert.Contains("if ( ! defined( 'ABSPATH' ) )", phpContent);
        Assert.Contains("exit;", phpContent);
    }

    [Fact]
    public void Build_ShortcodeFeature_GeneratesExpectedShortcodeOutput()
    {
        var builder = new PluginBuilder();

        var result = builder.Build(StaffDirectorySpec());

        var phpContent = ReadEntryText(result.ZipBytes, "staff-directory/staff-directory.php");

        Assert.Contains("add_shortcode( 'staff_directory', 'staff_directory_shortcode' );", phpContent);
        Assert.Contains("<div class=\"staff-directory\">\n    Staff Directory\n</div>", phpContent);
    }

    [Fact]
    public void Build_WithoutShortcodeFeature_DoesNotGenerateShortcodeCode()
    {
        var builder = new PluginBuilder();
        var spec = StaffDirectorySpec();
        spec.Features = new List<string>();

        var result = builder.Build(spec);

        var phpContent = ReadEntryText(result.ZipBytes, "staff-directory/staff-directory.php");

        Assert.DoesNotContain("add_shortcode", phpContent);
    }

    [Fact]
    public void Build_CustomPostTypeFeature_GeneratesRegisterPostTypeCode()
    {
        var builder = new PluginBuilder();
        var spec = StaffDirectorySpec();
        spec.Features = new List<string> { "custom-post-type" };
        spec.CustomPostType = new CustomPostTypeSpec
        {
            SingularName = "Staff Member",
            PluralName = "Staff Members",
            Slug = "staff-member",
            Public = true,
            HasArchive = false,
        };

        var result = builder.Build(spec);

        var phpContent = ReadEntryText(result.ZipBytes, "staff-directory/staff-directory.php");

        Assert.Contains("add_action( 'init', 'staff_member_register_post_type' );", phpContent);
        Assert.Contains("register_post_type( 'staff-member', array(", phpContent);
        Assert.Contains("'singular_name' => __( 'Staff Member' ),", phpContent);
        Assert.Contains("'name' => __( 'Staff Members' ),", phpContent);
    }

    [Fact]
    public void Build_SettingsPageFeature_GeneratesSettingsApiCode()
    {
        var builder = new PluginBuilder();
        var spec = StaffDirectorySpec();
        spec.Features = new List<string> { "settings-page" };
        spec.SettingsPage = new SettingsPageSpec
        {
            PageTitle = "Staff Directory Settings",
            MenuTitle = "Staff Directory",
            Fields = new List<SettingsFieldSpec>
            {
                new() { Key = "api_key", Label = "API Key", Type = "text", DefaultValue = "" },
                new() { Key = "notes", Label = "Notes", Type = "textarea" },
                new() { Key = "enabled", Label = "Enabled", Type = "checkbox", DefaultValue = "1" },
            },
        };

        var result = builder.Build(spec);

        var phpContent = ReadEntryText(result.ZipBytes, "staff-directory/staff-directory.php");

        Assert.Contains("add_options_page(", phpContent);
        Assert.Contains("register_setting(", phpContent);
        Assert.Contains("settings_fields(", phpContent);
        Assert.Contains("do_settings_sections(", phpContent);
        Assert.Contains("submit_button();", phpContent);
        Assert.Contains("current_user_can( 'manage_options' )", phpContent);
        Assert.Contains("sanitize_text_field(", phpContent);
        Assert.Contains("sanitize_textarea_field(", phpContent);
        Assert.Contains("esc_attr(", phpContent);
        Assert.Contains("esc_textarea(", phpContent);
        Assert.Contains("esc_html(", phpContent);
    }

    [Fact]
    public void Build_CustomFieldsFeature_GeneratesMetaBoxCode()
    {
        var builder = new PluginBuilder();
        var spec = StaffDirectorySpec();
        spec.Features = new List<string> { "custom-post-type", "custom-fields" };
        spec.CustomPostType = new CustomPostTypeSpec
        {
            SingularName = "Staff Member",
            PluralName = "Staff Members",
            Slug = "staff-member",
        };
        spec.CustomFields = new CustomFieldsSpec
        {
            PostType = "staff-member",
            Fields = new List<CustomFieldSpec>
            {
                new() { Key = "job_title", Label = "Job Title", Type = "text" },
                new() { Key = "bio", Label = "Bio", Type = "textarea" },
                new() { Key = "featured", Label = "Featured", Type = "checkbox" },
            },
        };

        var result = builder.Build(spec);

        var phpContent = ReadEntryText(result.ZipBytes, "staff-directory/staff-directory.php");

        Assert.Contains("add_meta_box(", phpContent);
        Assert.Contains("add_action( 'add_meta_boxes',", phpContent);
        Assert.Contains("wp_nonce_field(", phpContent);
        Assert.Contains("wp_verify_nonce(", phpContent);
        Assert.Contains("DOING_AUTOSAVE", phpContent);
        Assert.Contains("current_user_can( 'edit_post', $post_id )", phpContent);
        Assert.Contains("sanitize_text_field(", phpContent);
        Assert.Contains("sanitize_textarea_field(", phpContent);
        Assert.Contains("esc_attr(", phpContent);
        Assert.Contains("esc_textarea(", phpContent);
        Assert.Contains("esc_html__(", phpContent);
        Assert.Contains("update_post_meta(", phpContent);
        Assert.Contains("get_post_meta(", phpContent);
        Assert.Contains("add_action( 'save_post_staff-member',", phpContent);
    }

    [Fact]
    public void Build_ScheduledTaskFeature_GeneratesCronRegistrationCode()
    {
        var builder = new PluginBuilder();
        var spec = StaffDirectorySpec();
        spec.Features = new List<string> { "scheduled-task" };
        spec.ScheduledTask = new ScheduledTaskSpec
        {
            TaskName = "Cleanup Old Entries",
            Schedule = "daily",
            HookName = "staff_directory_cleanup",
        };

        var result = builder.Build(spec);

        var phpContent = ReadEntryText(result.ZipBytes, "staff-directory/staff-directory.php");

        Assert.Contains("register_activation_hook(", phpContent);
        Assert.Contains("register_deactivation_hook(", phpContent);
        Assert.Contains("wp_next_scheduled( 'staff_directory_cleanup' )", phpContent);
        Assert.Contains("wp_schedule_event( time(), 'daily', 'staff_directory_cleanup' )", phpContent);
        Assert.Contains("wp_unschedule_event(", phpContent);
        Assert.Contains("add_action( 'staff_directory_cleanup',", phpContent);
        Assert.Contains("// Placeholder: implement the 'Cleanup Old Entries' task here.", phpContent);
    }

    private static string ReadEntryText(byte[] zipBytes, string entryName)
    {
        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"Entry '{entryName}' not found in zip.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
