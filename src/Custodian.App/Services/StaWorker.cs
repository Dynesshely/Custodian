using System.Windows.Threading;

namespace Custodian.App.Services;

/// <summary>
/// 专用的 STA 线程执行器。Shell（IShellLink / IShellItemImageFactory）是公寓线程对象，
/// 统一放在一个自己的 STA 线程上跑，避免污染 UI 线程的 COM 状态。
/// </summary>
public sealed class StaWorker : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Thread _thread;
    private volatile bool _disposed;

    public StaWorker()
    {
        var ready = new ManualResetEventSlim(false);
        Dispatcher? dispatcher = null;

        _thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Custodian.StaWorker",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();

        _dispatcher = dispatcher ?? throw new InvalidOperationException("STA worker 启动失败");
    }

    public Task<T> InvokeAsync<T>(Func<T> callback)
    {
        if (_disposed)
        {
            return Task.FromResult(default(T)!);
        }

        return _dispatcher.InvokeAsync(callback).Task;
    }

    public Task InvokeAsync(Action callback)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(callback).Task;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispatcher.InvokeShutdown();
    }
}
