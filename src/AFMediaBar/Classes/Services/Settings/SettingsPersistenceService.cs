using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services.Localization;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace AFMediaBar.Classes.Services;

/// <summary>负责用户设置 JSON 的加载、恢复、原子保存和防抖。 / Owns loading, recovery, atomic saving and debouncing of user settings JSON.</summary>
public sealed class SettingsPersistenceService : IDisposable
{
    /// <summary>
    /// 当前设置文件 schema。**只在发布（release）批次里变更，开发批次不动它**：两次发布之间的中间构建共用同一个编号，
    /// 因此"这份文件是谁写的"始终只有一个含义，而不是每加一个设置就换一个数字。
    ///
    /// 程序**只读取本编号的设置文件，不迁移更早的编号**：1.2.0 起，编号不同的文件一律被隔离改名，设置回到内置默认值，
    /// 而不是被"尽力读进来"。理由是重建后的设置模型与旧模型已不是同一份东西——半对半错地读进来比回到默认值更难排查。
    /// 将来某个发布若确实要读取上一版的文件，就在那个发布里显式写迁移，并同时改掉这段注释与 `ReadEnvelope` 的相等判断。
    /// Current settings schema. It **changes only in a release batch, never in a development one**: intermediate builds between
    /// two releases share one number, so "who wrote this file" keeps exactly one meaning instead of getting a new digit every time
    /// a setting is added.
    ///
    /// The application reads a file carrying this number **only and never migrates an earlier one**: from 1.2.0 on, a file with a
    /// different number is quarantined under a new name and the built-in defaults take over, rather than being read on a best-effort
    /// basis. The rebuilt settings model is not the same object as the old one, and reading it half-right is harder to diagnose than
    /// starting from the defaults. A future release that really has to read its predecessor's file writes an explicit migration
    /// there, and changes both this comment and the equality check in `ReadEnvelope` at the same time.
    /// </summary>
    public const int CurrentSchemaVersion = 2;
    private readonly string _directoryPath;
    private readonly string _settingsPath;
    private readonly string _backupPath;
    private readonly string _userDefaultsPath;
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
    private int? _loadedSchemaVersion;

    /// <summary>创建设置存储服务；目录和防抖间隔可覆盖以便测试。 / Creates the settings store; directory and debounce can be overridden for tests.</summary>
    public SettingsPersistenceService(string? directoryPath = null, TimeSpan? debounce = null)
    {
        _directoryPath = directoryPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMediaBar");
        _settingsPath = Path.Combine(_directoryPath, "settings.json");
        _backupPath = _settingsPath + ".bak";
        _userDefaultsPath = Path.Combine(_directoryPath, "user-defaults.json");
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

    /// <summary>
    /// 把当前设置保存为「我的默认设置」：写入独立的快照文件，并把内存里生效的默认值换成它。
    /// 设置本身不变，因此保存默认不会打断用户当前的使用状态。
    /// Saves the current settings as the user's defaults: the snapshot goes to its own file and the in-memory effective defaults are
    /// swapped to it. The settings themselves are untouched, so saving defaults never interrupts what the user is doing.
    /// </summary>
    /// <returns>写入失败的原因；成功时为 null。/ The failure reason, or null on success.</returns>
    public string? SaveCurrentAsUserDefaults()
    {
        var snapshot = SettingsManager.Current.Clone();
        try
        {
            Directory.CreateDirectory(_directoryPath);
            var temp = _userDefaultsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new SettingsEnvelope(CurrentSchemaVersion, snapshot), _jsonOptions));
            File.Move(temp, _userDefaultsPath, overwrite: true);
        }
        catch (Exception exception)
        {
            AppLogService.Current?.Warn("Settings", $"写入我的默认设置失败 / writing user defaults failed: {exception.Message}");
            Debug.WriteLine($"[Settings] Could not write user defaults: {exception.Message}");
            return exception.Message;
        }

