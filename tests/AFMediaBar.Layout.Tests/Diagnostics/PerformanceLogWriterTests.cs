#if DEBUG
// 验证编号在轮转、元数据更新和 PID 复用后仍可解析，防止压缩丢失异常归属。
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AFMediaBar.Classes.Services.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>紧凑性能格式的身份连续性、未知值与容量验证。</summary>
[TestClass]
public sealed class PerformanceLogWriterTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void RepeatedSamplesKeepIdentityOutOfValuesAndReduceText()
    {
        WithDirectory(directory =>
        {
            var time = DateTimeOffset.UtcNow;
            var started = Stopwatch.GetTimestamp();
            var resources = Enumerable.Range(1, 7).Select(pid => new DebugProcessResources(pid, time.AddMinutes(-1),
                pid == 1 ? "AF" : "WebView2", 100000000, 120000000, 1.234567, pid == 1 ? 900 : null, pid == 1 ? 50 : null)).ToArray();
            var idleEngines = Enumerable.Range(0, 30).Select(index => new GpuProcessEngineUsage(2, "luid_0x00000000_0x00000001_phys_0", "eng_" + index + "_engtype_3D", 0)).ToArray();
            var owned = GpuProcessEngineUsage.SummarizeOwned(idleEngines, resources, time, 1);
            var path = Path.Combine(directory, "performance.jsonl");
            var measurements = new { durationMs = 12.3456789, gcPauseDeltaMs = (double?)null, managedBytes = 123456789L };
            var expanded = JsonSerializer.Serialize(new
            {
                schemaVersion = 1, sessionId = "session", recordingId = "recording", time,
                payload = new { measurements, processes = resources, ownedGpuEngines = idleEngines.Select(item => new OwnedGpuEngineUsage(item.Adapter, item.Engine, 0, 0)) }
            });
            using (var writer = new PerformanceLogWriter(path, 100000, 3, "session", "recording", started, new { pid = 1 }))
                for (var index = 0; index < 8; index++) writer.WriteSample(measurements, resources, null, owned, [], time, started);
            var records = Read(path);
            var samples = records.Where(item => Kind(item) == "sample").ToArray();
            Assert.AreEqual(8, samples.Length);
            Assert.AreEqual(7, records.Count(item => Kind(item) == "process"));
            var payload = samples[0].GetProperty("payload");
            Assert.AreEqual(0, payload.GetProperty("ownedGpuEngines").GetArrayLength());
            var values = payload.GetProperty("measurements");
            Assert.AreEqual(12.35, values.GetProperty("durationMs").GetDouble());
            Assert.AreEqual(JsonValueKind.Null, values.GetProperty("gcPauseDeltaMs").ValueKind);
            Assert.AreEqual(123456789L, values.GetProperty("managedBytes").GetInt64());
            Assert.IsFalse(payload.GetProperty("processes")[0].TryGetProperty("startedAt", out _));
            var meanCompact = new FileInfo(path).Length / 8d;
            var expandedBytes = Encoding.UTF8.GetByteCount(expanded);
            Assert.IsTrue(meanCompact < expandedBytes / 2d, "Representative idle samples should shrink by more than half, including metadata.");
            TestContext.WriteLine($"expandedBytesPerSample={expandedBytes}; compactBytesPerSampleIncludingMetadata={meanCompact:F0}");
        });
    }

    [TestMethod]
    public void EveryRotatedFileHasDefinitionsBeforeReferences()
    {
        WithDirectory(directory =>
        {
            var time = DateTimeOffset.UtcNow;
            var started = Stopwatch.GetTimestamp();
            var path = Path.Combine(directory, "performance.jsonl");
            DebugProcessResources[] resources = [new(1, time.AddMinutes(-1), "AF", 100, 200, 1, 20, 10)];
            using (var writer = new PerformanceLogWriter(path, 2200, 3, "session", "recording", started, new { pid = 1 }))
                for (var index = 0; index < 30; index++) writer.WriteSample(new { index, elapsedMs = 1.234567 }, resources,
                    new("adapter", "3D", 30), [new("adapter", "3D", 10, 0)],
                    [new(1, "AFMediaBar", resources[0].StartedAt, "adapter", "3D", 10)], time, started);
            Assert.AreEqual(3, Directory.GetFiles(directory).Length);
            foreach (var file in Directory.GetFiles(directory))
            {
                Assert.IsTrue(new FileInfo(file).Length <= 2200);
                var processes = new HashSet<int>();
                var engines = new HashSet<int>();
                var records = Read(file);
                Assert.AreEqual("context", Kind(records[0]));
                foreach (var record in records)
                {
                    switch (Kind(record))
                    {
                        case "context": processes.Clear(); engines.Clear(); break;
                        case "process": processes.Add(record.GetProperty("id").GetInt32()); break;
                        case "gpuEngine": engines.Add(record.GetProperty("id").GetInt32()); break;
                        case "sample":
                            var payload = record.GetProperty("payload");
                            foreach (var process in payload.GetProperty("processes").EnumerateArray())
                                Assert.IsTrue(processes.Contains(process.GetProperty("process").GetInt32()));
                            foreach (var engine in payload.GetProperty("ownedGpuEngines").EnumerateArray())
                                Assert.IsTrue(engines.Contains(engine.GetProperty("engine").GetInt32()));
                            Assert.IsTrue(engines.Contains(payload.GetProperty("globalBusiestGpuEngine").GetProperty("engine").GetInt32()));
                            foreach (var top in payload.GetProperty("topGpuProcesses").EnumerateArray())
                            {
                                Assert.IsTrue(processes.Contains(top.GetProperty("process").GetInt32()));
                                Assert.IsTrue(engines.Contains(top.GetProperty("engine").GetInt32()));
                            }
                            break;
                    }
                }
            }
        });
    }

    [TestMethod]
    public void ProcessLifetimesAndUnknownIdentitiesRemainDistinctAcrossMetadataReset()
    {
        WithDirectory(directory =>
        {
            var time = DateTimeOffset.UtcNow;
            var started = Stopwatch.GetTimestamp();
            var path = Path.Combine(directory, "performance.jsonl");
            using (var writer = new PerformanceLogWriter(path, 1000000, 3, "session", "recording", started, new { pid = 1 }))
            {
                for (var index = 0; index < 260; index++)
                {
                    DebugProcessResources[] resources = [new(2, time.AddSeconds(index - 300), "WebView2", 10, 20, null, null, null)];
                    writer.WriteSample(new { index }, resources, null, [], [new(99, null, null, "adapter", "3D", 5)], time, started);
                }
            }
            var records = Read(path);
            Assert.IsTrue(records.Count(item => Kind(item) == "context") >= 2, "Identity tables must be bounded and explicitly reset.");
            var births = records.Where(item => Kind(item) == "process").Select(item => item.GetProperty("startedAt").GetDateTimeOffset()).Distinct().Count();
            Assert.AreEqual(260, births, "A reused PID cannot inherit a previous process identity.");
            var last = records.Last(item => Kind(item) == "sample").GetProperty("payload").GetProperty("topGpuProcesses")[0];
            Assert.AreEqual(JsonValueKind.Null, last.GetProperty("process").ValueKind);
            Assert.AreEqual(99, last.GetProperty("pid").GetInt32());
        });
    }

    private static string Kind(JsonElement element) => element.GetProperty("kind").GetString()!;
    private static JsonElement[] Read(string path) => File.ReadLines(path).Select(line =>
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }).ToArray();

    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-compact-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }
}
#endif
