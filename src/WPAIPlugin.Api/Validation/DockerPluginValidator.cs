using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WPAIPlugin.Api.Validation;

/// <summary>
/// Runs a generated plugin ZIP through the disposable Docker WordPress
/// environment (docker/docker-compose.validate.yml): php -l, WordPress
/// install, plugin install, plugin activate, verify active. Always tears
/// down containers/volumes and deletes its temporary files.
///
/// Every docker invocation runs directly via <see cref="Process"/> with
/// <see cref="ProcessStartInfo.ArgumentList"/> - no shell, no PowerShell,
/// no string-concatenated commands.
/// </summary>
public class DockerPluginValidator
{
    private readonly string _composeFilePath;
    private readonly TimeSpan _timeout;
    private readonly ILogger<DockerPluginValidator> _logger;

    public DockerPluginValidator(string composeFilePath, TimeSpan timeout, ILogger<DockerPluginValidator> logger)
    {
        _composeFilePath = composeFilePath;
        _timeout = timeout;
        _logger = logger;
    }

    public async Task<PluginValidationResult> ValidateAsync(byte[] pluginZip, string pluginSlug, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);
        var token = timeoutCts.Token;

        // 1. Unique temporary validation directory.
        var tempDir = Path.Combine(Path.GetTempPath(), "wpaiplugin-buildvalidate-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(tempDir, "plugin.zip");
        var extractDir = Path.Combine(tempDir, "extracted");

        // 3. Isolated Docker Compose project name containing a GUID, so
        // concurrent validation runs never share containers/networks/volumes.
        var projectName = "wpaiplugin-buildvalidate-" + Guid.NewGuid().ToString("N");
        var dockerStarted = false;

        try
        {
            Directory.CreateDirectory(tempDir);

            // 2. Save the generated ZIP there.
            await File.WriteAllBytesAsync(zipPath, pluginZip, token);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);

            var dockerAvailable = await RunAsync(token, "docker", new[] { "version", "--format", "{{.Server.Version}}" });
            if (!dockerAvailable.Succeeded)
            {
                return PluginValidationResult.Failed(
                    PluginValidationFailureReason.ValidationUnavailable,
                    "Plugin validation is currently unavailable.");
            }

            // 4. Start the disposable environment.
            var up = await RunAsync(token, "docker", ComposeArgs(projectName, "up", "-d"));
            if (!up.Succeeded)
            {
                return PluginValidationResult.Failed(PluginValidationFailureReason.WordPressSetupFailed, "Could not start the validation environment.");
            }
            dockerStarted = true;

            if (!await WaitForDbReadyAsync(projectName, token))
            {
                return PluginValidationResult.Failed(PluginValidationFailureReason.WordPressSetupFailed, "The validation database did not become ready in time.");
            }

            if (!await WaitForWordPressFilesReadyAsync(projectName, token))
            {
                return PluginValidationResult.Failed(PluginValidationFailureReason.WordPressSetupFailed, "WordPress core files were not ready in time.");
            }

            var containerId = await GetContainerIdAsync(projectName, token);
            if (containerId is null)
            {
                return PluginValidationResult.Failed(PluginValidationFailureReason.WordPressSetupFailed, "Could not resolve the validation container.");
            }

            var copyZip = await RunAsync(token, "docker", new[] { "cp", zipPath, $"{containerId}:/tmp/plugin.zip" });
            var copyLint = await RunAsync(token, "docker", new[] { "cp", extractDir + "/.", $"{containerId}:/tmp/lint" });
            if (!copyZip.Succeeded || !copyLint.Succeeded)
            {
                return PluginValidationResult.Failed(PluginValidationFailureReason.WordPressSetupFailed, "Could not copy the generated plugin into the validation environment.");
            }

            // 6. PHP lint of every generated PHP file.
            var phpFiles = Directory.GetFiles(extractDir, "*.php", SearchOption.AllDirectories);
            if (phpFiles.Length == 0)
            {
                return PluginValidationResult.Failed(PluginValidationFailureReason.PhpLintFailed, "Generated plugin contained no PHP files to lint.");
            }

            foreach (var file in phpFiles)
            {
                var relative = Path.GetRelativePath(extractDir, file).Replace('\\', '/');
                var lint = await RunAsync(token, "docker", ComposeExecArgs(projectName, "wpcli", "php", "-l", $"/tmp/lint/{relative}"));
                if (!lint.Succeeded)
                {
                    _logger.LogWarning("php -l failed for {File}: {Output}", relative, Truncate(lint.StdErr + lint.StdOut));
                    return PluginValidationResult.Failed(PluginValidationFailureReason.PhpLintFailed, $"PHP syntax error in a generated file: {relative}.");
                }
            }

            // 7. Install WordPress.
            var isInstalled = await RunAsync(token, "docker", ComposeExecArgs(projectName, "wpcli", "wp", "core", "is-installed", "--allow-root"));
            if (!isInstalled.Succeeded)
            {
                var install = await RunAsync(token, "docker", ComposeExecArgs(
                    projectName, "wpcli", "wp", "core", "install",
                    "--url=http://validation.local",
                    "--title=Validation Site",
                    "--admin_user=validate_admin",
                    "--admin_password=validate-admin-pw",
                    "--admin_email=validate@example.test",
                    "--skip-email", "--allow-root"));
                if (!install.Succeeded)
                {
                    _logger.LogWarning("wp core install failed: {Output}", Truncate(install.StdErr + install.StdOut));
                    return PluginValidationResult.Failed(PluginValidationFailureReason.WordPressSetupFailed, "WordPress installation failed.", phpLintPassed: true);
                }
            }

            // 8. Install the generated plugin ZIP.
            var pluginInstall = await RunAsync(token, "docker", ComposeExecArgs(projectName, "wpcli", "wp", "plugin", "install", "/tmp/plugin.zip", "--allow-root"));
            if (!pluginInstall.Succeeded)
            {
                _logger.LogWarning("wp plugin install failed: {Output}", Truncate(pluginInstall.StdErr + pluginInstall.StdOut));
                return PluginValidationResult.Failed(PluginValidationFailureReason.PluginInstallFailed, "The plugin could not be installed.", phpLintPassed: true, wordPressInstalled: true);
            }

            // 9. Activate the plugin.
            var activate = await RunAsync(token, "docker", ComposeExecArgs(projectName, "wpcli", "wp", "plugin", "activate", pluginSlug, "--allow-root"));
            if (!activate.Succeeded)
            {
                _logger.LogWarning("wp plugin activate failed: {Output}", Truncate(activate.StdErr + activate.StdOut));
                return PluginValidationResult.Failed(PluginValidationFailureReason.PluginActivationFailed, "The plugin could not be activated.", phpLintPassed: true, wordPressInstalled: true, pluginInstalled: true);
            }

            // 10. Verify it is active.
            var isActive = await RunAsync(token, "docker", ComposeExecArgs(projectName, "wpcli", "wp", "plugin", "is-active", pluginSlug, "--allow-root"));
            if (!isActive.Succeeded)
            {
                return PluginValidationResult.Failed(PluginValidationFailureReason.PluginActivationFailed, "The plugin did not report as active.", phpLintPassed: true, wordPressInstalled: true, pluginInstalled: true);
            }

            return PluginValidationResult.Passed();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PluginValidationResult.Failed(PluginValidationFailureReason.Cancelled, "Plugin validation was cancelled.");
        }
        catch (OperationCanceledException)
        {
            return PluginValidationResult.Failed(PluginValidationFailureReason.Timeout, "Plugin validation timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin validation failed unexpectedly.");
            return PluginValidationResult.Failed(PluginValidationFailureReason.ValidationUnavailable, "Plugin validation is currently unavailable.");
        }
        finally
        {
            // 11. Always destroy containers/volumes and delete temporary files.
            if (dockerStarted)
            {
                try
                {
                    await RunAsync(CancellationToken.None, "docker", ComposeArgs(projectName, "down", "-v", "--remove-orphans"));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to tear down validation containers for project {ProjectName}.", projectName);
                }
            }

            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temporary validation directory {TempDir}.", tempDir);
            }
        }
    }

    private async Task<bool> WaitForDbReadyAsync(string projectName, CancellationToken token)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var check = await RunAsync(token, "docker", ComposeExecArgs(projectName, "db", "healthcheck.sh", "--connect", "--innodb_initialized"));
            if (check.Succeeded)
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        return false;
    }

    private async Task<bool> WaitForWordPressFilesReadyAsync(string projectName, CancellationToken token)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var check = await RunAsync(token, "docker", ComposeExecArgs(projectName, "wpcli", "test", "-f", "/var/www/html/wp-load.php"));
            if (check.Succeeded)
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        return false;
    }

    private async Task<string?> GetContainerIdAsync(string projectName, CancellationToken token)
    {
        var result = await RunAsync(token, "docker", ComposeArgs(projectName, "ps", "-q", "wpcli"));
        var id = result.StdOut.Trim();
        return result.Succeeded && id.Length > 0 ? id : null;
    }

    private string[] ComposeArgs(string projectName, params string[] args) =>
        new[] { "compose", "-p", projectName, "-f", _composeFilePath }.Concat(args).ToArray();

    private string[] ComposeExecArgs(string projectName, string service, params string[] args) =>
        ComposeArgs(projectName, new[] { "exec", "-T", service }.Concat(args).ToArray());

    /// <summary>
    /// Runs a single process to completion. Never uses a shell (UseShellExecute is
    /// false, arguments go through ArgumentList, never string concatenation).
    /// Protected/virtual so tests can substitute process execution without
    /// running real Docker commands, while everything else in this class
    /// (temp dirs, compose project naming, step sequencing, cleanup) runs unchanged.
    /// </summary>
    protected virtual async Task<ProcessResult> RunAsync(CancellationToken cancellationToken, string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception)
        {
            // e.g. docker executable not found.
            return new ProcessResult(-1, string.Empty, string.Empty);
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;
        return new ProcessResult(process.ExitCode, stdOut, stdErr);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort; the process may have already exited.
        }
    }

    private static string Truncate(string value, int maxLength = 500) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    public readonly struct ProcessResult
    {
        public ProcessResult(int exitCode, string stdOut, string stdErr)
        {
            ExitCode = exitCode;
            StdOut = stdOut;
            StdErr = stdErr;
        }

        public int ExitCode { get; }

        public string StdOut { get; }

        public string StdErr { get; }

        public bool Succeeded => ExitCode == 0;
    }
}
