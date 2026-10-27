#if DEBUG
// 后台记录任务独占格式、身份表及轮转文件；编号只在一个 context 区间内有效。
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AFMediaBar.Classes.Services.Diagnostics;

/// <summary>性能格式第二版：身份按变化记录，轮转时补齐上下文，数值仅在输出时舍入。</summary>
internal sealed class PerformanceLogWriter(string path, long maximumBytes, int maximumFiles,
    string sessionId, string recordingId, long started, object environment) : IDisposable
{
    private const int MaximumProcesses = 256;
    private const int MaximumEngines = 512;
    private static readonly JsonSerializerOptions _json = CreateJsonOptions();
    private readonly RollingLogFile _file = new(path, maximumBytes, maximumFiles);
    private readonly Dictionary<(int Pid, DateTimeOffset StartedAt), ProcessDefinition> _processes = [];
    private readonly Dictionary<(string Adapter, string Engine), EngineDefinition> _engines = [];
    private IReadOnlyList<string>? _prefix;
    private bool _contextPending = true;
    private int _epoch;
    private long _sequence;

    internal void Write(string kind, object payload, DateTimeOffset time, long timestamp) =>
        WriteRecord(kind, payload, time, timestamp, []);

    internal void WriteSample(object measurements, DebugProcessResources[] resources, GpuEngineTotal? global,
        OwnedGpuEngineUsage[] owned, GpuProcessIdentityUsage[] top, DateTimeOffset time, long timestamp)
    {
        // 预留一整条采样所需的身份，不能在构造采样中途清空编号表。
        if (_processes.Count + resources.Length + top.Length > MaximumProcesses ||
            _engines.Count + owned.Length + top.Length + 1 > MaximumEngines)
        {
            _processes.Clear();
            _engines.Clear();
            _epoch++;
            _contextPending = true;
            _prefix = null;
        }
        List<string> definitions = [];
        var processValues = resources.Select(item => new
        {
            process = ProcessId(item.Pid, item.StartedAt, null, item.Role, definitions),
            item.WorkingSetBytes, item.PrivateBytes, item.CpuPercent, item.Handles, item.Threads
        }).ToArray();
        object? globalValue = global is null ? null : new
        {
            engine = EngineId(global.Adapter, global.Engine, definitions), global.Percent
        };
        var ownedValues = owned.Select(item => new
        {
            engine = EngineId(item.Adapter, item.Engine, definitions), item.AfPercent, item.WebViewPercent
        }).ToArray();
        var topValues = top.Select(item => new
        {
            process = item.StartedAt is { } birth ? ProcessId(item.Pid, birth, item.Name, null, definitions) : (int?)null,
            pid = item.StartedAt is null ? item.Pid : (int?)null,
            engine = EngineId(item.Adapter, item.Engine, definitions), item.Percent
        }).ToArray();
        WriteRecord("sample", new
        {
            measurements, processes = processValues, globalBusiestGpuEngine = globalValue,
            ownedGpuEngines = ownedValues, topGpuProcesses = topValues
        }, time, timestamp, definitions);
    }

    private int ProcessId(int pid, DateTimeOffset birth, string? name, string? role, List<string> definitions)
    {
        var key = (pid, birth);
        _processes.TryGetValue(key, out var previous);
        var current = new ProcessDefinition(previous?.Id ?? _processes.Count + 1, pid, birth,
            name ?? previous?.Name, role ?? previous?.Role);
        if (current != previous)
        {
            _processes[key] = current;
            definitions.Add(SerializeProcess(current));
            _prefix = null;
        }
        return current.Id;
    }

    private int EngineId(string adapter, string engine, List<string> definitions)
    {
        var key = (adapter, engine);
        if (_engines.TryGetValue(key, out var previous)) return previous.Id;
        var current = new EngineDefinition(_engines.Count + 1, adapter, engine);
        _engines[key] = current;
        definitions.Add(SerializeEngine(current));
        _prefix = null;
        return current.Id;
    }

    private void WriteRecord(string kind, object payload, DateTimeOffset time, long timestamp, List<string> definitions)
    {
        List<string> batch = [];
        if (_contextPending) batch.Add(SerializeContext());
        batch.AddRange(definitions);
        batch.Add(JsonSerializer.Serialize(new
        {
            sequence = ++_sequence, time, timestamp,
            elapsedMs = Stopwatch.GetElapsedTime(started, timestamp).TotalMilliseconds, kind, payload
        }, _json));
        _file.AppendBatch(batch, _contextPending ? null : Prefix);
        _contextPending = false;
    }

    private IReadOnlyList<string> Prefix() => _prefix ??= new[] { SerializeContext() }
        .Concat(_processes.Values.Select(SerializeProcess)).Concat(_engines.Values.Select(SerializeEngine)).ToArray();

    private string SerializeContext() => JsonSerializer.Serialize(new
    {
        schemaVersion = 2, kind = "context", sessionId, recordingId, metadataEpoch = _epoch,
        startedTimestamp = started, timestampFrequency = Stopwatch.Frequency, environment
    }, _json);

    private static string SerializeProcess(ProcessDefinition value) => JsonSerializer.Serialize(new
    {
        kind = "process", value.Id, value.Pid, value.StartedAt, value.Name, value.Role
    }, _json);

    private static string SerializeEngine(EngineDefinition value) => JsonSerializer.Serialize(new
    {
        kind = "gpuEngine", value.Id, value.Adapter, value.Engine
    }, _json);

    internal void Flush() => _file.Flush();
    public void Dispose() => _file.Dispose();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new RoundedDoubleConverter());
        return options;
    }

    private sealed record ProcessDefinition(int Id, int Pid, DateTimeOffset StartedAt, string? Name, string? Role);
    private sealed record EngineDefinition(int Id, string Adapter, string Engine);

    private sealed class RoundedDoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDouble();
        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (!double.IsFinite(value)) { writer.WriteNullValue(); return; }
            var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
            writer.WriteNumberValue(rounded == 0 ? 0 : rounded);
        }
    }
}
#endif