        SettingsManager.SetUserDefaults(snapshot);
        AppLogService.Current?.Info("Settings", "已保存「我的默认设置」/ user defaults saved");
        return null;
    }

    /// <summary>
    /// 读取「我的默认设置」快照；文件不存在或不可读时返回 null。
    /// 快照与设置文件走同一条读取路径，因此编号不同的快照同样不被读取：文件改名留档（不删除），界面据此显示"未保存"，
    /// 而不是拿一份读不准的旧快照去覆盖用户的设置。
    /// Reads the user-defaults snapshot, or null when the file is missing or unreadable. The snapshot goes through the same read path
    /// as the settings file, so a snapshot carrying a different schema number is not read either: the file is renamed and kept (never
    /// deleted) and the page honestly shows "not saved", instead of using a snapshot it cannot read accurately to overwrite settings.
    /// </summary>
    public AppSettings? LoadUserDefaults()
    {
        if (!File.Exists(_userDefaultsPath))
            return null;

        try
        {
            return ReadEnvelope(_userDefaultsPath).Normalize();
        }
        catch (UnsupportedSettingsSchemaException)
        {
            Quarantine(_userDefaultsPath, "unsupported");
            return null;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Settings] Invalid user defaults: {exception.Message}");
            return null;
        }
    }

    /// <summary>删除「我的默认设置」快照，让所有重置入口回到程序内置默认。/ Deletes the user-defaults snapshot so every reset entry falls back to the built-in defaults.</summary>
    /// <returns>删除失败的原因；成功或文件本就不存在时为 null。/ The failure reason, or null on success or when no snapshot existed.</returns>
    public string? ClearUserDefaults()
    {
        try
        {
            if (File.Exists(_userDefaultsPath))
                File.Delete(_userDefaultsPath);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Settings] Could not delete user defaults: {exception.Message}");
            return exception.Message;
        }

        SettingsManager.SetUserDefaults(null);
        return null;
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
        _loadedSchemaVersion = null;

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
        AppLogService.Current?.Info(
            "Settings",
            $"已加载设置 / settings loaded: schema {_loadedSchemaVersion?.ToString() ?? "none"} → {CurrentSchemaVersion}, " +
            $"file={(File.Exists(_settingsPath) ? _settingsPath : "<none>")}");
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
        // 只接受当前编号：编号不同的文件（含 1.1.1 写出的一切旧编号）既不读取也不改写，由调用方隔离后回到内置默认值。
        // Only the current number is accepted: a file carrying a different one — including everything 1.1.1 wrote — is neither read
        // nor rewritten; the caller quarantines it and the built-in defaults take over.
        if (envelope.SchemaVersion != CurrentSchemaVersion)
            throw new UnsupportedSettingsSchemaException(envelope.SchemaVersion);
        _loadedSchemaVersion = envelope.SchemaVersion;
        return (envelope.Settings ?? new AppSettings()).Normalize();
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
            AppLogService.Current?.Error("Settings", "保存设置失败，内存中的设置保留 / saving settings failed, in-memory settings kept", exception);
            Debug.WriteLine($"[Settings] Save failed; keeping in-memory settings: {exception}");
        }
    }

    private void Quarantine(string path, string reason)
    {
        try
        {
            var target = $"{path}.{reason}-{DateTime.Now:yyyyMMddHHmmssfff}";
            File.Move(path, target, true);
            AppLogService.Current?.Warn("Settings", $"设置文件不可用，已隔离 / settings file quarantined ({reason}): {target}");
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
                typeof(TEnum) == typeof(LyricsSecondaryLineMode) ? LyricsSecondaryLineMode.Translation :                typeof(TEnum) == typeof(TaskbarBarPosition) ? TaskbarBarPosition.Start :
                typeof(TEnum) == typeof(LayoutOrientationMode) ? LayoutOrientationMode.Auto :
                typeof(TEnum) == typeof(DynamicIslandBackgroundMode) ? DynamicIslandBackgroundMode.SystemTheme :
                typeof(TEnum) == typeof(TrackChangeNotificationPosition) ? TrackChangeNotificationPosition.BottomLeft :
                typeof(TEnum) == typeof(NotificationTargetMode) ? NotificationTargetMode.Fixed :
                typeof(TEnum) == typeof(WindowMode) ? WindowMode.Taskbar :
                typeof(TEnum) == typeof(DynamicIslandEdge) ? DynamicIslandEdge.Top :
                typeof(TEnum) == typeof(LatinFontPreset) ? LatinFontPreset.SystemDefault :
                typeof(TEnum) == typeof(CjkFontPreset) ? CjkFontPreset.SystemDefault :
                typeof(TEnum) == typeof(PlayerForegroundMode) ? PlayerForegroundMode.Automatic :
                typeof(TEnum) == typeof(ApplicationThemeMode) ? ApplicationThemeMode.Automatic :
                typeof(TEnum) == typeof(ApplicationBackdropMode) ? ApplicationBackdropMode.Mica :
                typeof(TEnum) == typeof(WheelAction) ? WheelAction.PreviousNext :
                typeof(TEnum) == typeof(PlayerClickAction) ? PlayerClickAction.TogglePlayPause :
                typeof(TEnum) == typeof(InteractionModifier) ? InteractionModifier.Shift :
                typeof(TEnum) == typeof(TrayClickAction) ? TrayClickAction.OpenAudioControl :
                typeof(TEnum) == typeof(TaskbarInformationDensity) ? TaskbarInformationDensity.Balanced :
                typeof(TEnum) == typeof(TaskbarContentLayout) ? TaskbarContentLayout.AdaptiveStack :
                typeof(TEnum) == typeof(TaskbarMediaTextAlignment) ? TaskbarMediaTextAlignment.Left :
                typeof(TEnum) == typeof(TaskbarLengthMode) ? TaskbarLengthMode.FollowContent :
                typeof(TEnum) == typeof(PlayerSurfaceStyle) ? PlayerSurfaceStyle.Automatic :
                typeof(TEnum) == typeof(LyricsTextAlignment) ? LyricsTextAlignment.Center : default(TEnum);
            return (TEnum)value;
        }
    }
}
