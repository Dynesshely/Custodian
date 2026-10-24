using System;
using System.Windows;

namespace Custodian.App;

/// <summary>独立设置窗口的宿主：托盘右键、调试通道都从这里打开。</summary>
public partial class MainWindow
{
    private SettingsWindow? _settingsWindow;

    /// <summary>设置是否已经打开（调试通道 / 自测用）。</summary>
    public bool IsSettingsOpen => _settingsWindow is not null;

    /// <summary>打开设置窗口（已打开时前置）。</summary>
    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settingsCoordinator);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();

        Services.Diagnostics.Write("settings: window opened");
    }

    public void CloseSettings()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        _settingsWindow.Close();
        _settingsWindow = null;
        Services.Diagnostics.Write("settings: window closed");
    }
}
