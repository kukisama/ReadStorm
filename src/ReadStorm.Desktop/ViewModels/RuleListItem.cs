using CommunityToolkit.Mvvm.ComponentModel;

namespace ReadStorm.Desktop.ViewModels;

/// <summary>规则列表条目（用于 ListBox 绑定）。</summary>
public sealed partial class RuleListItem : ObservableObject
{
    public int Id { get; }
    public string Name { get; }
    public string Url { get; }
    public bool HasSearch { get; }

    /// <summary>null=未知, true=可达(绿), false=不可达(红)。</summary>
    [ObservableProperty]
    private bool? isHealthy;

    public string HealthDot => IsHealthy switch
    {
        true => "●",
        false => "●",
        null => "○",
    };

    public string HealthColor => IsHealthy switch
    {
        true => "#22C55E",
        false => "#EF4444",
        null => "#9CA3AF",
    };

    public string Display => $"[{Id}] {Name}";

    public RuleListItem(int id, string name, string url, bool hasSearch, bool? isHealthy = null)
    {
        Id = id;
        Name = name;
        Url = url;
        HasSearch = hasSearch;
        IsHealthy = isHealthy;
    }

    partial void OnIsHealthyChanged(bool? value)
    {
        OnPropertyChanged(nameof(HealthDot));
        OnPropertyChanged(nameof(HealthColor));
    }
}
