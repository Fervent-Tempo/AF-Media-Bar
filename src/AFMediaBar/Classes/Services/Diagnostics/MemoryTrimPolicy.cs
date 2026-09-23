namespace AFMediaBar.Classes.Services;

/// <summary>
/// 触发一次"按需回收"的原因。与档位无关：档位是"系统状态决定该不该省内存"，而这里回答的是"某件事刚做完，现在值得收一遍吗"。
/// Why an on-demand reclaim was requested. This is independent of the prune levels: a level answers "does the system state allow saving memory",
/// while these triggers answer "something just finished, is a reclaim worth it now".
/// </summary>
public enum MemoryTrimTrigger
{
    /// <summary>进入空闲档：没有媒体、用户也已经离开，值得收一遍托管垃圾。/ The idle level was entered: no media and the user is gone, so collecting managed garbage is worth it.</summary>
    IdleLevelEntered = 0,

    /// <summary>设置窗口关闭：它是最大的界面树与缓存持有者，关掉之后回收最划算。/ The settings window closed: it holds the largest visual tree and the most caches, so a reclaim pays off most there.</summary>
    SettingsWindowClosed = 1,

    /// <summary>浮层关闭：完整层、曲目通知等按需窗口，关闭后释放它们持有的封面临时位图。/ A panel closed: on-demand windows such as the full panel or the track notification, whose temporary artwork bitmaps are released afterwards.</summary>
    PanelClosed = 2,

    /// <summary>用户在设置页明确点击"立即压缩物理内存"。/ The user explicitly clicked "compress physical memory now" on the settings page.</summary>
    ManualRequest = 3,

    /// <summary>
    /// 启动已经安定下来：启动时读进来的一大批页面（JIT 后的代码、WPF 与 WinRT 元数据、一次性初始化）此后大概率不会再被碰到，
    /// 因此这是把它们还回去最划算的时机。
    /// Startup has settled: the large batch of pages read during startup — JITed code, WPF and WinRT metadata, one-time initialization — is unlikely to be
    /// touched again, which makes this the most rewarding moment to hand them back.
    /// </summary>
    StartupSettled = 4,

    /// <summary>
    /// 自动隐藏任务栏保持收起一段时间：只回收已经失去引用的托管对象，不剥离工作集，避免下一次从屏幕边缘呼出时发生硬缺页。
    /// The auto-hidden taskbar stayed collapsed for a while: collect only managed objects that are already unreachable and leave the working set alone,
    /// avoiding hard page faults on the next edge reveal.
    /// </summary>
    TaskbarHidden = 5
}

/// <summary>
/// 回收强度。两档的差别只在"要不要动工作集"，不动的是托管垃圾的回收。
/// How deep a reclaim goes. The two strengths differ only in whether the working set is touched; collecting managed garbage happens either way.
/// </summary>
public enum MemoryTrimStrength
{
    /// <summary>
    /// 温和：后台 GC + 等待终结器，**不碰工作集**。用在"刚关掉一个窗口，但用户随时可能再打开"的场合，
    /// 因为剥离工作集换来的是下一次交互时的硬缺页。
    /// Gentle: a background GC plus a finalizer drain, leaving the working set alone. Used where a window has just closed but the user may reopen it at
    /// any moment, because returning the working set costs a hard page fault on the next interaction.
    /// </summary>
    Gentle = 0,

    /// <summary>
    /// 深度：压缩 GC ×2 + 交还工作集。用在"短时间不会再回来"的场合（设置窗口关闭、用户手动点击）。
    /// Deep: a compacting GC twice over plus returning the working set. Used where the process is unlikely to be needed again right away, such as after
    /// the settings window closes or when the user asks for it.
    /// </summary>
    Deep = 1
}

