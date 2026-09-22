using Microsoft.Extensions.Options;

namespace WPAIPlugin.Generator.Tests.Planning;

/// <summary>Minimal test double for IOptionsMonitor&lt;T&gt;: always returns a fixed value, never changes.</summary>
internal sealed class FakeOptionsMonitor<T> : IOptionsMonitor<T>
{
    public FakeOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener) => new NoopDisposable();

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
