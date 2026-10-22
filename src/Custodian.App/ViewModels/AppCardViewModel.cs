using System.IO;
using System.Windows.Media;
using Custodian.Core.Discovery;

namespace Custodian.App.ViewModels;

/// <summary>启动面板上的一张应用卡片（普通应用与标准应用共用）。</summary>
public sealed class AppCardViewModel : ObservableObject, INavItem
{
    private ImageSource? _icon;
    private bool _isSelected;
    private bool _isRunning;
    private bool _isVisible = true;

    public AppCardViewModel(DiscoveredApp app, string sandboxDisplay, CardMetrics metrics)
    {
        App = app;
        SandboxDisplay = sandboxDisplay;
        Metrics = metrics;
        ProcessName = Path.GetFileNameWithoutExtension(app.TargetPath);
    }

    public DiscoveredApp App { get; }

    /// <summary>
    /// 共享布局指标：由 <see cref="MainViewModel"/> 持有，所有卡片引用同一个实例；
    /// 切换标准 / 紧凑视图只改它，卡片靠绑定自动重排。
    /// </summary>
    public CardMetrics Metrics { get; }

    public string SandboxDisplay { get; }

    public string Name => App.Name;

    /// <summary>键盘导航模型里的名字（卡片＝应用名）。</summary>
    public string NavTitle => Name;

    public string DisplayPath => App.DisplayPath;

    public string FullPath => string.IsNullOrWhiteSpace(App.Arguments)
        ? App.TargetPath
        : App.TargetPath + " " + App.Arguments;

    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

    /// <summary>进程名（去扩展名），用于「运行中」检测。</summary>
    public string ProcessName { get; }

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (Set(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => _icon is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                RaiseSelectedChanged();
            }
        }
    }

    /// <summary>目标进程正在运行（卡片显示绿点 + 绿框；选中框优先级更高）。</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (Set(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(ShowRunningRing));
            }
        }
    }

    /// <summary>绿框只在「运行中且未被选中」时显示，选中态优先。</summary>
    public bool ShowRunningRing => _isRunning && !_isSelected;

    /// <summary>筛选可见性（用 Collapsed 而不是重建集合，避免每次筛选重建控件）。</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public bool Matches(string query) =>
        Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        App.TargetPath.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        SandboxDisplay.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    protected override void OnSelectedChanged()
    {
        OnPropertyChanged(nameof(ShowRunningRing));
    }
}
