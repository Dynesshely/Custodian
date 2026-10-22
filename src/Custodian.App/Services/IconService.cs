using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Custodian.Interop;

namespace Custodian.App.Services;

/// <summary>图标请求优先级：视口内优先于滚动预取（数值小的先出队）。</summary>
public enum IconPriority
{
    Viewport = 0,
    Prefetch = 1,
}

/// <summary>图标队列的统计快照（调试通道 <c>icons</c> 命令用）。</summary>
public readonly record struct IconServiceStats(
    int Requested,
    int CacheHits,
    int Deduped,
    int Queued,
    int Loaded,
    int Failed,
    int Delivered,
    int Pending,
    int QueueLength,
    int InFlight,
    int ViewportRequests,
    int PrefetchRequests,
    int Extractions,
    double ExtractMs,
    double FirstIconMs,
    double ElapsedMs,
    int FirstIconRequests);

/// <summary>
/// 带缓存和单线程泵的图标加载。
/// <list type="bullet">
/// <item><see cref="Request"/> 立即返回：缓存命中同步回调，未命中入队。</item>
/// <item>一条串行泵逐个 <c>await</c> <see cref="StaWorker"/> 抽取 —— Shell COM 只能在 STA 上串行跑。</item>
/// <item>队列按优先级出队（视口内先于预取）；同一路径重复请求合并回调，只抽一次。</item>
/// <item>回调统一切回 UI 线程，调用方直接改绑定属性即可。</item>
/// </list>
/// </summary>
public sealed class IconService
{
    public const int DefaultSize = 48;

    private readonly StaWorker _sta;
    private readonly Dispatcher _dispatcher;
    private readonly ConcurrentDictionary<string, ImageSource?> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly Dictionary<string, PendingIcon> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingIcon> _queue = new();

    private Task? _pump;

    // 统计（Interlocked，跨线程读写）
    private long _firstRequestAt;
    private long _firstIconAt;
    private long _requestedAtFirstIcon;
    private long _requested;
    private long _cacheHits;
    private long _deduped;
    private long _queued;
    private long _loaded;
    private long _failed;
    private long _delivered;
    private long _viewportRequests;
    private long _prefetchRequests;
    private long _extractions;
    private long _extractTicks;

    public IconService(StaWorker sta)
    {
        _sta = sta;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    /// <summary>已缓存条目数（含抽不到图标的失败结果）。</summary>
    public int CachedCount => _cache.Count;

    public static string CacheKey(string targetPath, int size) => size + "|" + targetPath;

    /// <summary>
    /// 请求一张图标。立即返回；<paramref name="onReady"/> 一定在 UI 线程上被调用一次（可能同步，也可能稍后）。
    /// </summary>
    public void Request(
        string targetPath,
        Action<ImageSource?> onReady,
        int size = DefaultSize,
        IconPriority priority = IconPriority.Viewport)
    {
        ArgumentNullException.ThrowIfNull(onReady);

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            Deliver(() => onReady(null));
            return;
        }

        var key = CacheKey(targetPath, size);
        Interlocked.Increment(ref _requested);
        Interlocked.CompareExchange(ref _firstRequestAt, Stopwatch.GetTimestamp(), 0);
        if (priority == IconPriority.Prefetch)
        {
            Interlocked.Increment(ref _prefetchRequests);
        }
        else
        {
            Interlocked.Increment(ref _viewportRequests);
        }

        if (_cache.TryGetValue(key, out var cached))
        {
            Interlocked.Increment(ref _cacheHits);
            Deliver(() => onReady(cached));
            return;
        }

        lock (_gate)
        {
            if (_pending.TryGetValue(key, out var running))
            {
                // 同一路径已在排队/抽取：挂上回调即可，顺带把优先级提到更高的一档
                Interlocked.Increment(ref _deduped);
                running.Callbacks.Add(onReady);
                if (priority < running.Priority)
                {
                    running.Priority = priority;
                }

                return;
            }

            var request = new PendingIcon(key, targetPath, size, priority);
            request.Callbacks.Add(onReady);
            _pending.Add(key, request);
            _queue.Add(request);
            Interlocked.Increment(ref _queued);
        }

        StartPump();
    }

    /// <summary>兼容旧的 await 风格调用（内部转成 Request + TCS）。</summary>
    public Task<ImageSource?> GetAsync(string targetPath, int size = DefaultSize)
    {
        var completion = new TaskCompletionSource<ImageSource?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Request(targetPath, icon => completion.TrySetResult(icon), size);
        return completion.Task;
    }

