// 后台提取 Shell 启动项图标；单次读取拥有并释放 COM/HBITMAP，缓存仅保留冻结位图。
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services;

/// <summary>可读取未运行应用、快捷方式和 AppsFolder 图标的有界缓存。</summary>
public sealed class QuickLaunchIconService : IDisposable
{
    private const int MaximumIcons = 32;
    private readonly object _gate = new();
    private readonly Dictionary<string, (BitmapSource? Image, long RetryAt)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _order = new();
    private readonly SemaphoreSlim _reader = new(1, 1);
    private volatile bool _disposed;

    public async Task<BitmapSource?> GetAsync(QuickLaunchEntry entry, CancellationToken token)
    {
        var key = $"{entry.Kind}:{entry.Target}";
        await _reader.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (_disposed) return null;
            lock (_gate)
                if (_cache.TryGetValue(key, out var cached) && (cached.Image is not null || Environment.TickCount64 < cached.RetryAt))
                    return cached.Image;
            // Shell 提取不能取消；后台读取完成并释放原生对象后，调用者再按令牌淘汰结果。
            var image = await Task.Run(() => _disposed ? null : ReadIcon(entry), token).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_disposed)
                {
                    if (!_cache.ContainsKey(key))
                    {
                        while (_cache.Count >= MaximumIcons && _order.TryDequeue(out var oldest)) _cache.Remove(oldest);
                        _order.Enqueue(key);
                    }
                    _cache[key] = (image, Environment.TickCount64 + 30_000);
                }
            }
            token.ThrowIfCancellationRequested();
            return _disposed ? null : image;
        }
        finally { _reader.Release(); }
    }

    private static BitmapSource? ReadIcon(QuickLaunchEntry entry)
    {
        IShellItemImageFactory? factory = null;
        var bitmap = IntPtr.Zero;
        var apartment = CoInitializeEx(IntPtr.Zero, 0);
        try
        {
            if (apartment < 0 && apartment != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(apartment);
            var path = entry.Kind == QuickLaunchTargetKind.AppUserModelId ? @"shell:AppsFolder\" + entry.Target : entry.Target;
            var iid = typeof(IShellItemImageFactory).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory));
            Marshal.ThrowExceptionForHR(factory.GetImage(new NativeSize { Width = 128, Height = 128 }, 0x00000005, out bitmap));
            if (bitmap == IntPtr.Zero) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[QuickLaunchIcon] {exception.Message}");
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (factory is not null) Marshal.FinalReleaseComObject(factory);
            if (apartment >= 0) CoUninitialize();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_gate) { _cache.Clear(); _order.Clear(); }
        // 不在 UI 线程等待 Shell；在途读取自行释放原生资源并归还仅使用托管等待的信号量。
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width; public int Height; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr bitmap);
}
