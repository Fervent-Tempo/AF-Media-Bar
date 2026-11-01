// 合成原创短音节；后台任务串行拥有 WASAPI 输出，取消不阻塞 UI。
using System.IO;
using Microsoft.Extensions.Hosting;
using NAudio.Wave;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>本地叽喳发声，替换当前片段，失败不影响文字。</summary>
public sealed class PetVoiceService : IHostedService, IDisposable
{
    private CancellationTokenSource? _cancellation;
    private Task _work = Task.CompletedTask;
    private bool _disposed;
    private long _quietUntil;
    public bool IsSpeaking => !_work.IsCompleted || Environment.TickCount64 < Interlocked.Read(ref _quietUntil);

    public void Play(bool preview = false)
    {
        if (_disposed) return;
        var settings = SettingsManager.Current.PetComponent.Normalize();
        if (!preview && !settings.VoiceEnabled) return;
        Stop();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _work = PlayAsync(_work, cancellation, settings.VoiceVolume, preview);
    }

    private async Task PlayAsync(Task previous, CancellationTokenSource cancellation, double volume, bool preview)
    {
        try
        {
            await previous.ConfigureAwait(false);
            await Task.Run(() =>
            {
                var token = cancellation.Token;
                token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(CreatePcm(volume));
                using var wave = new RawSourceWaveStream(stream, new WaveFormat(24000, 16, 1));
                using var output = new WasapiPlayerBuilder().WithSharedMode().Build();
                output.Init(wave);
                token.ThrowIfCancellationRequested();
                output.Play();
                while (output.PlaybackState == PlaybackState.Playing && !token.WaitHandle.WaitOne(25))
                    if (!preview && !SettingsManager.Current.PetComponent.VoiceEnabled) break;
                output.Stop();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { AppLogService.Current?.Warn("PetVoice", error.Message); }
        finally { Interlocked.Exchange(ref _quietUntil, Environment.TickCount64 + 300); cancellation.Dispose(); }
    }

    internal static byte[] CreatePcm(double volume)
    {
        const int SampleRate = 24000;
        var bytes = new byte[SampleRate * 2];
        var gain = Math.Clamp(double.IsFinite(volume) ? volume : 20, 0, 100) / 100 * 0.35;
        var notes = new[] { 620, 830, 740, 980, 690, 880, 720, 1040 };
        double phase = 0;
        for (var i = 0; i < SampleRate; i++)
        {
            var syllable = i / 3000;
            var t = i % 3000 / 3000d;
            var envelope = t < 0.8 ? Math.Pow(Math.Sin(Math.PI * t / 0.8), 2) : 0;
            phase += 2 * Math.PI * notes[syllable] * (1 + t * 0.2) / SampleRate;
            var value = (short)(short.MaxValue * gain * envelope * (Math.Sin(phase) + 0.25 * Math.Sin(phase * 2)) / 1.25);
            bytes[i * 2] = (byte)(value & 255);
            bytes[i * 2 + 1] = (byte)(value >> 8);
        }
        return bytes;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        await _work.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Stop()
    {
        try { _cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        _cancellation = null;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Stop(); }
}