/// <summary>
/// 按需回收的判定：什么原因该用多大强度，以及多久内只做一次。
/// The on-demand reclaim decision: which strength a reason deserves, and how often it may run.
///
/// 判定刻意留成纯逻辑（无 I/O、无计时器），因为"两次回收之间的最短间隔"和"手动请求可以绕过间隔"是两条会被改坏的规则，
/// 它们必须能被单元测试直接钉住。
/// The decision stays pure logic — no I/O, no timers — because "the shortest interval between two reclaims" and "a manual request may bypass that
/// interval" are exactly the rules that get broken by accident, so they have to be pinned down by unit tests.
/// </summary>
public static class MemoryTrimPolicy
{
    /// <summary>
    /// 两次非手动回收之间的最短间隔。5 秒来自对照项目（StarPie 的 `MemoryOptimizer`）的实测取值：界面连续开关时，
    /// 每次关闭都做一遍 GC 只会白白吃掉 CPU，而 5 秒已足够覆盖"关掉一个窗口再打开另一个"的节奏。
    /// The shortest interval between two non-manual reclaims. Five seconds comes from the behaviour measured in the reference project (StarPie's
    /// `MemoryOptimizer`): while panels are opened and closed in a row, collecting on every close only burns CPU, and five seconds already covers the
    /// pace of closing one window and opening another.
    /// </summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 启动后最早可以考虑回收的时间。启动阶段读进来的页面此刻多半还被启动链自己用着（任务栏停靠、DPI 探测、首个媒体快照、
    /// 主题与语言资源），过早回收等于刚还回去就立刻缺页读回来，既白做又拖慢启动。
    /// The earliest moment a reclaim may be considered after startup. The pages read during startup are still in use by the startup chain at that point —
    /// taskbar docking, DPI probing, the first media snapshot, theme and language resources — so reclaiming too early means paging them straight back in,
    /// which is wasted work that also slows startup down.
    /// </summary>
    public static readonly TimeSpan StartupMinimumDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 启动后等待"用户有一会儿没操作"的时间。回收本身只是一次 GC 加交还工作集，而**缺页发生在用户下一次碰到这些页面的那一刻**，
    /// 因此挑一个用户此刻没在拖窗口、没在滚轮盘的瞬间，就是把可感知卡顿压到最低的做法。
    /// How long the user has to have been idle before the startup reclaim runs. The reclaim itself is just a collection plus returning the working set, while the
    /// page faults happen **the next time the user touches those pages**, so choosing a moment when the user is not dragging the bar or spinning the wheel is
    /// what keeps any perceptible stutter at its lowest.
    /// </summary>
    public static readonly TimeSpan StartupUserIdleThreshold = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 启动后最迟执行的时间。用户可能一直在动鼠标（那就永远等不到空闲），而启动时读进来的页面早就冷掉了；
    /// 到这个时间点就执行一次，既不无限期拖延，也不会在启动链还在跑的时候动手。
    /// The latest moment the startup reclaim runs. The user may keep moving the mouse, in which case an idle moment never arrives, while the pages read during
    /// startup have long gone cold; running once at this point neither postpones it forever nor fires while the startup chain is still working.
    /// </summary>
    public static readonly TimeSpan StartupDeadline = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 这一次请求该用多大强度。
    /// Which strength this request deserves.
    /// </summary>
    /// <param name="trigger">触发原因。/ The trigger.</param>
    /// <returns>回收强度。/ The reclaim strength.</returns>
    public static MemoryTrimStrength ResolveStrength(MemoryTrimTrigger trigger) => trigger switch
    {
        // 设置窗口是最大的界面树与缓存持有者，而且关掉之后用户短期内不会一直开关它；手动请求理所当然按最深的来。
        // The settings window holds the largest visual tree and the most caches, and it is not something the user opens and closes in a loop; a manual
        // request obviously goes as deep as it can.
        MemoryTrimTrigger.SettingsWindowClosed => MemoryTrimStrength.Deep,
        MemoryTrimTrigger.ManualRequest => MemoryTrimStrength.Deep,

        // 启动后的那一次也按最深来：它的全部意义就是把"启动时读进来、之后不会再碰"的冷页面交还系统；只收垃圾等于什么都没做
        // （工作集读数不会动）。用户已经亲自验证过：手动点一次之后读数明显下降，而且没有可感知卡顿。
        // The post-startup reclaim goes as deep as it can as well: its entire point is handing back the cold pages read during startup that will not be touched
        // again, and collecting garbage alone would achieve nothing (the working-set figure would not move). The user has verified this first-hand: one manual
        // click dropped the figure clearly, with no perceptible stutter.
        MemoryTrimTrigger.StartupSettled => MemoryTrimStrength.Deep,

        // 空闲档与浮层关闭都只收垃圾：空闲档的"省内存"由档位本身（清缓存、降频）负责，工作集交给显示器关闭/睡眠档，
        // 浮层则随时可能被再打开。
        // The idle level and a panel closing only collect garbage: at the idle level the saving comes from the level itself — dropping caches and slowing
        // timers — while the working set is left to the display-off and suspend levels, and a panel may be reopened at any moment.
        _ => MemoryTrimStrength.Gentle
    };

