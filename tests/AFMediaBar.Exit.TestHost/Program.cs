// 独立子进程承载真实关窗和 ProcessExit 阻塞；不加载用户配置、播放器、任务栏或全局 Hook。
// 父测试拥有该进程及就绪事件；异常分支只终止自身，正常分支检验后台看门狗不阻止退出。
using System.Windows;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services.Startup;

namespace AFMediaBar.Exit.TestHost;

/// <summary>退出保护的隔离进程入口。</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 2) return 2;
        using var ready = EventWaitHandle.OpenExisting(args[1]);
        using var blocked = new ManualResetEventSlim(false);
        var exit = new ApplicationExitCoordinator(code =>
            NativeMethods.TerminateProcess(NativeMethods.GetCurrentProcess(), code), TimeSpan.FromSeconds(1));
        switch (args[0])
        {
            case "normal":
                exit.RequestExit();
                ready.Set();
                return 0;
            case "closing":
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var window = new Window { Width = 20, Height = 20, ShowInTaskbar = false };
                window.Closing += (_, _) => { ready.Set(); blocked.Wait(); };
                exit.ExitRequested += code => app.Shutdown(code);
                app.Startup += (_, _) => { window.Show(); exit.RequestExit(); };
                return app.Run();
            case "process-exit":
                AppDomain.CurrentDomain.ProcessExit += (_, _) => blocked.Wait();
                exit.ExitRequested += code => { ready.Set(); Environment.Exit(code); };
                exit.RequestExit();
                return 3;
            default:
                exit.Dispose();
                return 2;
        }
    }
}
