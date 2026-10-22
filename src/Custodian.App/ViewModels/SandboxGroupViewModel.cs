using System.Collections.ObjectModel;
using Custodian.Core.Sandboxie;

namespace Custodian.App.ViewModels;

/// <summary>一个沙盒在面板中的分组：普通应用 + 默认折叠的标准应用程序。</summary>
public sealed class SandboxGroupViewModel : ObservableObject
{
    private string _countText = string.Empty;
    private string _standardCountText = string.Empty;
    private bool _isStandardExpanded;

    public SandboxGroupViewModel(SandboxBox box)
    {
        Box = box;
    }

    public SandboxBox Box { get; }

    public string Name => Box.Name;

    public string GroupLabel => Box.DisplayGroup;

    public string GroupLabelSuffix => HasGroupLabel ? "· " + GroupLabel : string.Empty;

    public bool HasGroupLabel => GroupLabel.Length > 0;

    /// <summary>普通应用（来自沙盒内快捷方式）。</summary>
    public ObservableCollection<AppCardViewModel> AllApps { get; } = new();

    public ObservableCollection<AppCardViewModel> Apps { get; } = new();

    /// <summary>标准应用程序（Windows 自带工具），默认折叠。</summary>
    public ObservableCollection<AppCardViewModel> StandardApps { get; } = new();

    public ObservableCollection<AppCardViewModel> VisibleStandardApps { get; } = new();

    public string CountText
    {
        get => _countText;
        set => Set(ref _countText, value);
    }

    public string StandardCountText
    {
        get => _standardCountText;
        set => Set(ref _standardCountText, value);
    }

    public bool IsStandardExpanded
    {
        get => _isStandardExpanded;
        set => Set(ref _isStandardExpanded, value);
    }

    public bool HasApps => Apps.Count > 0;

    public bool HasStandardApps => StandardApps.Count > 0;

    public bool HasAnyContent => HasApps || HasStandardApps;

    public void ApplyFilter(string query)
    {
        Apps.Clear();
        foreach (var card in AllApps)
        {
            if (query.Length == 0 || card.Matches(query))
            {
                Apps.Add(card);
            }
        }

        VisibleStandardApps.Clear();
        foreach (var card in StandardApps)
        {
            if (query.Length == 0 || card.Matches(query))
            {
                VisibleStandardApps.Add(card);
            }
        }

        CountText = Apps.Count.ToString();
        StandardCountText = VisibleStandardApps.Count.ToString();
        OnPropertyChanged(nameof(HasApps));
        OnPropertyChanged(nameof(HasAnyContent));
        OnPropertyChanged(nameof(HasStandardApps));
    }
}
