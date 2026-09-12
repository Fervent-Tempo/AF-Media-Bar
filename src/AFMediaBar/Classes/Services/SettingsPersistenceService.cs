using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Models.Layout;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace AFMediaBar.Classes.Services;

/// <summary>负责用户设置 JSON 的加载、恢复、原子保存和防抖。 / Owns loading, recovery, atomic saving and debouncing of user settings JSON.</summary>
public sealed class SettingsPersistenceService : IDisposable
{
    public const int CurrentSchemaVersion = 2;
    private readonly string _directoryPath;
    private readonly string _settingsPath;
    private readonly string _backupPath;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
    private Timer? _timer;
    private bool _initialized;
    private bool _disposed;

    /// <summary>创建设置存储服务；目录和防抖间隔可覆盖以便测试。 / Creates the settings store; directory and debounce can be overridden for tests.</summary>
    public SettingsPersistenceService(string? directoryPath = null, TimeSpan? debounce = null)
    {
        _directoryPath = directoryPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMediaBar");
        _settingsPath = Path.Combine(_directoryPath, "settings.json");
        _backupPath = _settingsPath + ".bak";
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        _jsonOptions.Converters.Add(new LenientEnumConverterFactory());
    }

    public string SettingsPath => _settingsPath;

    /// <summary>在 Host 启动前加载设置并订阅后续变化。 / Loads settings before Host startup and subscribes to later changes.</summary>
    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized) return;
            _initialized = true;
            LoadCore();
            SettingsManager.SettingsChanged += OnSettingsChanged;
        }
    }

    /// <summary>打开设置所在目录。 / Opens the settings directory.</summary>
    public void OpenSettingsFolder()
    {
        Directory.CreateDirectory(_directoryPath);
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{_directoryPath}\"", UseShellExecute = true });
    }

    /// <summary>等待当前防抖写入并同步落盘。 / Flushes the pending debounced write synchronously.</summary>
    public void Flush()
    {
        Timer? timer;
        lock (_gate)
        {
            timer = _timer;
            _timer = null;
        }
        timer?.Dispose();
        SaveCore(SettingsManager.Current);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Flush();
        return Task.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed || !_initialized) return;
            _timer?.Dispose();
            _timer = new Timer(static state => ((SettingsPersistenceService)state!).SaveFromTimer(), this, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void SaveFromTimer()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timer?.Dispose();
            _timer = null;
        }
        SaveCore(SettingsManager.Current);
    }

    private void LoadCore()
    {
        AppSettings? loaded = null;
        if (File.Exists(_settingsPath))
        {
            try { loaded = ReadEnvelope(_settingsPath); }
            catch (UnsupportedSettingsSchemaException)
            {
                Quarantine(_settingsPath, "unsupported");
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[Settings] Invalid settings file: {exception.Message}");
                Quarantine(_settingsPath, "invalid");
            }
        }

        if (loaded is null && File.Exists(_backupPath))
        {
            try { loaded = ReadEnvelope(_backupPath); }
            catch (Exception exception) { Debug.WriteLine($"[Settings] Invalid settings backup: {exception.Message}"); }
        }

        SettingsManager.Replace((loaded ?? new AppSettings()).Normalize());
        if (!File.Exists(_settingsPath) || loaded is null)
            SaveCore(SettingsManager.Current);
    }

    private AppSettings ReadEnvelope(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new JsonException("Settings envelope is empty.");
        if (node["settings"] is JsonObject settings)
        {
            foreach (var property in new[] { "layoutLengthScalePercent", "layoutThicknessScalePercent", "taskbarBarCrossAxisOffsetDip", "dynamicIslandLeft", "dynamicIslandTop" })
            {
                if (settings[property] is null) settings.Remove(property);
            }
        }
        var envelope = JsonSerializer.Deserialize<SettingsEnvelope>(node.ToJsonString(), _jsonOptions)
            ?? throw new JsonException("Settings envelope is empty.");
        if (envelope.SchemaVersion is < 1 or > CurrentSchemaVersion)
            throw new UnsupportedSettingsSchemaException(envelope.SchemaVersion);
        var result = envelope.Settings ?? new AppSettings();
        if (envelope.SchemaVersion == 1)
        {
            // The redesigned interaction model intentionally starts from its new defaults.
            // Stable appearance, lyric, taskbar-placement, and window-mode fields are retained.
            result.Interaction = GlobalInteractionSettings.Default;
            result.TaskbarExperience = TaskbarExperienceSettings.Default;
            result.TaskbarSurface = ModeSurfaceSettings.Default;
            result.DynamicIslandSurface = ModeSurfaceSettings.Default;
            result.LyricsTextAlignment = LyricsTextAlignment.Center;
        }
        return result.Normalize();
    }

    private void SaveCore(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_directoryPath);
            var temp = Path.Combine(_directoryPath, $"settings.{Guid.NewGuid():N}.tmp");
            var envelope = new SettingsEnvelope(CurrentSchemaVersion, settings.Normalize());
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, envelope, _jsonOptions);
                stream.Flush(true);
            }

            if (File.Exists(_settingsPath))
                File.Copy(_settingsPath, _backupPath, true);
            File.Move(temp, _settingsPath, true);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Settings] Save failed; keeping in-memory settings: {exception}");
        }
    }

    private void Quarantine(string path, string reason)
    {
        try
        {
            var target = $"{path}.{reason}-{DateTime.Now:yyyyMMddHHmmssfff}";
            File.Move(path, target, true);
        }
        catch (Exception exception) { Debug.WriteLine($"[Settings] Could not quarantine {path}: {exception.Message}"); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            SettingsManager.SettingsChanged -= OnSettingsChanged;
            _timer?.Dispose();
            _timer = null;
        }
        Flush();
    }

    private sealed record SettingsEnvelope(int SchemaVersion, AppSettings? Settings);
    private sealed class UnsupportedSettingsSchemaException(int version) : Exception($"Unsupported settings schema {version}.");

    private sealed class LenientEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type type) => type.IsEnum;
        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(LenientEnumConverter<>).MakeGenericType(type))!;
    }

    private sealed class LenientEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && Enum.TryParse<TEnum>(reader.GetString(), true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(TEnum), number)) return (TEnum)Enum.ToObject(typeof(TEnum), number);
            return DefaultValue();
        }
        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());

        private static TEnum DefaultValue()
        {
            object value = typeof(TEnum) == typeof(TrayWheelBehavior) ? TrayWheelBehavior.SwitchOutputDevice :
                typeof(TEnum) == typeof(LyricsSecondaryLineMode) ? LyricsSecondaryLineMode.NextLine :
                typeof(TEnum) == typeof(TaskbarBarPosition) ? TaskbarBarPosition.Start :
                typeof(TEnum) == typeof(LayoutOrientationMode) ? LayoutOrientationMode.Auto :
                typeof(TEnum) == typeof(DynamicIslandBackgroundMode) ? DynamicIslandBackgroundMode.SystemTheme :
                typeof(TEnum) == typeof(WindowMode) ? WindowMode.Taskbar :
                typeof(TEnum) == typeof(DynamicIslandEdge) ? DynamicIslandEdge.Top :
                typeof(TEnum) == typeof(LatinFontPreset) ? LatinFontPreset.SegoeUi :
                typeof(TEnum) == typeof(CjkFontPreset) ? CjkFontPreset.SystemDefault :
                typeof(TEnum) == typeof(PlayerForegroundMode) ? PlayerForegroundMode.Automatic :
                typeof(TEnum) == typeof(ApplicationThemeMode) ? ApplicationThemeMode.Automatic :
                typeof(TEnum) == typeof(ApplicationBackdropMode) ? ApplicationBackdropMode.Mica :
                typeof(TEnum) == typeof(MediaInteractionMode) ? MediaInteractionMode.Hybrid :
                typeof(TEnum) == typeof(WheelAction) ? WheelAction.PreviousNext :
                typeof(TEnum) == typeof(MouseChordButton) ? MouseChordButton.Left :
                typeof(TEnum) == typeof(TrayClickAction) ? TrayClickAction.OpenAudioControl :
                typeof(TEnum) == typeof(TaskbarInformationDensity) ? TaskbarInformationDensity.Balanced :
                typeof(TEnum) == typeof(TaskbarContentLayout) ? TaskbarContentLayout.AdaptiveStack :
                typeof(TEnum) == typeof(PlayerSurfaceStyle) ? PlayerSurfaceStyle.Automatic :
                typeof(TEnum) == typeof(LyricsTextAlignment) ? LyricsTextAlignment.Center : default(TEnum);
            return (TEnum)value;
        }
    }
}
