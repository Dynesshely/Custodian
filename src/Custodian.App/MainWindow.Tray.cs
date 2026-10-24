using System;
using System.Drawing;
using System.Windows;
using Custodian.App.Services;
using Forms = System.Windows.Forms;

namespace Custodian.App;

/// <summary>
/// 任务栏通知区（托盘）图标：复用同一套 Logo；左键唤出面板，右键弹出菜单（可退出）。
/// 菜单用 WinForms ContextMenuStrip —— 它天生处理托盘的前台/焦点问题，WPF ContextMenu 在托盘里容易闪退。
/// </summary>
public partial class MainWindow
{
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;

    private void SetupTray()
    {
        try
        {
            _trayMenu = BuildTrayMenu();

            _trayIcon = new Forms.NotifyIcon
            {
                Text = "Custodian",
                Icon = LoadTrayIcon(),
                ContextMenuStrip = _trayMenu,
                Visible = true,
            };

            _trayIcon.MouseClick += OnTrayMouseClick;
            Diagnostics.Write("tray: icon created");
        }
        catch (Exception ex)
        {
            Diagnostics.Write("tray: create failed " + ex.Message);
        }
    }

    private static Icon? LoadTrayIcon()
    {
        var resource = Application.GetResourceStream(new Uri("Assets/custodian.ico", UriKind.Relative));
        if (resource is null)
        {
            return null;
        }

        using var stream = resource.Stream;
        var size = Forms.SystemInformation.SmallIconSize; // 100% = 16，150% = 24
        return new Icon(stream, size);
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = new Forms.Padding(0, 4, 0, 4),
            Font = new Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, GraphicsUnit.Point),
            Renderer = new Forms.ToolStripProfessionalRenderer(new TrayColorTable(_isDarkTheme)) { RoundedEdges = false },
        };

        menu.Items.Add(CreateMenuItem("显示面板", () => ShowAndFocus()));
        menu.Items.Add(CreateMenuItem("收起到托盘", HidePanel));
        menu.Items.Add(CreateMenuItem("设置…", OpenSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("退出", ExitFromTray));

        // 主题可能在托盘创建之后才确定（_isDarkTheme 在 SourceInitialized 里赋值），
        // 所以每次弹出前按当前主题刷新配色。
        menu.Opening += (_, _) =>
        {
            var text = _isDarkTheme ? Color.FromArgb(240, 240, 240) : Color.FromArgb(26, 26, 26);
            menu.Renderer = new Forms.ToolStripProfessionalRenderer(new TrayColorTable(_isDarkTheme)) { RoundedEdges = false };
            menu.BackColor = _isDarkTheme ? Color.FromArgb(43, 43, 43) : Color.FromArgb(249, 249, 249);

            foreach (Forms.ToolStripItem item in menu.Items)
            {
                if (item is Forms.ToolStripMenuItem menuItem)
                {
                    menuItem.ForeColor = text;
                }
            }
        };

        return menu;
    }

    private Forms.ToolStripMenuItem CreateMenuItem(string text, Action action)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            Padding = new Forms.Padding(6, 4, 6, 4),
            ForeColor = _isDarkTheme ? Color.FromArgb(240, 240, 240) : Color.FromArgb(26, 26, 26),
        };
        item.Click += (_, _) => Dispatcher.BeginInvoke(action);
        return item;
    }

    private void OnTrayMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            ShowAndFocus();
        }
    }

    private void ShowTrayMenu()
    {
        _trayMenu?.Show(Forms.Cursor.Position);
        Diagnostics.Write("tray: menu opened");
    }

    /// <summary>关闭按钮：隐藏窗口但保留托盘与热键。</summary>
    private void HidePanel()
    {
        // 收起面板时把文件夹弹窗一并关掉，下次唤出回到正常层级；
        // 同时暂停「运行中」进程采样，托盘常驻时不再周期性枚举全系统进程。
        _vm.CloseFolder(restoreSelection: false);
        _processMonitor.SetActive(false);
        Hide();
        Diagnostics.Write("window: hidden to tray");
    }

    private void ExitFromTray()
    {
        Diagnostics.Write("tray: exit requested");
        DisposeTray();
        Application.Current.Shutdown();
    }

    private void DisposeTray()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.MouseClick -= OnTrayMouseClick;
            _trayIcon.ContextMenuStrip = null;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _trayMenu?.Dispose();
        _trayMenu = null;
    }

    /// <summary>托盘菜单配色（跟随应用深浅色）。</summary>
    private sealed class TrayColorTable : Forms.ProfessionalColorTable
    {
        private readonly bool _dark;

        public TrayColorTable(bool dark)
        {
            _dark = dark;
            UseSystemColors = false;
        }

        private Color Background => _dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(249, 249, 249);
        private Color Highlight => _dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(232, 232, 232);
        private Color BorderColor => _dark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(214, 214, 214);

        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuBorder => BorderColor;
        public override Color MenuItemBorder => BorderColor;
        public override Color MenuItemSelected => Highlight;
        public override Color MenuItemSelectedGradientBegin => Highlight;
        public override Color MenuItemSelectedGradientEnd => Highlight;
        public override Color SeparatorDark => BorderColor;
        public override Color SeparatorLight => Background;
    }
}
