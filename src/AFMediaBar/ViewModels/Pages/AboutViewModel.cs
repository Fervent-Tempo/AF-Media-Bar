using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Credits;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Credits;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages
{
    /// <summary>
    /// 「赞助我」里的一份收款码，图片可能尚未放入安装包。
    /// One payment code under "support me"; its image may not have been packaged yet.
    /// </summary>
    public sealed class SupportEntryViewModel
    {
        /// <summary>创建一条赞助入口并尝试解析它的收款码图片。/ Creates one support entry and tries to resolve its payment-code image.</summary>
        /// <param name="entry">清单里的原始条目。/ The raw entry from the catalog.</param>
        public SupportEntryViewModel(SupportEntry entry)
        {
            Title = Translations.Get(entry.TitleKey);
            ExpectedAssetPath = entry.AssetPath;
            Image = TryLoadImage(entry.AssetPath);
        }

        /// <summary>标题（按当前语言解析）。/ Title, resolved in the active language.</summary>
        public string Title { get; }

        /// <summary>收款码在包内的预期路径，用于"还没放进来"的提示。/ The expected in-pack path of the payment code, shown while it has not been added yet.</summary>
        public string ExpectedAssetPath { get; }

        /// <summary>收款码图片；没有图片时为空。/ The payment-code image, null when there is none.</summary>
        public ImageSource? Image { get; }

        /// <summary>是否已经可以展示收款码。/ Whether the payment code can be shown.</summary>
        public bool IsReady => Image is not null;

        /// <summary>是否需要提示"待补充"。/ Whether a "to be supplied" note is needed.</summary>
        public bool IsPending => !IsReady;

        /// <summary>是否已经放入收款码图片。/ Whether the payment-code image has been added.</summary>
        public bool HasImage => Image is not null;

        /// <summary>
        /// 按候选名与候选扩展名依次尝试加载收款码。
        ///
        /// 依次尝试而不是只认一个文件名，是因为这两张图由用户自己命名：写死一个名字时，"图放进去了但界面不显示"没有任何提示，
        /// 而这是最让人困惑的一类问题。全部找不到时返回 null，界面据此显示应当放入的位置。
        /// Tries the candidate file names and extensions in order.
        ///
        /// The order exists because the user names these two files: with a single hard-coded name, "I put the image in but nothing shows" produces no
        /// hint at all, which is the most confusing kind of problem. When nothing is found this returns null and the interface shows where the file belongs.
        /// </summary>
        /// <param name="assetPath">清单里给出的首选路径。/ The preferred path from the catalog.</param>
        /// <returns>冻结的位图，或 null。/ A frozen bitmap, or null.</returns>
        private static ImageSource? TryLoadImage(string assetPath)
        {
            foreach (var candidate in EnumerateCandidates(assetPath))
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    // 只有真正存在时才去读；OnLoad + 立即冻结，界面因此不持有文件句柄，也不会在渲染线程上再次解码。
                    // The resource is only read when it really exists; OnLoad plus an immediate freeze means the interface holds no file
                    // handle and never decodes again on the render thread.
                    image.UriSource = new Uri($"pack://application:,,,/{candidate}", UriKind.Absolute);
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"[Credits] Support image candidate missing ({candidate}): {exception.Message}");
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateCandidates(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                yield break;
            }

            var directory = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? string.Empty;
            var baseName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (baseName.EndsWith("-pay", StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[..^"-pay".Length];
            }

            foreach (var extension in new[] { ".png", ".jpg", ".jpeg" })
            {
                foreach (var name in new[] { $"{baseName}-pay", baseName })
                {
                    yield return string.IsNullOrEmpty(directory) ? $"{name}{extension}" : $"{directory}/{name}{extension}";
                }
            }
        }
    }

    /// <summary>
    /// 关于页的视图模型：开发人员、赞助者、赞助入口与开源许可。
    ///
    /// 页面只做展示：许可清单来自 <see cref="OpenSourceLicenseCatalog"/>（手写但可核对），地址集中在 <see cref="CreditLinks"/>，
    /// 两份名单由 <see cref="CreditsService"/> 填进来，头像由 <see cref="AvatarImageLoader"/> 异步补上。
    /// View model for the about page: developers, sponsors, the support entry, and open-source licenses.
    ///
    /// The page only presents: the license catalog comes from <see cref="OpenSourceLicenseCatalog"/> (hand-written but checkable), addresses live in
    /// <see cref="CreditLinks"/>, the two name lists are filled in by <see cref="CreditsService"/>, and avatars are filled in asynchronously by
    /// <see cref="AvatarImageLoader"/>.
    /// </summary>
    public partial class AboutViewModel : ObservableObject
    {
        private readonly LocalizationService _localization;
        private readonly CreditsService _credits;
        private readonly AvatarImageLoader _avatars;
        private readonly Dispatcher _dispatcher = DispatcherHelper.Current;
        private CancellationTokenSource? _avatarCancellation;
        private IReadOnlyList<SponsorInfo> _sponsorEntries = [];
        private bool _sponsorsLoaded;

        /// <summary>名单的加载状态说明（正在取 / 取失败的原因）；没有可说的内容时为空。/ The load-status text for the lists — a fetch in flight, or the reason it failed — empty when there is nothing to say.</summary>
        [ObservableProperty]
        private string _creditsStatusText = string.Empty;

        /// <summary>是否正在取名单。/ Whether a fetch is in flight.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CreditsRefreshButtonText))]
        [NotifyCanExecuteChangedFor(nameof(RefreshCreditsCommand))]
        private bool _isCreditsLoading;

        /// <summary>在线更新失败，包括仍有本地名单可显示的情况。</summary>
        [ObservableProperty]
        private bool _hasCreditsFailure;

        /// <summary>赞助者名单的展示文本（名字连成一段）。/ The display text of the sponsor list: the names joined into one paragraph.</summary>
        [ObservableProperty]
        private string _sponsorNamesText = string.Empty;

        /// <summary>赞助者数量的说明文本。/ The text describing how many sponsors there are.</summary>
        [ObservableProperty]
        private string _sponsorCountText = string.Empty;

        /// <summary>
        /// 创建关于页视图模型并在构造时请求一次名单。
        ///
        /// 页面是 DI 单例、由首次导航构造，因此"在这页第一次被打开时才取"是自然结果；取数在后台线程，缓存有效时连网络都不碰。
        /// Creates the about view model and asks for the lists once while being constructed.
        ///
        /// The page is a DI singleton built on first navigation, so "fetch when this page is first opened" comes for free; the fetch runs on a
        /// background thread and touches no network at all while the cache is fresh.
        /// </summary>
        /// <param name="localization">界面语言：本页的名称与许可说明要跟着它换语言。/ Interface language, which the page's names and license notes follow.</param>
        /// <param name="credits">名单服务。/ The credits service.</param>
        /// <param name="avatars">头像加载器。/ The avatar loader.</param>
        public AboutViewModel(LocalizationService localization, CreditsService credits, AvatarImageLoader avatars)
        {
            _localization = localization;
            _credits = credits;
            _avatars = avatars;

            Licenses = OpenSourceLicenseCatalog.All;
            SupportEntries = new ObservableCollection<SupportEntryViewModel>(
                CreditLinks.SupportEntries.Select(entry => new SupportEntryViewModel(entry)));
            _localization.LanguageChanged += OnLanguageChanged;

            _credits.SnapshotChanged += OnCreditsSnapshotChanged;
            ApplyCreditsSnapshot(_credits.Current);
            _credits.RequestRefresh(isManual: false);
        }

        /// <summary>开发人员（GitHub 贡献者），每人一行：头像 + 用户名 + 提交次数。/ Developers, one row each: avatar, login, and contribution count.</summary>
        public ObservableCollection<ContributorViewModel> Contributors { get; } = [];

        /// <summary>开源许可清单（包 + 衍生代码）。这份清单不会变，因此是只读列表而不是可观察集合。/ The open-source license catalog: packages plus derived code. It never changes, so it is a read-only list rather than an observable collection.</summary>
        public IReadOnlyList<LicenseEntry> Licenses { get; }

        /// <summary>「赞助我」的条目：微信与支付宝收款码。/ The "support me" entries: WeChat and Alipay payment codes.</summary>
        public ObservableCollection<SupportEntryViewModel> SupportEntries { get; }

        /// <summary>是否已经拿到开发人员名单（决定该分组是否显示）。/ Whether the developer list has arrived, which decides if that group is shown.</summary>
        public bool HasContributors => Contributors.Count > 0;

        /// <summary>
        /// 是否显示赞助者分组。
        ///
        /// 判据是"这份名单取到过没有"，而不是"里面有没有名字"：没有赞助者时显示一行"暂无"，用户才知道这一节存在且确实是空的；
        /// 而"取不到"是另一回事，由状态说明条负责说出来（此前的实现把两种情况都藏成一个不存在的分组，于是"看不到赞助者名单"
        /// 既可能是没人赞助，也可能是名单没取到，用户无从分辨）。
        /// Whether the sponsor group is shown.
        ///
        /// The criterion is "has this list ever been loaded", not "does it contain any names": with no sponsors the group shows a "none yet" line, which tells the
        /// user the section exists and is genuinely empty, while "could not be loaded" is a different matter that the status callout states. The earlier
        /// implementation hid both cases behind a group that simply was not there, so "I cannot see the sponsor list" could mean either nobody had sponsored or the
        /// list had not loaded, and the user had no way to tell.
        /// </summary>
        public bool HasSponsors => _sponsorsLoaded;

        /// <summary>是否还没有任何赞助者（显示"暂无"而不是空白）。/ Whether there are no sponsors yet, which shows a "none yet" line instead of an empty area.</summary>
        public bool HasNoSponsorsYet => _sponsorsLoaded && _sponsorEntries.Count == 0;

        /// <summary>是否有任何赞助入口可以展示（收款码已放入，或链接已填）。/ Whether any support entry can be shown: its payment code is present, or its link is filled in.</summary>
        public bool HasSupportEntries => SupportEntries.Any(entry => entry.IsReady);

        /// <summary>是否有赞助入口还在等素材（用于显示"待补充"提示）。/ Whether any support entry is still waiting for its material, which shows the "to be supplied" note.</summary>
        public bool HasPendingSupportEntries => SupportEntries.Any(entry => entry.IsPending);

        /// <summary>是否有名单状态要说（正在取或取失败）。/ Whether there is any list status to show: a fetch in flight, or a failure.</summary>
        public bool HasCreditsStatus => IsCreditsLoading || HasCreditsFailure;

        /// <summary>没有开发人员数据时仍显示名单状态与重试入口。</summary>
        public bool HasDevelopersSection => HasContributors || HasCreditsStatus;

        public string CreditsRefreshButtonText => Translations.Get(IsCreditsLoading ? "Credits.Updating" : "Credits.Retry");

        /// <summary>仓库地址。/ The repository address.</summary>
        public string RepositoryUrl => CreditLinks.RepositoryDisplayUrl;

        /// <summary>议题地址。/ The issue tracker address.</summary>
        public string IssuesUrl => CreditLinks.IssuesUrl;

        /// <summary>
        /// 语言变化后重取由代码拼出的文案。
        ///
        /// 收款码标题、赞助者名单的连接符、提交次数说明都是构造时按当时语言取好的，只发通知不够；这里重建那一份内容，
        /// 再让 WPF 重读全部绑定。
        /// Re-reads the text composed in code after a language change.
        ///
        /// The payment-code titles, the sponsor separator, and the contribution-count text were taken in the language active at construction, so raising
        /// notifications alone is not enough: the content is rebuilt here and WPF then re-reads every binding.
        /// </summary>
        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            var rebuilt = CreditLinks.SupportEntries.Select(entry => new SupportEntryViewModel(entry)).ToList();
            SupportEntries.Clear();
            foreach (var entry in rebuilt)
            {
                SupportEntries.Add(entry);
            }

            RebuildContributors(_credits.Current.Contributors);
            ApplySponsorNames();

            OnPropertyChanged(string.Empty);
            OnPropertyChanged(nameof(HasSupportEntries));
            OnPropertyChanged(nameof(HasPendingSupportEntries));
        }

        /// <summary>用系统默认浏览器打开一个链接；地址为空时什么都不做。/ Opens a link in the default browser, doing nothing when the address is empty.</summary>
        /// <param name="url">链接地址。/ The link address.</param>
        [RelayCommand]
        private static void OpenUrl(string? url) => ExternalLinkLauncher.TryOpen(url);

        /// <summary>手动重取名单（忽略缓存有效期）。/ Fetches the lists again on request, ignoring the cache lifetime.</summary>
        [RelayCommand(CanExecute = nameof(CanRefreshCredits))]
        private void RefreshCredits() => _credits.RequestRefresh(isManual: true);

        private bool CanRefreshCredits() => !IsCreditsLoading;

        /// <summary>
        /// 名单快照变化：切回 UI 线程后按快照重建内容并更新状态文案。
        ///
        /// 服务在后台线程取数，而集合与属性都绑定在界面上；跨线程改 <c>ObservableCollection</c> 会让 WPF 直接抛异常，
        /// 因此统一走 <see cref="DispatcherHelper"/>（与其它页面同一份判定）。
        /// A snapshot change: back on the UI thread, the content is rebuilt from the snapshot and the status text is updated.
        ///
        /// The service fetches on a background thread while the collections and properties are bound to the interface, and mutating an
        /// <c>ObservableCollection</c> across threads makes WPF throw, so everything goes through <see cref="DispatcherHelper"/>, which is the same
        /// decision every other page uses.
        /// </summary>
        /// <param name="snapshot">新快照。/ The new snapshot.</param>
        private void OnCreditsSnapshotChanged(CreditsSnapshot snapshot) =>
            DispatcherHelper.Run(_dispatcher, () => ApplyCreditsSnapshot(snapshot));

        private void ApplyCreditsSnapshot(CreditsSnapshot snapshot)
        {
            RebuildContributors(snapshot.Contributors);
            _sponsorEntries = snapshot.Sponsors;
            _sponsorsLoaded = snapshot.SponsorsLoaded;
            ApplySponsorNames();

            IsCreditsLoading = snapshot.IsLoading;
            HasCreditsFailure = !snapshot.IsLoading && snapshot.FailureReason is not null;
            CreditsStatusText = snapshot.IsLoading
                ? Translations.Get("Credits.Status.Loading")
                : snapshot.FailureReason is not { } reason
                    ? string.Empty
                    : snapshot.IsPartial
                        // 一部分取到了、一部分没取到：这是最容易被静默吞掉的状态，必须说出来并给出重试。
                        // Part of the lists arrived and part did not: this is the state most easily swallowed silently, so it is stated and a retry is offered.
                        ? Translations.Get("Credits.Status.Partial")
                        : Translations.Format("Credits.Status.Failed", reason);

            OnPropertyChanged(nameof(HasContributors));
            OnPropertyChanged(nameof(HasSponsors));
            OnPropertyChanged(nameof(HasNoSponsorsYet));
            OnPropertyChanged(nameof(HasCreditsStatus));
            OnPropertyChanged(nameof(HasDevelopersSection));
        }

        /// <summary>
        /// 按快照重建开发人员列表，并为每人异步取头像。
        ///
        /// 重建会取消上一轮的取图：语言切换或重试都会走到这里，不取消就会让两轮请求同时写同一批条目。
        /// Rebuilds the developer list from the snapshot and fetches each avatar asynchronously.
        ///
        /// A rebuild cancels the previous round of image fetches: a language change or a retry both land here, and without the cancel two rounds would
        /// write into the same entries at once.
        /// </summary>
        /// <param name="contributors">贡献者。/ Contributors.</param>
        private void RebuildContributors(IReadOnlyList<ContributorInfo> contributors)
        {
            _avatarCancellation?.Cancel();
            _avatarCancellation?.Dispose();
            _avatarCancellation = null;

            Contributors.Clear();
            foreach (var contributor in contributors)
            {
                Contributors.Add(new ContributorViewModel(contributor));
            }

            if (Contributors.Count == 0)
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            _avatarCancellation = cancellation;
            var pending = Contributors.Where(contributor => !string.IsNullOrWhiteSpace(contributor.AvatarUrl)).ToArray();
            _ = FillAvatarsAsync(pending, cancellation.Token);
        }

        private async Task FillAvatarsAsync(IReadOnlyList<ContributorViewModel> pending, CancellationToken cancellationToken)
        {
            try
            {
                foreach (var contributor in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var image = await _avatars.LoadAsync(contributor.AvatarUrl, cancellationToken).ConfigureAwait(false);
                    if (image is null)
                    {
                        continue;
                    }

                    // 位图已冻结，可以跨线程传递，但属性通知必须落在 UI 线程上。
                    // The bitmap is frozen and may cross threads, while the property notification still has to land on the UI thread.
                    DispatcherHelper.Run(_dispatcher, () => contributor.ApplyAvatar(image));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                // 头像只是装饰：整批失败也只记一行，名单本身照常显示。
                // Avatars are decoration only: even a whole batch failing gets one debug line while the list itself keeps showing.
                Debug.WriteLine($"[Credits] Avatar pass failed: {exception.Message}");
            }
        }

        private void ApplySponsorNames()
        {
            SponsorNamesText = CreditsPresentationPolicy.BuildNameList(
                _sponsorEntries.Select(sponsor => sponsor.Name),
                Translations.Get("Credits.SponsorSeparator"));
            SponsorCountText = _sponsorEntries.Count > 0
                ? Translations.Format("Credits.Sponsors.Count", _sponsorEntries.Count)
                : string.Empty;
        }
    }
}
