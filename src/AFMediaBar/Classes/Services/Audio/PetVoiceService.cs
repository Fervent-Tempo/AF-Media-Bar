// 拼接内置原创音节；后台任务串行拥有 WASAPI 输出，取消不阻塞 UI。
using System.IO;
using System.Text;
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

    public void Play(string text, bool preview = false)
    {
        if (_disposed) return;
        var settings = SettingsManager.Current.PetComponent.Normalize();
        if (!preview && !settings.VoiceEnabled) return;
        Stop();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _work = PlayAsync(_work, cancellation, settings.VoiceVolume, preview, text);
    }

    private async Task PlayAsync(Task previous, CancellationTokenSource cancellation, double volume, bool preview, string text)
    {
        try
        {
            await previous.ConfigureAwait(false);
            await Task.Run(() =>
            {
                var token = cancellation.Token;
                token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(CreatePcm(volume, text));
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

    private static readonly Lazy<byte[]> Syllables = new(() =>
    {
        using var source = typeof(PetVoiceService).Assembly.GetManifestResourceStream("AFMediaBar.Resources.Audio.PetSyllables.pcm")
            ?? throw new InvalidOperationException("Pet syllables missing");
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    });

    internal static byte[] CreatePcm(double volume, string text)
    {
        const int sampleRate = 24000, syllableLength = 2640;
        var gain = Math.Clamp(double.IsFinite(volume) ? volume : 20, 0, 100) / 100 * 0.35;
        var bank = Syllables.Value;
        var notes = new[] { 0, 2, 5, 7, 9, 5, 2, 0, -2, 4 };
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var position = 0;
        // ponytail: 最长 80 个字符；长篇朗读需要流式调度。
        foreach (var character in (text ?? string.Empty).EnumerateRunes().Take(80))
        {
            if (Rune.IsPunctuation(character) || Rune.IsWhiteSpace(character))
            {
                var pause = Rune.IsWhiteSpace(character) ? 0.06 : ".!?。！？".Contains(character.ToString()) ? 0.3 : 0.18;
                writer.Write(new byte[(int)(sampleRate * pause) * 2]);
                continue;
            }
            var code = character.Value;
            var index = (code * 17 + position * 11) % 40;
            var semitones = notes[(code + position * 3) % notes.Length];
            var speed = 1.3 * Math.Pow(2, semitones / 12d);
            var count = Math.Min(3200, (int)(syllableLength / speed));
            for (var frame = 0; frame < count; frame++)
            {
                var offset = frame * speed;
                var left = (int)offset;
                var right = Math.Min(left + 1, syllableLength - 1);
                var first = BitConverter.ToInt16(bank, (index * syllableLength + left) * 2);
                var second = BitConverter.ToInt16(bank, (index * syllableLength + right) * 2);
                var envelope = Math.Min(1, Math.Min(frame, count - 1 - frame) / 120d);
                writer.Write((short)((first + (second - first) * (offset - left)) * gain * envelope));
            }
            writer.Write(new byte[(3200 - count) * 2]);
            position++;
        }
        return stream.ToArray();
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