    /// <summary>清空统计；(可选)清缓存/丢弃排队中的请求，用于可重复的自动化测量。</summary>
    public void ResetStatistics(bool clearCache = false, bool dropQueue = false)
    {
        Interlocked.Exchange(ref _firstRequestAt, 0);
        Interlocked.Exchange(ref _firstIconAt, 0);
        Interlocked.Exchange(ref _requestedAtFirstIcon, 0);
        Interlocked.Exchange(ref _requested, 0);
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _deduped, 0);
        Interlocked.Exchange(ref _queued, 0);
        Interlocked.Exchange(ref _loaded, 0);
        Interlocked.Exchange(ref _failed, 0);
        Interlocked.Exchange(ref _delivered, 0);
        Interlocked.Exchange(ref _viewportRequests, 0);
        Interlocked.Exchange(ref _prefetchRequests, 0);
        Interlocked.Exchange(ref _extractions, 0);
        Interlocked.Exchange(ref _extractTicks, 0);

        if (clearCache)
        {
            _cache.Clear();
        }

        if (dropQueue)
        {
            lock (_gate)
            {
                foreach (var request in _queue)
                {
                    _pending.Remove(request.Key);
                }

                _queue.Clear();
            }
        }
    }

    public IconServiceStats Snapshot()
    {
        int pending;
        int queueLength;
        lock (_gate)
        {
            pending = _pending.Count;
            queueLength = _queue.Count;
        }

        var now = Stopwatch.GetTimestamp();
        var firstRequest = Interlocked.Read(ref _firstRequestAt);
        var firstIcon = Interlocked.Read(ref _firstIconAt);

        return new IconServiceStats(
            Requested: (int)Interlocked.Read(ref _requested),
            CacheHits: (int)Interlocked.Read(ref _cacheHits),
            Deduped: (int)Interlocked.Read(ref _deduped),
            Queued: (int)Interlocked.Read(ref _queued),
            Loaded: (int)Interlocked.Read(ref _loaded),
            Failed: (int)Interlocked.Read(ref _failed),
            Delivered: (int)Interlocked.Read(ref _delivered),
            Pending: pending,
            QueueLength: queueLength,
            InFlight: pending - queueLength,
            ViewportRequests: (int)Interlocked.Read(ref _viewportRequests),
            PrefetchRequests: (int)Interlocked.Read(ref _prefetchRequests),
            Extractions: (int)Interlocked.Read(ref _extractions),
            ExtractMs: ToMilliseconds(Interlocked.Read(ref _extractTicks)),
            FirstIconMs: firstRequest > 0 && firstIcon > 0 ? ToMilliseconds(firstIcon - firstRequest) : -1,
            ElapsedMs: firstRequest > 0 ? ToMilliseconds(now - firstRequest) : 0,
            FirstIconRequests: (int)Interlocked.Read(ref _requestedAtFirstIcon));
    }

    private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private void StartPump()
    {
        lock (_gate)
        {
            if (_pump is { IsCompleted: false })
            {
                return;
            }

            _pump = Task.Run(PumpAsync);
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            PendingIcon next;
            lock (_gate)
            {
                if (_queue.Count == 0)
                {
                    // 与 StartPump 共用同一把锁：这里置空后新请求一定会重新起泵
                    _pump = null;
                    return;
                }

                var index = 0;
                for (var i = 1; i < _queue.Count; i++)
                {
                    if (_queue[i].Priority < _queue[index].Priority)
                    {
                        index = i;
                    }
                }

                next = _queue[index];
                _queue.RemoveAt(index);
            }

            var started = Stopwatch.GetTimestamp();
            ImageSource? icon = null;
            try
            {
                icon = await _sta.InvokeAsync(() => IconLoader.Load(next.Path, next.Size)).ConfigureAwait(false);
            }
            catch
            {
                icon = null;
            }

            Interlocked.Add(ref _extractTicks, Stopwatch.GetTimestamp() - started);
            Interlocked.Increment(ref _extractions);

            // 失败也缓存：同一路径只抽一次，避免反复打 Shell
            _cache[next.Key] = icon;

            Action<ImageSource?>[] callbacks;
            lock (_gate)
            {
                _pending.Remove(next.Key);
                callbacks = next.Callbacks.ToArray();
            }

            if (icon is null)
            {
                Interlocked.Increment(ref _failed);
            }
            else
            {
                Interlocked.Increment(ref _loaded);
                Interlocked.CompareExchange(ref _firstIconAt, Stopwatch.GetTimestamp(), 0);
                // 记下「第一张图标出现时已请求了多少张」：验证首屏只请求了视口那一小撮
                Interlocked.CompareExchange(
                    ref _requestedAtFirstIcon,
                    Interlocked.Read(ref _requested),
                    0);
            }

            Interlocked.Add(ref _delivered, callbacks.Length);
            foreach (var callback in callbacks)
            {
                Deliver(() => callback(icon));
            }
        }
    }

    private void Deliver(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }

    private sealed class PendingIcon
    {
        public PendingIcon(string key, string path, int size, IconPriority priority)
        {
            Key = key;
            Path = path;
            Size = size;
            Priority = priority;
        }

        public string Key { get; }

        public string Path { get; }

        public int Size { get; }

        public IconPriority Priority { get; set; }

        public List<Action<ImageSource?>> Callbacks { get; } = new();
    }
}
