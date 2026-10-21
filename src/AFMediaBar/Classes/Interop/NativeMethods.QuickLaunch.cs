// 双击时间由系统维护；声明不持有原生资源。
using System.Runtime.InteropServices;

namespace AFMediaBar.Classes.Interop;

public static partial class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern uint GetDoubleClickTime();
}
