using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Custodian.Interop.Native;

namespace Custodian.Interop;

public sealed class HotkeyPressedEventArgs : EventArgs
{
    public HotkeyPressedEventArgs(int id)
    {
        Id = id;
    }

    public int Id { get; }
}

/// <summary>
/// Global hotkeys on top of RegisterHotKey / WM_HOTKEY.
/// Passing IntPtr.Zero creates a hidden message-only window; the caller's thread needs a Dispatcher,
/// otherwise a dedicated STA message thread is spun up so the manager also works from console code.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<int, (ModifierKeys Modifiers, Key Key)> _registrations = new();
    private readonly SynchronizationContext? _callerContext;
    private readonly int _callerThreadId;

    private HwndSource? _source;
    private Dispatcher? _sourceDispatcher;
    private Thread? _sinkThread;
    private IntPtr _hwnd;
    private bool _ownsSource;
    private bool _disposed;

    public HotkeyManager(IntPtr hwnd)
    {
        _callerContext = SynchronizationContext.Current;
        _callerThreadId = Environment.CurrentManagedThreadId;

        if (hwnd != IntPtr.Zero)
        {
            var source = HwndSource.FromHwnd(hwnd);
            if (source is not null)
            {
                Attach(source, ownsSource: false);
                return;
            }
        }

        CreateSink();
    }

    public event EventHandler<HotkeyPressedEventArgs>? Pressed;

    /// <summary>Registers a hotkey. Returns false (never throws) when the combination is unavailable.</summary>
    public bool Register(int id, ModifierKeys modifiers, Key key)
    {
        if (_disposed)
        {
            return false;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return false;
        }

        var registered = false;
        RunOnSinkThread(() =>
        {
            lock (_gate)
            {
                if (_registrations.ContainsKey(id))
                {
                    UnregisterCore(id);
                }

                var nativeModifiers = ToNativeModifiers(modifiers) | NativeMethods.MOD_NOREPEAT;
                if (NativeMethods.RegisterHotKey(_hwnd, id, nativeModifiers, (uint)virtualKey) != 0)
                {
                    _registrations[id] = (modifiers, key);
                    registered = true;
                }
            }
        });

        return registered;
    }

    public void Unregister(int id)
    {
        if (_disposed)
        {
            return;
        }

        RunOnSinkThread(() =>
        {
            lock (_gate)
            {
                UnregisterCore(id);
            }
        });
    }

    public void UnregisterAll()
    {
        RunOnSinkThread(() =>
        {
            lock (_gate)
            {
                foreach (var id in _registrations.Keys.ToArray())
                {
                    if (_hwnd != IntPtr.Zero)
                    {
                        NativeMethods.UnregisterHotKey(_hwnd, id);
                    }
                }

                _registrations.Clear();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            UnregisterAll();
        }
        catch
        {
            // Ignored.
        }

        var source = _source;
        var dispatcher = _sourceDispatcher;
        var ownsSource = _ownsSource;
        if (source is not null)
        {
            try
            {
                void Teardown()
                {
                    try
                    {
                        source.RemoveHook(WndProc);
                    }
                    catch
                    {
                        // Ignored.
                    }

                    if (ownsSource)
                    {
                        source.Dispose();
                    }
                }

                if (dispatcher is not null && !dispatcher.CheckAccess())
                {
                    if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                    {
                        dispatcher.Invoke(Teardown);
                    }
                }
                else
                {
                    Teardown();
                }
            }
            catch
            {
                // Ignored.
            }
        }

        _source = null;
        _sourceDispatcher = null;
        _hwnd = IntPtr.Zero;

        if (_sinkThread is not null)
        {
            try
            {
                dispatcher?.InvokeShutdown();
            }
            catch
            {
                // Ignored.
            }

            try
            {
                _sinkThread.Join(1000);
            }
            catch
            {
                // Ignored.
            }

            _sinkThread = null;
        }
    }

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>(5);
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            parts.Add("Win");
        }

        if (key != Key.None)
        {
            parts.Add(FormatKey(key));
        }

        return string.Join("+", parts);
    }

    public static (ModifierKeys Modifiers, Key Key)? Parse(string text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var modifiers = ModifierKeys.None;
            var key = Key.None;

            foreach (var raw in text.Split(['+', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                switch (token.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= ModifierKeys.Control;
                        continue;
                    case "alt":
                    case "menu":
                        modifiers |= ModifierKeys.Alt;
                        continue;
                    case "shift":
                        modifiers |= ModifierKeys.Shift;
                        continue;
                    case "win":
                    case "windows":
                    case "logo":
                    case "meta":
                        modifiers |= ModifierKeys.Windows;
                        continue;
                }

                var parsed = ParseKey(token);
                if (parsed is null || key != Key.None)
                {
                    return null;
                }

                key = parsed.Value;
            }

            if (key == Key.None)
            {
                return null;
            }

            return (modifiers, key);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatKey(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((char)('0' + ((int)key - (int)Key.D0))).ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return "Num" + ((int)key - (int)Key.NumPad0);
        }

        return key.ToString();
    }

    private static Key? ParseKey(string token)
    {
        if (token.Length == 1 && char.IsDigit(token[0]))
        {
            return (Key)((int)Key.D0 + (token[0] - '0'));
        }

        switch (token.ToLowerInvariant())
        {
            case "space":
                return Key.Space;
            case "enter":
            case "return":
                return Key.Enter;
            case "esc":
            case "escape":
                return Key.Escape;
            case "del":
            case "delete":
                return Key.Delete;
            case "ins":
            case "insert":
                return Key.Insert;
            case "pgup":
            case "pageup":
                return Key.PageUp;
            case "pgdn":
            case "pagedown":
                return Key.PageDown;
            case "back":
            case "backspace":
                return Key.Back;
            case "tab":
                return Key.Tab;
        }

        if (token.StartsWith("num", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(token.AsSpan(3), out var number)
            && number is >= 0 and <= 9)
        {
            return (Key)((int)Key.NumPad0 + number);
        }

        if (Enum.TryParse<Key>(token, ignoreCase: true, out var key) && key != Key.None)
        {
            return key;
        }

        return null;
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        var native = 0u;
        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            native |= NativeMethods.MOD_ALT;
        }

        if ((modifiers & ModifierKeys.Control) != 0)
        {
            native |= NativeMethods.MOD_CONTROL;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            native |= NativeMethods.MOD_SHIFT;
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            native |= NativeMethods.MOD_WIN;
        }

        return native;
    }

    private void Attach(HwndSource source, bool ownsSource)
    {
        _source = source;
        _sourceDispatcher = source.Dispatcher;
        _hwnd = source.Handle;
        _ownsSource = ownsSource;
        source.AddHook(WndProc);
    }

    private void CreateSink()
    {
        var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
        if (dispatcher is not null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
        {
            CreateSinkOnCurrentThread();
            return;
        }

        // No usable Dispatcher on this thread: dedicate an STA message thread to the sink window.
        var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                CreateSinkOnCurrentThread();
                ready.Set();

                if (_source is not null)
                {
                    Dispatcher.Run();
                }
            }
            catch
            {
                ready.Set();
            }
        })
        {
            IsBackground = true,
            Name = "Custodian.HotkeySink",
        };

        try
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        catch
        {
            // Ignored: best effort.
        }

        thread.Start();
        ready.Wait(3000);

        if (_source is not null)
        {
            _sinkThread = thread;
        }
    }

    private void CreateSinkOnCurrentThread()
    {
        try
        {
            var parameters = new HwndSourceParameters("Custodian.HotkeySink")
            {
                ParentWindow = NativeMethods.HWND_MESSAGE,
                WindowStyle = 0,
                ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW,
                Width = 0,
                Height = 0,
            };

            Attach(new HwndSource(parameters), ownsSource: true);
        }
        catch
        {
            try
            {
                var parameters = new HwndSourceParameters("Custodian.HotkeySink")
                {
                    WindowStyle = NativeMethods.WS_POPUP,
                    ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW,
                    Width = 0,
                    Height = 0,
                };

                Attach(new HwndSource(parameters), ownsSource: true);
            }
            catch
            {
                // No sink: Register will simply report failure.
            }
        }
    }

    private void RunOnSinkThread(Action action)
    {
        var dispatcher = _sourceDispatcher;
        if (dispatcher is null)
        {
            return;
        }

        try
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
            {
                dispatcher.Invoke(action);
            }
        }
        catch
        {
            // Dispatcher gone: ignore.
        }
    }

    private void UnregisterCore(int id)
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, id);
        }

        _registrations.Remove(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            var known = false;
            lock (_gate)
            {
                known = _registrations.ContainsKey(id);
            }

            if (known)
            {
                handled = true;
                RaisePressed(id);
            }
        }

        return IntPtr.Zero;
    }

    private void RaisePressed(int id)
    {
        var handler = Pressed;
        if (handler is null)
        {
            return;
        }

        var args = new HotkeyPressedEventArgs(id);
        var context = _callerContext;
        if (context is null || Environment.CurrentManagedThreadId == _callerThreadId)
        {
            handler(this, args);
            return;
        }

        try
        {
            context.Post(_ => handler(this, args), null);
        }
        catch
        {
            handler(this, args);
        }
    }
}
