using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Interop;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 在后台批量读取桌面像素并稀疏取色；容器拥有可复用的 GDI 表面，释放时不等待正在执行的采样。
/// </summary>
public sealed class ScreenBackgroundSampler : IDisposable
{
    private const int HorizontalSamples = 11;
    private const int VerticalSamples = 5;
    private const long MaximumBufferBytes = 64 * 1024 * 1024;
    private readonly object _captureGate = new();
    private nint _memoryDc;
    private nint _bitmap;
    private nint _originalBitmap;
    private nint _bits;
    private int _width;
    private int _height;
    private int _disposed;

    /// <summary>异步采样指定物理像素矩形。/ Asynchronously samples the specified physical-pixel rectangle.</summary>
    public Task<IReadOnlyList<Color>> SampleAsync(Int32Rect bounds, CancellationToken cancellationToken) =>
        // 取消是采样会话的正常代际切换，不应通过 Task.Run 的取消重载制造
        // OperationCanceledException 首次机会异常；采样循环会返回已收集的结果，
        // 上层再按代际丢弃它。
        // Cancellation is a normal generation switch, not an exceptional sampling failure.
        // Avoid Task.Run's cancellation overload and let the loop return partial samples;
        // the owning session discards stale generations afterwards.
        Task.Run(() => Sample(bounds, cancellationToken));

    /// <summary>在同一个后台请求中采集独立区域，不读取它们之间的组件。</summary>
    internal Task<IReadOnlyList<Color>> SampleAsync(IReadOnlyList<Int32Rect> regions, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<Color>>(() =>
    {
        var colors = new List<Color>();
        foreach (var region in regions)
        {
            if (cancellationToken.IsCancellationRequested) break;
            colors.AddRange(Sample(region, cancellationToken));
        }
        return colors;
    });

    private IReadOnlyList<Color> Sample(Int32Rect requestedBounds, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0 || cancellationToken.IsCancellationRequested ||
            requestedBounds.Width <= 0 || requestedBounds.Height <= 0)
            return [];

        var virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var virtualTop = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var virtualWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var virtualHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (virtualWidth <= 0 || virtualHeight <= 0)
            return [];

        var left = Math.Max(requestedBounds.X, virtualLeft);
        var top = Math.Max(requestedBounds.Y, virtualTop);
        var right = Math.Min((long)requestedBounds.X + requestedBounds.Width, (long)virtualLeft + virtualWidth);
        var bottom = Math.Min((long)requestedBounds.Y + requestedBounds.Height, (long)virtualTop + virtualHeight);
        if (right <= left || bottom <= top || (right - left) * (bottom - top) * 4 > MaximumBufferBytes)
            return [];

        // 多个宿主共用一个有界缓冲；只有后台采样线程等待此锁。
        lock (_captureGate)
        {
            try
            {
                if (Volatile.Read(ref _disposed) != 0 || cancellationToken.IsCancellationRequested)
                    return [];
                var screenDc = NativeMethods.GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero)
                    return [];
                try
                {
                    var width = checked((int)(right - left));
                    var height = checked((int)(bottom - top));
                    if (!EnsureBuffer(screenDc, width, height) || cancellationToken.IsCancellationRequested ||
                        !NativeMethods.BitBlt(_memoryDc, 0, 0, width, height, screenDc, left, top,
                            NativeMethods.ScreenCopyRasterOperation) || !NativeMethods.GdiFlush())
                        return [];
                    var colors = new List<Color>(HorizontalSamples * VerticalSamples);
                    for (var row = 0; row < VerticalSamples; row++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return colors;
                        var y = (int)(((2L * row + 1) * height) / (2L * VerticalSamples));
                        for (var column = 0; column < HorizontalSamples; column++)
                        {
                            var x = (int)(((2L * column + 1) * width) / (2L * HorizontalSamples));
                            var pixel = Marshal.ReadInt32(_bits, checked((y * width + x) * 4));
                            colors.Add(Color.FromRgb((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel));
                        }
                    }
                    return colors;
                }
                finally
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
            finally
            {
                if (Volatile.Read(ref _disposed) != 0) ReleaseBuffer();
            }
        }
    }

    private bool EnsureBuffer(nint screenDc, int width, int height)
    {
        if (_bitmap != 0 && _width == width && _height == height) return true;
        ReleaseBuffer();
        _memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        if (_memoryDc == 0) return false;
        var info = new NativeMethods.ScreenBitmapInfo
        {
            Size = 40,
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32
        };
        _bitmap = NativeMethods.CreateDIBSection(screenDc, in info, 0, out _bits, 0, 0);
        if (_bitmap == 0 || _bits == 0) { ReleaseBuffer(); return false; }
        _originalBitmap = NativeMethods.SelectObject(_memoryDc, _bitmap);
        if (_originalBitmap == 0 || _originalBitmap == new nint(-1))
        {
            _originalBitmap = 0;
            ReleaseBuffer();
            return false;
        }
        _width = width;
        _height = height;
        return true;
    }

    private void ReleaseBuffer()
    {
        if (_originalBitmap != 0) NativeMethods.SelectObject(_memoryDc, _originalBitmap);
        if (_bitmap != 0) NativeMethods.DeleteObject(_bitmap);
        if (_memoryDc != 0) NativeMethods.DeleteDC(_memoryDc);
        _originalBitmap = _bitmap = _bits = _memoryDc = 0;
        _width = _height = 0;
    }

    /// <summary>释放缓存的 GDI 表面；不在 UI 线程等待正在进行的屏幕回读。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (!Monitor.TryEnter(_captureGate)) return;
        try { ReleaseBuffer(); }
        finally { Monitor.Exit(_captureGate); }
    }
}
