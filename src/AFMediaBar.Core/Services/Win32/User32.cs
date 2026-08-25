// Modified from https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC


using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AFMediaBar.Services.Win32Api;

public static class User32
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowRect(IntPtr hwnd, out RECT rc);

    [DllImport("user32.dll", EntryPoint = "FindWindow")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    private delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void GetClassName(IntPtr hwnd, StringBuilder sb, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder strText, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr handle, out int pid);

    private static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, 256);

        return sb.ToString();
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        var sb     = new StringBuilder(256);
        GetWindowText(hwnd, sb, length + 1);

        return sb.ToString();
    }

    public static bool GetWindowTitle(string match, out string text, out int pid)
    {
        var title     = string.Empty;
        var processId = 0;

        EnumWindows(delegate(IntPtr handle, int param)
                    {
                        var classname = GetClassName(handle);

                        if (match.Equals(classname) && GetWindowThreadProcessId(handle, out var xpid) != 0 && xpid != 0)
                        {
                            title     = GetWindowTitle(handle);
                            processId = xpid;
                        }

                        return true;
                    },
                    IntPtr.Zero);

        text = title;
        pid  = processId;

        return !string.IsNullOrEmpty(title) && pid > 0;
    }
}
