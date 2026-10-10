// 用通知和重启边界替身验证去重、补试和用户确认；不发送真实通知或重启应用。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Notifications;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Resources;
using System.Collections;
using System.Resources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>歌词故障通知和确认重启的回归验证。</summary>
[TestClass]
public sealed class LyricsRecoveryNotificationTests
{
    [TestMethod]
    public void MultipleHostsShareOneNoticeAndOnlyOneShellRetry()
    {
        var notifications = new Notifications();
        var confirmation = new Confirmation(() => Task.FromResult(false));
        var restart = new Restart();
        using var coordinator = new LyricsRecoveryNotificationCoordinator(notifications, confirmation, restart, null);
        coordinator.ReportDegraded(); coordinator.ReportDegraded();
        Assert.AreEqual(1, notifications.Targets.Count);
        Assert.AreEqual(ShellNotificationTarget.LyricsRecovery, notifications.Targets[0]);
        notifications.Accept = true;
        coordinator.OnShellRestored(); coordinator.OnShellRestored(); coordinator.ReportDegraded();
        Assert.AreEqual(2, notifications.Targets.Count);
        var rejectedNotifications = new Notifications();
        using var rejected = new LyricsRecoveryNotificationCoordinator(rejectedNotifications, confirmation, restart, null);
        rejected.ReportDegraded(); rejected.OnShellRestored(); rejected.OnShellRestored();
        Assert.AreEqual(2, rejectedNotifications.Targets.Count);
        coordinator.Dispose(); coordinator.ReportDegraded(); coordinator.OnShellRestored();
        Assert.AreEqual(2, notifications.Targets.Count);
    }

    [TestMethod]
    public async Task CancelingConfirmationDoesNotRestartAndDisposeRejectsLateClicks()
    {
        var notifications = new Notifications { Accept = true };
        var confirmation = new Confirmation(() => Task.FromResult(false));
        var restart = new Restart();
        using var coordinator = new LyricsRecoveryNotificationCoordinator(notifications, confirmation, restart, null);
        Assert.AreEqual(ApplicationRestartResult.Canceled, await coordinator.RequestConfirmedRestartAsync());
        Assert.AreEqual(0, restart.Requests);
        notifications.Click(ShellNotificationTarget.Application);
        Assert.AreEqual(1, confirmation.Requests);
        coordinator.Dispose(); notifications.Click(ShellNotificationTarget.LyricsRecovery);
        Assert.AreEqual(1, confirmation.Requests);
        Assert.AreEqual(ApplicationRestartResult.Canceled, await coordinator.RequestConfirmedRestartAsync());
    }

    [TestMethod]
    public async Task RepeatedClicksShareThePendingConfirmationAndOnlyAcceptanceRestarts()
    {
        var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmation = new Confirmation(() => choice.Task);
        var restart = new Restart();
        using var coordinator = new LyricsRecoveryNotificationCoordinator(new Notifications(), confirmation, restart, null);
        var first = coordinator.RequestConfirmedRestartAsync();
        Assert.AreEqual(ApplicationRestartResult.Busy, await coordinator.RequestConfirmedRestartAsync());
        Assert.AreEqual(1, confirmation.Requests);
        Assert.AreEqual(0, restart.Requests);
        choice.SetResult(true);
        Assert.AreEqual(ApplicationRestartResult.Requested, await first);
        Assert.AreEqual(1, restart.Requests);
    }

    [TestMethod]
    public async Task ExitDuringConfirmationDoesNotRequestRestart()
    {
        var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restart = new Restart();
        using var coordinator = new LyricsRecoveryNotificationCoordinator(new Notifications(), new Confirmation(() => choice.Task), restart, null);
        var first = coordinator.RequestConfirmedRestartAsync();
        coordinator.CancelPendingRequests(); choice.SetResult(true);
        Assert.AreEqual(ApplicationRestartResult.Canceled, await first);
        Assert.AreEqual(0, restart.Requests);
    }

    [TestMethod]
    public void RecoveryAndRestartStringsExistInEverySupportedLanguage()
    {
        string[] keys = ["Lyrics.Recovery.Notification.Title", "Lyrics.Recovery.Notification.Body", "Lyrics.Recovery.RestartReason",
            "Restart.Dialog.Title", "Restart.Dialog.Confirm", "Restart.Dialog.Later", "Restart.Failed", "Restart.Busy"];
        foreach (var language in Enum.GetValues<LocalizationLanguage>())
        {
            var suffix = language switch
            {
                LocalizationLanguage.SimplifiedChinese => "StringsZhHans.resources",
                LocalizationLanguage.TraditionalChinese => "StringsZhHant.resources",
                LocalizationLanguage.English => "StringsEn.resources",
                LocalizationLanguage.Vietnamese => "StringsVi.resources",
                _ => throw new AssertFailedException("Unsupported language")
            };
            var assembly = typeof(Translations).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal)))!;
            using var reader = new ResourceReader(stream);
            var values = reader.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
            foreach (var key in keys)
            {
                Assert.IsTrue(values.TryGetValue(key, out var value), $"Missing {key} in {language}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(value));
                if (key.EndsWith("Notification.Body", StringComparison.Ordinal)) Assert.IsTrue(value.Length <= 255);
            }
        }
    }

    private sealed class Notifications : ISystemNotificationService
    {
        public bool Accept;
        public List<ShellNotificationTarget> Targets { get; } = [];
        public event Action<ShellNotificationTarget>? NotificationClicked;
        public bool TryShowNotification(string title, string message, ShellNotificationTarget target = ShellNotificationTarget.None)
        { Targets.Add(target); return Accept; }
        public void Click(ShellNotificationTarget target) => NotificationClicked?.Invoke(target);
    }

    private sealed class Confirmation(Func<Task<bool>> choice) : IRestartConfirmationService
    {
        public int Requests;
        public Task<bool> ConfirmAsync(string reasonResourceKey, CancellationToken cancellationToken = default)
        { Requests++; return choice(); }
    }

    private sealed class Restart : IApplicationRestartService
    {
        public int Requests;
        public event EventHandler? RestartRequested { add { } remove { } }
        public Task<ApplicationRestartResult> RequestRestartAsync(CancellationToken cancellationToken = default)
        { Requests++; return Task.FromResult(ApplicationRestartResult.Requested); }
    }
}
