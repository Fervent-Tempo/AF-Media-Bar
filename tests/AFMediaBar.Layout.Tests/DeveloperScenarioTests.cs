// 验证预览不消费生产通知额度、确认用途隔离和取消；外部通知、平台窗口与重启使用替身。
using System.Collections;
using System.Resources;
using System.Text.Json;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Diagnostics;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Notifications;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using AFMediaBar.ViewModels.Windows;
using AFMediaBar.Views.Dialogs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>固定动作目录、生产边界和工具会话生命周期回归。</summary>
[TestClass, DoNotParallelize]
public sealed class DeveloperScenarioTests
{
    [TestMethod]
    public void CommandsAcceptWhitespaceButNeverInterpretScriptsOrPartialCommands()
    {
        Assert.AreSame(DeveloperActionCatalog.Find("notify lyrics"), DeveloperActionCatalog.Find("  NOTIFY\tLyrics \r\n"));
        Assert.IsNull(DeveloperActionCatalog.Find("notify"));
        Assert.IsNull(DeveloperActionCatalog.Find("notify lyrics; app restart"));
        Assert.IsNull(DeveloperActionCatalog.Find("powershell Get-Process"));
        Assert.IsTrue(DeveloperActionCatalog.Suggest("webview fail").All(action => action.Command.StartsWith("webview fail ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task RepeatedPreviewsPreserveSettingsAndProductionDeduplication()
    {
        using var fixture = new Fixture();
        var before = JsonSerializer.Serialize(SettingsManager.Current);
        foreach (var action in DeveloperActionCatalog.Actions.Where(action => action.Impact == DeveloperActionImpact.Preview))
        {
            await fixture.Service.ExecuteAsync(action.Command, "host", CancellationToken.None);
            await fixture.Service.ExecuteAsync(action.Command, "host", CancellationToken.None);
        }
        using var production = new LyricsRecoveryNotificationCoordinator(fixture.Notifications, fixture.Confirmation, fixture.Restart, null);
        production.ReportDegraded(); production.ReportDegraded();
        Assert.AreEqual(1, fixture.Notifications.Targets.Count(target => target == ShellNotificationTarget.LyricsRecovery));
        Assert.AreEqual(2, fixture.Notifications.Targets.Count(target => target == ShellNotificationTarget.DeveloperLyricsPreview));
        Assert.AreEqual(0, fixture.Restart.Requests);
        Assert.AreEqual(before, JsonSerializer.Serialize(SettingsManager.Current));
    }

    [TestMethod]
    public async Task PreviewClickDoesNotRestartAndClosedToolsIgnoreOldClicks()
    {
        using var fixture = new Fixture();
        await fixture.Service.ExecuteAsync("notify lyrics", null, CancellationToken.None);
        fixture.Notifications.Click(ShellNotificationTarget.LyricsRecovery);
        Assert.AreEqual(0, fixture.Confirmation.Previews);
        fixture.Notifications.Click(ShellNotificationTarget.DeveloperLyricsPreview);
        Assert.AreEqual(1, fixture.Confirmation.Previews);
        Assert.AreEqual(0, fixture.Restart.Requests);
        fixture.Service.ClosePreviews();
        fixture.Notifications.Click(ShellNotificationTarget.DeveloperLyricsPreview);
        Assert.AreEqual(1, fixture.Confirmation.Previews);
        await fixture.Service.ExecuteAsync("notify lyrics", null, CancellationToken.None);
        fixture.Mode.SetEnabled(false);
        fixture.Notifications.Click(ShellNotificationTarget.DeveloperLyricsPreview);
        Assert.AreEqual(1, fixture.Confirmation.Previews);
    }

    [TestMethod]
    public async Task DisableDuringConfirmationCancelsAndRepeatedCommandsReturnBusy()
    {
        using var fixture = new Fixture();
        var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Confirmation.RestartChoice = () => choice.Task;
        var request = fixture.Service.ExecuteAsync("app restart", null, CancellationToken.None);
        Assert.AreEqual(DeveloperActionStatus.Busy, (await fixture.Service.ExecuteAsync("app restart", null, CancellationToken.None)).Status);
        fixture.Mode.SetEnabled(false);
        choice.SetResult(true);
        Assert.AreEqual(DeveloperActionStatus.Canceled, (await request).Status);
        Assert.AreEqual(0, fixture.Restart.Requests);
        Assert.AreEqual(DeveloperActionStatus.Canceled, (await fixture.Service.ExecuteAsync("notify lyrics", null, CancellationToken.None)).Status);
    }

    [TestMethod]
    public async Task RealRestartRequiresConfirmationAndPreviewActivityRejectsIt()
    {
        using var fixture = new Fixture();
        fixture.Confirmation.IsPreviewActive = true;
        Assert.AreEqual(DeveloperActionStatus.Busy, (await fixture.Service.ExecuteAsync("app restart", null, CancellationToken.None)).Status);
        fixture.Confirmation.IsPreviewActive = false;
        fixture.Confirmation.RestartChoice = () => Task.FromResult(false);
        Assert.AreEqual(DeveloperActionStatus.Canceled, (await fixture.Service.ExecuteAsync("app restart", null, CancellationToken.None)).Status);
        Assert.AreEqual(0, fixture.Restart.Requests);
        fixture.Confirmation.RestartChoice = () => Task.FromResult(true);
        Assert.AreEqual(DeveloperActionStatus.Accepted, (await fixture.Service.ExecuteAsync("app restart", null, CancellationToken.None)).Status);
        Assert.AreEqual(1, fixture.Restart.Requests);
    }

    [TestMethod]
    public async Task SelectedHostIsRecheckedAfterDisableConfirmation()
    {
        using var fixture = new Fixture();
        var choice = new TaskCompletionSource<DeveloperConfirmationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Confirmation.DisableChoice = () => choice.Task;
        var request = fixture.Service.ExecuteAsync("webview disable", "host", CancellationToken.None);
        fixture.Host.HostId = "replacement";
        choice.SetResult(DeveloperConfirmationResult.Confirmed);
        Assert.AreEqual(DeveloperActionStatus.Unavailable, (await request).Status);
        Assert.AreEqual(0, fixture.Host.Applied.Count);
        Assert.AreEqual(DeveloperActionStatus.Unavailable, (await fixture.Service.ExecuteAsync("webview rebuild", "host", CancellationToken.None)).Status);
    }

    [TestMethod]
    public void ActualConfirmationChannelsDoNotMergeAcrossPurposes()
    {
        StaTest.Run(async _ =>
        {
            using var confirmation = new RestartConfirmationService(null!);
            using var cancellation = new CancellationTokenSource();
            var production = confirmation.ConfirmAsync("Restart.Failed", cancellation.Token);
            Assert.AreEqual(DeveloperConfirmationResult.Busy, await confirmation.ShowPreviewAsync(CancellationToken.None));
            cancellation.Cancel();
            Assert.IsFalse(await production);
            using var previewCancellation = new CancellationTokenSource();
            var preview = confirmation.ShowPreviewAsync(previewCancellation.Token);
            Assert.AreSame(preview, confirmation.ShowPreviewAsync(CancellationToken.None));
            Assert.IsFalse(await confirmation.ConfirmAsync("Restart.Failed"));
            Assert.AreEqual(DeveloperConfirmationResult.Busy, await confirmation.ConfirmLyricsDisableAsync(CancellationToken.None));
            previewCancellation.Cancel();
            Assert.AreEqual(DeveloperConfirmationResult.Declined, await preview);
        });
    }

    [TestMethod]
    public void ClosedViewModelDiscardsLateResultsFromItsOwnRequest()
    {
        StaTest.Run(async _ =>
        {
            using var fixture = new Fixture();
            using var localization = new LocalizationService();
            var scenarios = new DelayedScenarios();
            using var viewModel = new DeveloperToolsViewModel(fixture.Mode, scenarios, localization);
            var item = viewModel.Actions.Single(item => item.Command == "notify lyrics");
            viewModel.CommandText = "notify lyrics";
            var request = viewModel.ExecuteCommandCommand.ExecuteAsync(null);
            Assert.AreEqual(item.Action.Command, scenarios.LastCommand);
            viewModel.Dispose();
            scenarios.Result.SetResult(new(DeveloperActionStatus.Completed, "late"));
            await request;
            Assert.AreEqual(string.Empty, viewModel.Output);
            Assert.IsTrue(scenarios.Token.IsCancellationRequested);
            Assert.AreEqual(1, scenarios.CloseCount);
        });
    }

    [TestMethod]
    public void EveryDeveloperStringExistsWithoutLanguageFallback()
    {
        var assembly = typeof(Translations).Assembly;
        HashSet<string>? reference = null;
        foreach (var suffix in new[] { "StringsZhHans.resources", "StringsZhHant.resources", "StringsEn.resources", "StringsVi.resources" })
        {
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal)))!;
            using var reader = new ResourceReader(stream);
            var values = reader.Cast<DictionaryEntry>().Where(entry => ((string)entry.Key).StartsWith("Developer.", StringComparison.Ordinal))
                .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
            Assert.IsTrue(values.Values.All(value => !string.IsNullOrWhiteSpace(value)));
            reference ??= values.Keys.ToHashSet();
            CollectionAssert.AreEquivalent(reference.ToArray(), values.Keys.ToArray());
            foreach (var action in DeveloperActionCatalog.Actions)
            {
                Assert.IsTrue(values.ContainsKey("Developer.Action." + action.ResourceId));
                Assert.IsTrue(values.ContainsKey("Developer.Description." + action.ResourceId));
            }
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly AppSettings _previous = SettingsManager.Current;
        public DeveloperModeService Mode { get; }
        public Host Host { get; } = new();
        public Notifications Notifications { get; } = new();
        public Confirmation Confirmation { get; } = new();
        public Restart Restart { get; } = new();
        public DeveloperScenarioService Service { get; }
        public Fixture()
        {
            SettingsManager.Current = new AppSettings { DeveloperModeEnabled = true };
            Mode = new();
            Service = new(Mode, () => Host, Notifications, Confirmation, Confirmation, Restart, null!, null!, null);
        }
        public void Dispose() { Service.Dispose(); Mode.Dispose(); SettingsManager.Current = _previous; }
    }

    private sealed class Host : IDeveloperHostActions
    {
        public string HostId = "host";
        public List<string> Applied { get; } = [];
        public IReadOnlyList<DeveloperLyricsHostState> GetLyricsHosts() => [new(HostId, "display", true, true, true, false, 1, 0, 0, false, "runtime")];
        public DeveloperActionResult ExecuteLyricsAction(string command, string? hostId)
        {
            if (hostId != HostId) return new(DeveloperActionStatus.Unavailable);
            Applied.Add(command); return new(DeveloperActionStatus.Queued);
        }
        public DeveloperActionResult ReloadTaskbar() => new(DeveloperActionStatus.Queued);
        public DeveloperActionResult ShowTrackPreview() => new(DeveloperActionStatus.Accepted);
        public string CaptureState() => "state";
        public void ClosePreviews() { }
    }

    private sealed class Notifications : ISystemNotificationService
    {
        public List<ShellNotificationTarget> Targets { get; } = [];
        public event Action<ShellNotificationTarget>? NotificationClicked;
        public bool TryShowNotification(string title, string message, ShellNotificationTarget target = ShellNotificationTarget.None)
        { Targets.Add(target); return true; }
        public void Click(ShellNotificationTarget target) => NotificationClicked?.Invoke(target);
    }

    private sealed class Confirmation : IDeveloperConfirmationService, IRestartConfirmationService
    {
        public bool IsPreviewActive { get; set; }
        public int Previews;
        public Func<Task<bool>> RestartChoice = () => Task.FromResult(true);
        public Func<Task<DeveloperConfirmationResult>> DisableChoice = () => Task.FromResult(DeveloperConfirmationResult.Confirmed);
        public Task<bool> ConfirmAsync(string reasonResourceKey, CancellationToken cancellationToken = default) => RestartChoice();
        public Task<DeveloperConfirmationResult> ShowPreviewAsync(CancellationToken cancellationToken)
        { Previews++; return Task.FromResult(DeveloperConfirmationResult.Confirmed); }
        public Task<DeveloperConfirmationResult> ConfirmLyricsDisableAsync(CancellationToken cancellationToken) => DisableChoice();
    }

    private sealed class Restart : IApplicationRestartService
    {
        public int Requests;
        public event EventHandler? RestartRequested { add { } remove { } }
        public Task<ApplicationRestartResult> RequestRestartAsync(CancellationToken cancellationToken = default)
        { Requests++; return Task.FromResult(ApplicationRestartResult.Requested); }
    }

    private sealed class DelayedScenarios : IDeveloperScenarioService
    {
        public readonly TaskCompletionSource<DeveloperActionResult> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? LastCommand;
        public CancellationToken Token;
        public int CloseCount;
        public event Action<string, DeveloperActionResult>? ResultObserved { add { } remove { } }
        public IReadOnlyList<DeveloperLyricsHostState> GetLyricsHosts() => [];
        public Task<DeveloperActionResult> ExecuteAsync(string command, string? hostId, CancellationToken cancellationToken)
        { LastCommand = command; Token = cancellationToken; return Result.Task; }
        public void ClosePreviews() => CloseCount++;
    }
}
