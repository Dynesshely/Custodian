using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Custodian.App;

public partial class App : Application
{
    private const string MutexName = "Custodian.SingleInstance.7C1E6B0B";
    private const string ShowEventName = "Custodian.ShowPanel.7C1E6B0B";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var isFirstInstance);
        Services.Diagnostics.Write($"startup: pid={Environment.ProcessId} first={isFirstInstance}");
        if (!isFirstInstance)
        {
            var signaled = SignalExistingInstance();
            Services.Diagnostics.Write($"startup: second instance, signaled={signaled}, exiting");
            Shutdown();
            return;
        }

        LoadTheme();
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        StartShowSignalListener();
    }

    private void LoadTheme()
    {
        try
        {
            var theme = new ResourceDictionary { Source = new Uri("Themes/Fluent.xaml", UriKind.Relative) };
            Resources.MergedDictionaries.Add(theme);
        }
        catch
        {
            // 主题字典缺失时使用 Themes/Fallback.xaml 基线
        }

        try
        {
            Themes.ThemeManager.Initialize();

            // 排障开关：CUSTODIAN_SAFE=theme:light / theme:dark 可强制主题，便于截图自测
            if (SafeMode.Argument("theme:") is { Length: > 0 } forced &&
                (forced.Equals("light", StringComparison.OrdinalIgnoreCase) ||
                 forced.Equals("dark", StringComparison.OrdinalIgnoreCase)))
            {
                Themes.ThemeManager.FollowSystem = false;
                Themes.ThemeManager.SetDark(forced.Equals("dark", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch
        {
            // 主题管理器不可用时保持系统默认
        }
    }

    private void StartShowSignalListener()
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (MainWindow is MainWindow window)
                    {
                        window.ShowAndFocus();
                    }
                }), DispatcherPriority.Input);
            }
        })
        {
            IsBackground = true,
            Name = "Custodian.ShowSignal",
        };

        thread.Start();
    }

    private static bool SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }

                return true;
            }
        }
        catch
        {
            // 忽略：旧实例可能已退出
        }

        return false;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Custodian");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}{Environment.NewLine}");
        }
        catch
        {
            // 记录失败时不再递归
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _showEvent?.Dispose();
        base.OnExit(e);
    }
}
