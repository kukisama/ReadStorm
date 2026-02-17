namespace ReadStorm.Desktop.ViewModels;

/// <summary>纸张预设。</summary>
public sealed class PaperPreset(string name, string background, string foreground)
{
    public string Name { get; } = name;
    public string Background { get; } = background;
    public string Foreground { get; } = foreground;
    public string Display => $"{Name} ({Background})";
}
