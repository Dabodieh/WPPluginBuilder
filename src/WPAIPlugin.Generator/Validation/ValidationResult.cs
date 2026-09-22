namespace WPAIPlugin.Generator.Validation;

public sealed class ValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public List<string> Errors { get; } = new();

    public static ValidationResult Success() => new();

    public void AddError(string error) => Errors.Add(error);
}
