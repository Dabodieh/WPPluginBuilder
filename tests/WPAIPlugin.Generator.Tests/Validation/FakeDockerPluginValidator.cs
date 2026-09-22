using Microsoft.Extensions.Logging.Abstractions;
using WPAIPlugin.Api.Validation;

namespace WPAIPlugin.Generator.Tests.Validation;

/// <summary>
/// Test double for <see cref="DockerPluginValidator"/>. Overrides the process
/// execution seam so tests never run real Docker, while exercising the
/// controller/DI path exactly as production does.
/// </summary>
public sealed class FakeDockerPluginValidator : DockerPluginValidator
{
    public Func<string, IEnumerable<string>, ProcessResult>? Handler { get; set; }

    public FakeDockerPluginValidator()
        : base(composeFilePath: "unused.yml", timeout: TimeSpan.FromSeconds(5), NullLogger<DockerPluginValidator>.Instance)
    {
    }

    protected override Task<ProcessResult> RunAsync(CancellationToken cancellationToken, string fileName, IEnumerable<string> arguments)
    {
        var args = arguments.ToList();

        // "docker compose ... ps -q wpcli" must return a non-empty container id
        // so the container-resolution step never fails before reaching the
        // step under test, unless a test's own handler overrides this args shape.
        if (args.Contains("ps") && args.Contains("-q"))
        {
            return Task.FromResult(new ProcessResult(0, "fake-container-id", string.Empty));
        }

        var result = Handler?.Invoke(fileName, args) ?? new ProcessResult(0, string.Empty, string.Empty);
        return Task.FromResult(result);
    }
}
