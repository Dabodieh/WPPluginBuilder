namespace WPAIPlugin.Api.Configuration;

/// <summary>The From identity used for every transactional email (account-recovery milestone). Never a secret - safe to log.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string? FromAddress { get; set; }

    public string? FromName { get; set; }
}
