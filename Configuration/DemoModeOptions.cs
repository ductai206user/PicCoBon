namespace PickleballClub.Configuration;

public sealed class DemoModeOptions
{
    public const string SectionName = "DemoMode";

    public bool Enabled { get; set; }
    public string Label { get; set; } = "DEMO MODE";
}
