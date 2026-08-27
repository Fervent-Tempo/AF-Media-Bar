using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AFMediaBar.Layout.Defaults;
using AFMediaBar.Layout.Serialization;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 持有 schema 5 布局文档的 JSON 存储、恢复和原子写入；注册表仍由各旧设置服务负责。
/// Owns schema-5 JSON storage, recovery, and atomic writes while existing settings services retain registry ownership.
/// </summary>
public static class LayoutSettingsService
{
    private const string LayoutDirectoryName = "profiles";
    private const string LayoutFileName = "layout.json";
    private const string BackupFileName = "layout.json.bak";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    static LayoutSettingsService()
    {
        SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public static string LayoutFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AFMediaBar",
        LayoutDirectoryName,
        LayoutFileName);

    public static LayoutDocument Load(
        WindowSettings legacyWindowSettings,
        MetricSettings legacyMetricSettings)
    {
        var path = LayoutFilePath;
        if (!File.Exists(path))
        {
            var migrated = LayoutDefaultTemplates.LoadDocument();
            TrySaveAfterMigration(migrated, "layout-settings-initial-migration");
            DiagnosticsLogService.Write("layout-settings-loaded", details: $"Path={path};Source=legacy");
            return migrated;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            using var jsonDocument = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            var schemaVersion = jsonDocument.RootElement.TryGetProperty(
                "schemaVersion",
                out var schemaElement)
                ? schemaElement.GetInt32()
                : 1;
            LayoutDocument document;
            const bool migratedLegacyDocument = false;
            if (schemaVersion == LayoutDocument.CurrentSchemaVersion)
            {
                document = JsonSerializer.Deserialize<LayoutDocument>(json, SerializerOptions)
                    ?? throw new InvalidDataException("Layout document is empty.");
            }
            else
            {
                // schema 1-4 和未来版本均不再作为正式兼容格式；保留文件后恢复 schema 5 默认档案。
                throw new InvalidDataException(
                    $"Unsupported layout schema version: {schemaVersion}.");
            }

            var normalized = LayoutDocumentNormalizer.Normalize(document);
            if (migratedLegacyDocument || !Equals(document, normalized))
            {
                TrySaveAfterMigration(normalized, "layout-settings-normalize");
            }

            DiagnosticsLogService.Write(
                "layout-settings-loaded",
                details: $"Path={path};Source=json;Schema={schemaVersion}");
            return normalized;
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write(
                "layout-settings-read",
                exception,
                path);

            PreserveInvalidFile(path);
            var fallback = LayoutDefaultTemplates.LoadDocument();
            TrySaveAfterMigration(fallback, "layout-settings-recovery");
            DiagnosticsLogService.Write("layout-settings-loaded", details: $"Path={path};Source=recovery");
            return fallback;
        }
    }

    public static void Save(LayoutDocument document)
    {
        var normalized = LayoutDocumentNormalizer.Normalize(document);
        var path = LayoutFilePath;
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Layout directory cannot be resolved.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(normalized, SerializerOptions);
            File.WriteAllText(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(path))
            {
                File.Replace(
                    temporaryPath,
                    path,
                    Path.Combine(directory, BackupFileName),
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("layout-settings-write", exception, path);
            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception)
            {
                DiagnosticsLogService.Write(
                    "layout-settings-temp-cleanup",
                    exception,
                    temporaryPath);
            }
        }
    }

    private static void TrySaveAfterMigration(
        LayoutDocument document,
        string category)
    {
        try
        {
            Save(document);
        }
        catch (Exception exception)
        {
            // 迁移写入失败时仍返回内存中的有效布局，避免阻断启动。
            // Keep the valid in-memory layout when migration persistence fails so startup can continue.
            DiagnosticsLogService.Write(category, exception, LayoutFilePath);
        }
    }

    private static void PreserveInvalidFile(string path)
    {
        try
        {
            var preservedPath = $"{path}.invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            File.Copy(path, preservedPath, overwrite: false);
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("layout-settings-preserve-invalid", exception, path);
        }
    }
}
