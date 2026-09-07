namespace MyFinance.Core.Entities;

/// <summary>Key/value application settings stored inside the encrypted book.</summary>
public class AppSetting
{
    public required string Key { get; set; }

    public string? Value { get; set; }
}
