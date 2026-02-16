using CommunityToolkit.Mvvm.ComponentModel;
using ReadStorm.Domain.Models;

namespace ReadStorm.Desktop.ViewModels;

/// <summary>
/// ComboBox 可绑定的书源条目，包含健康状态指示。
/// </summary>
public partial class SourceItem : ObservableObject
{
    public int Id { get; }

    public string Name { get; }

    public string Url { get; }

    public bool SearchSupported { get; }

    /// <summary>
    /// null = 尚未检测, true = 可达, false = 不可达。
    /// </summary>
    [ObservableProperty]
    private bool? isHealthy;

    /// <summary>健康指示圆点的颜色文本，供 UI 绑定。</summary>
    public string HealthDot => IsHealthy switch
    {
        true => "●",
        false => "●",
        null => "○",
    };

    /// <summary>健康指示颜色，供 UI 绑定。</summary>
    public string HealthColor => IsHealthy switch
    {
        true => "#22C55E",   // green-500
        false => "#EF4444",  // red-500
        null => "#9CA3AF",   // gray-400
    };

    /// <summary>ComboBox 显示文本：圆点 + 名称。</summary>
    public string DisplayName => $"{HealthDot} {Name}";

    public SourceItem(BookSourceRule rule)
    {
        Id = rule.Id;
        Name = rule.Name;
        Url = rule.Url;
        SearchSupported = rule.SearchSupported;
    }

    partial void OnIsHealthyChanged(bool? value)
    {
        OnPropertyChanged(nameof(HealthDot));
        OnPropertyChanged(nameof(HealthColor));
        OnPropertyChanged(nameof(DisplayName));
    }
}
