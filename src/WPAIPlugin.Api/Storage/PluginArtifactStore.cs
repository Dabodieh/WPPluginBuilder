using Microsoft.Extensions.Options;

namespace WPAIPlugin.Api.Storage;

/// <summary>
/// Local-filesystem storage for generated plugin ZIPs, outside wwwroot and
/// never exposed via UseStaticFiles. Every path component is a server-generated
/// GUID - never an email address, plugin name, or other user-supplied value -
/// so nothing here is reachable via path traversal or guessable naming.
/// The returned ArtifactKey is an opaque identifier for this store to resolve
/// later; it is never a raw filesystem path and is never returned to the browser.
/// </summary>
public sealed class PluginArtifactStore
{
    private readonly string _rootPath;

    public PluginArtifactStore(IOptions<ArtifactStorageOptions> options, IWebHostEnvironment environment)
    {
        var configuredRoot = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot);
    }

    public async Task<string> SaveAsync(string userId, Guid projectId, Guid versionId, byte[] zipBytes, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(_rootPath, userId, projectId.ToString(), versionId.ToString());
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, "plugin.zip");
        await File.WriteAllBytesAsync(filePath, zipBytes, cancellationToken);

        // The artifact key is opaque to callers: this store is the only code
        // that turns it back into a filesystem path.
        return $"{userId}/{projectId}/{versionId}";
    }

    public async Task<byte[]?> ReadAsync(string artifactKey, CancellationToken cancellationToken = default)
    {
        var filePath = ResolvePath(artifactKey);
        if (filePath is null || !File.Exists(filePath))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(filePath, cancellationToken);
    }

    public void TryDelete(string artifactKey)
    {
        var filePath = ResolvePath(artifactKey);
        if (filePath is null)
        {
            return;
        }

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup only.
        }
    }

    private string? ResolvePath(string artifactKey)
    {
        // artifactKey is always produced by SaveAsync as "userId/projectId/versionId" -
        // three GUID-shaped segments - so this never accepts arbitrary caller input.
        var parts = artifactKey.Split('/');
        if (parts.Length != 3 || parts.Any(p => !Guid.TryParse(p, out _)))
        {
            return null;
        }

        return Path.Combine(_rootPath, parts[0], parts[1], parts[2], "plugin.zip");
    }
}
