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

    private static string ReadEntryText(byte[] zipBytes, string entryName)
    {
        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"Entry '{entryName}' not found in zip.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