    /// <summary>判断一次请求是否来自用户的明确动作（这类请求不受最小间隔限制）。/ Whether a request comes from an explicit user action, which the minimum interval does not hold back.</summary>
    /// <param name="trigger">触发原因。/ The trigger.</param>
    /// <returns>是否为手动请求。/ Whether it is a manual request.</returns>
    public static bool IsManual(MemoryTrimTrigger trigger) => trigger == MemoryTrimTrigger.ManualRequest;

    /// <summary>
    /// 判断现在是否该执行这次回收。
    /// Decides whether this reclaim should run now.
    /// </summary>
    /// <param name="trigger">触发原因。/ The trigger.</param>
    /// <param name="lastTrimUtc">上一次回收的时间；从未回收过时传 <see langword="default"/>。
    /// When the last reclaim ran, or <see langword="default"/> if there has never been one.</param>
    /// <param name="nowUtc">现在的时间。/ The current time.</param>
    /// <returns>该执行为 true。/ True when it should run.</returns>
    public static bool ShouldTrim(MemoryTrimTrigger trigger, DateTime lastTrimUtc, DateTime nowUtc)
    {
        // 手动请求永远执行：用户点了按钮就要有反应，被 5 秒节流吞掉比多跑一次 GC 更糟。
        // A manual request always runs: clicking the button has to do something, and being swallowed by the five-second throttle is worse than one extra
        // collection.
        if (IsManual(trigger))
        {
            return true;
        }

        if (lastTrimUtc == default)
        {
            return true;
        }

        return nowUtc - lastTrimUtc >= MinimumInterval;
    }

    /// <summary>
    /// 判断"启动后那一次回收"现在该不该执行。
    /// Decides whether the post-startup reclaim should run now.
    ///
    /// 三条判据缺一不可：启动已经过去足够久（启动链不再需要那些页面）、用户此刻没有在操作（缺页要等用户下一次碰到页面才会发生，
    /// 所以要在用户松手的时候还）、以及档位仍在常规（已经进入空闲或更深的档位时，档位路径刚做过更彻底的事，重复回收只是白跑）。
    /// Three conditions, all of which have to hold: enough time has passed since startup (the startup chain no longer needs those pages), the user is not
    /// interacting at this moment (page faults only happen the next time the user touches a page, so the hand-back belongs in a moment when the user has let
    /// go), and the level is still normal (once idle or a deeper level applies, the level path has just done something more thorough, so a second reclaim is
    /// pure waste).
    ///
    /// 到 <see cref="StartupDeadline"/> 之后即使条件不满足也执行：用户可能一直在动鼠标，而启动时读进来的页面早就冷了。
    /// Past <see cref="StartupDeadline"/> it runs even when the conditions do not hold, because the user may keep moving the mouse while the pages read during
    /// startup have long gone cold.
    /// </summary>
    /// <param name="sinceStart">自启动以来经过的时间。/ Time elapsed since startup.</param>
    /// <param name="userIdle">用户空闲时长。/ How long the user has been idle.</param>
    /// <param name="isLevelNormal">当前档位是否为常规（L0）。/ Whether the current level is normal (L0).</param>
    /// <returns>该执行为 true。/ True when it should run.</returns>
    public static bool ShouldTrimAfterStartup(TimeSpan sinceStart, TimeSpan userIdle, bool isLevelNormal)
    {
        if (sinceStart < StartupMinimumDelay)
        {
            return false;
        }

        if (!isLevelNormal)
        {
            return false;
        }

        return sinceStart >= StartupDeadline || userIdle >= StartupUserIdleThreshold;
    }
}
