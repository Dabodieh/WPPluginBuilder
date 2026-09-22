using WPAIPlugin.Planning;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Planning;

public class PlanningProviderResolverTests
{
    private sealed class StubProvider : IPlanningProvider
    {
        public StubProvider(string name) => Name = name;

        public string Name { get; }

        public Task<PlanningResult> PlanAsync(PlanningRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private static PlanningProviderResolver CreateResolver(string defaultProvider) =>
        new(new IPlanningProvider[] { new StubProvider("openai"), new StubProvider("anthropic") }, defaultProvider);

    [Fact]
    public void Resolve_ExplicitOpenAI_ResolvesOpenAI()
    {
        var resolver = CreateResolver("openai");

        var provider = resolver.Resolve("openai");

        Assert.Equal("openai", provider.Name);
    }

    [Fact]
    public void Resolve_ExplicitAnthropic_ResolvesAnthropic()
    {
        var resolver = CreateResolver("openai");

        var provider = resolver.Resolve("anthropic");

        Assert.Equal("anthropic", provider.Name);
    }

    [Fact]
    public void Resolve_OmittedProvider_ResolvesDefault()
    {
        var resolver = CreateResolver("openai");

        var provider = resolver.Resolve(null);

        Assert.Equal("openai", provider.Name);
    }
}
