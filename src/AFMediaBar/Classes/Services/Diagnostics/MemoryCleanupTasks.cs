// 主程序只检查和按需运行任务；安装/删除仅供已提权的隔离执行端调用。每次 COM 会话逆序释放全部 RCW。
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>实际授权状态不从用户配置或默认快照恢复。</summary>
public enum MemoryCleanupAuthorization { Missing, Ready, NeedsRepair, Unavailable }

/// <summary>固定任务、受保护执行端和当前用户安装实例的绑定。</summary>
public static class MemoryCleanupTasks
{
    public const string FolderName = "AFMediaBar.MemoryOptimizer";
    public const string PayloadManifest = "payload.files";
    public static bool ValidPayloadName(string name) => !string.IsNullOrWhiteSpace(name) && name is not ("." or "..") && name.IndexOfAny(['/', '\\', ':']) < 0;
    public const string RegistryPath = @"Software\AFMediaBar\MemoryOptimizer";
    public static string InstanceId(string executable) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(executable).ToUpperInvariant())))[..24];
    public static bool ValidInstance(string value) => value.Length == 24 && value.All(char.IsAsciiHexDigit);
    public static string HelperDirectory(string owner, string instance) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), FolderName, owner, instance);
    public static string HelperPath(string owner, string instance) => Path.Combine(HelperDirectory(owner, instance), "AFMediaBar.MemoryHelper.exe");
    private static string TaskName(string owner, string instance, SystemMemoryOptimizationLevel level) => owner + "-" + instance + "-" + level;
    private static string Arguments(SystemMemoryOptimizationLevel level) => "--run " + (level == SystemMemoryOptimizationLevel.Deep ? "deep" : "standard") + " $(Arg0)";

    public static MemoryCleanupAuthorization Inspect(string owner, string instance, IReadOnlyDictionary<string, byte[]> expectedFiles)
    {
        try
        {
            var directory = HelperDirectory(owner, instance);
            var path = HelperPath(owner, instance);
            if (!File.Exists(path)) return MemoryCleanupAuthorization.Missing;
            var parent = new DirectoryInfo(directory);
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), FolderName);
            while (parent is not null && parent.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0 || !IsProtected(parent.GetAccessControl())) return MemoryCleanupAuthorization.NeedsRepair;
                parent = parent.Parent;
            }
            foreach (var entry in expectedFiles)
            {
                if (!ValidPayloadName(entry.Key)) return MemoryCleanupAuthorization.NeedsRepair;
                var payload = Path.Combine(directory, entry.Key);
                if (!File.Exists(payload) || (File.GetAttributes(payload) & FileAttributes.ReparsePoint) != 0 || !IsProtected(new FileInfo(payload).GetAccessControl())) return MemoryCleanupAuthorization.NeedsRepair;
                using var file = File.OpenRead(payload);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(file), entry.Value)) return MemoryCleanupAuthorization.NeedsRepair;
            }
            using var com = new TaskSession();
            dynamic folder = com.Folder();
            foreach (var level in Enum.GetValues<SystemMemoryOptimizationLevel>())
            {
                dynamic task = com.Track(folder.GetTask(TaskName(owner, instance, level)));
                dynamic definition = com.Track(task.Definition);
                dynamic principal = com.Track(definition.Principal);
                dynamic actions = com.Track(definition.Actions);
                dynamic triggers = com.Track(definition.Triggers);
                if (!(bool)task.Enabled || (int)principal.RunLevel != 1 || (int)principal.LogonType != 3 ||
                    !SameUser((string)principal.UserId, owner) || (int)actions.Count != 1 || (int)triggers.Count != 0) return MemoryCleanupAuthorization.NeedsRepair;
                dynamic action = com.Track(actions[1]);
                if ((int)action.Type != 0 || !string.Equals((string)action.Path, path, StringComparison.OrdinalIgnoreCase) ||
                    (string)action.Arguments != Arguments(level) || !string.Equals((string)action.WorkingDirectory, directory, StringComparison.OrdinalIgnoreCase) || !TaskAclProtected((string)task.GetSecurityDescriptor(5))) return MemoryCleanupAuthorization.NeedsRepair;
            }
            return MemoryCleanupAuthorization.Ready;
        }
        catch (Exception error) when (IsMissing(error)) { return MemoryCleanupAuthorization.NeedsRepair; }
        catch (COMException) { return MemoryCleanupAuthorization.NeedsRepair; }
        catch (Exception) { return MemoryCleanupAuthorization.Unavailable; }
    }

    public static void Run(string owner, string instance, SystemMemoryOptimizationLevel level, string nonce)
    {
        using var com = new TaskSession();
        dynamic folder = com.Folder();
        dynamic task = com.Track(folder.GetTask(TaskName(owner, instance, level)));
        com.Track(task.Run(new string[] { nonce }));
    }

    public static void Install(string owner, string instance, string origin)
    {
        RequireOwner(owner, instance);
        if (InstanceId(origin) != instance) throw new UnauthorizedAccessException();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), FolderName);
        foreach (var directory in new[] { root, Path.Combine(root, owner), HelperDirectory(owner, instance) })
        {
            var info = new DirectoryInfo(directory);
            if (info.Exists)
            {
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || !IsProtected(info.GetAccessControl())) throw new UnauthorizedAccessException();
            }
            else info.Create(DirectoryAcl(owner));
        }
        var destination = HelperPath(owner, instance);
        var names = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, PayloadManifest)).Append(PayloadManifest).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!names.Contains("AFMediaBar.MemoryHelper.exe", StringComparer.OrdinalIgnoreCase) || names.Any(name => !ValidPayloadName(name))) throw new InvalidDataException();
        var oldManifest = Path.Combine(HelperDirectory(owner, instance), PayloadManifest);
        if (File.Exists(oldManifest))
        {
            if ((File.GetAttributes(oldManifest) & FileAttributes.ReparsePoint) != 0 || !IsProtected(new FileInfo(oldManifest).GetAccessControl())) throw new UnauthorizedAccessException();
            foreach (var obsolete in File.ReadAllLines(oldManifest).Except(names, StringComparer.OrdinalIgnoreCase))
            {
                if (!ValidPayloadName(obsolete)) throw new InvalidDataException();
                var oldFile = Path.Combine(HelperDirectory(owner, instance), obsolete);
                if (File.Exists(oldFile) && ((File.GetAttributes(oldFile) & FileAttributes.ReparsePoint) != 0 || !IsProtected(new FileInfo(oldFile).GetAccessControl()))) throw new UnauthorizedAccessException();
                File.Delete(oldFile);
            }
        }
        foreach (var name in names)
        {
            var targetPath = Path.Combine(HelperDirectory(owner, instance), name);
            if (File.Exists(targetPath) && ((File.GetAttributes(targetPath) & FileAttributes.ReparsePoint) != 0 || !IsProtected(new FileInfo(targetPath).GetAccessControl()))) throw new UnauthorizedAccessException();
            var fileAcl = new FileSecurity();
            fileAcl.SetSecurityDescriptorSddlForm($"O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FRFX;;;{owner})");
            using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, name));
            using var target = new FileInfo(targetPath).Create(FileMode.Create, FileSystemRights.Write, FileShare.None, 81920, FileOptions.SequentialScan, fileAcl);
            source.CopyTo(target);
        }
        using var com = new TaskSession();
        dynamic folder = com.Folder(create: true);
        foreach (var level in Enum.GetValues<SystemMemoryOptimizationLevel>())
        {
            dynamic definition = com.Track(com.Service.NewTask(0));
            dynamic principal = com.Track(definition.Principal);
            principal.UserId = owner; principal.LogonType = 3; principal.RunLevel = 1;
            dynamic settings = com.Track(definition.Settings);
            settings.Enabled = true; settings.Hidden = true; settings.AllowDemandStart = true;
            settings.DisallowStartIfOnBatteries = false; settings.StopIfGoingOnBatteries = false;
            settings.ExecutionTimeLimit = "PT60S"; settings.MultipleInstances = 2;
            dynamic actions = com.Track(definition.Actions);
            dynamic action = com.Track(actions.Create(0));
            action.Path = destination; action.Arguments = Arguments(level); action.WorkingDirectory = HelperDirectory(owner, instance);
            // 保留显式只读/执行权限，禁止调度器为当前用户额外添加可写 ACE。
            com.Track(folder.RegisterTaskDefinition(TaskName(owner, instance, level), definition, 0x6 | 0x10, owner, null, 3,
                $"O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;{owner})"));
        }
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath + "\\" + instance);
        key.SetValue("Origin", Path.GetFullPath(origin));
        key.SetValue("OwnerSid", owner);
    }

    public static void Remove(string owner, string instance)
    {
        RequireOwner(owner, instance);
        using (var com = new TaskSession())
        {
            dynamic folder;
            try { folder = com.Folder(); } catch (Exception error) when (IsMissing(error)) { folder = null!; }
            if (folder is not null)
                foreach (var level in Enum.GetValues<SystemMemoryOptimizationLevel>())
                    try
                    {
                        dynamic task = com.Track(folder.GetTask(TaskName(owner, instance, level)));
                        if ((int)task.State == 4) task.Stop(0);
                        folder.DeleteTask(TaskName(owner, instance, level), 0);
                    }
                    catch (Exception error) when (IsMissing(error)) { }
        }
        var directory = HelperDirectory(owner, instance);
        if (Directory.Exists(directory))
        {
            var info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || !IsProtected(info.GetAccessControl())) throw new UnauthorizedAccessException();
            var manifestPath = Path.Combine(directory, PayloadManifest);
            if (File.Exists(manifestPath) && ((File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0 || !IsProtected(new FileInfo(manifestPath).GetAccessControl()))) throw new UnauthorizedAccessException();
            var names = File.Exists(manifestPath)
                ? File.ReadAllLines(manifestPath).Append(PayloadManifest).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, PayloadManifest)).Append(PayloadManifest).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (names.Any(name => !ValidPayloadName(name))) throw new UnauthorizedAccessException();
            foreach (var name in names)
            {
                var file = Path.Combine(directory, name);
                if (File.Exists(file) && ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || !IsProtected(new FileInfo(file).GetAccessControl()))) throw new UnauthorizedAccessException();
                for (var attempt = 0; ; attempt++)
                {
                    try { File.Delete(file); break; }
                    catch (IOException) when (attempt < 20) { Thread.Sleep(100); }
                    catch (UnauthorizedAccessException) when (attempt < 20) { Thread.Sleep(100); }
                }
            }
            Directory.Delete(directory); // 不递归删除其他文件或安装实例。
        }
        Registry.CurrentUser.DeleteSubKey(RegistryPath + "\\" + instance, false);
    }

    private static void RequireOwner(string owner, string instance)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!ValidInstance(instance) || identity.User?.Value != owner || !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException();
    }
    private static DirectorySecurity DirectoryAcl(string owner)
    {
        var acl = new DirectorySecurity();
        acl.SetSecurityDescriptorSddlForm($"O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FRFX;;;{owner})");
        return acl;
    }
    private static bool IsProtected(FileSystemSecurity security)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner ||
            !(owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) || owner.IsWellKnown(WellKnownSidType.LocalSystemSid))) return false;
        const FileSystemRights WriteRights = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & WriteRights) != 0 &&
                rule.IdentityReference is SecurityIdentifier sid && !sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) && !sid.IsWellKnown(WellKnownSidType.LocalSystemSid)) return false;
        return true;
    }
    private static bool TaskAclProtected(string sddl)
    {
        var descriptor = new RawSecurityDescriptor(sddl);
        var acl = descriptor.DiscretionaryAcl;
        if (acl is null || descriptor.Owner is not { } owner ||
            !(owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) || owner.IsWellKnown(WellKnownSidType.LocalSystemSid))) return false;
        foreach (var ace in acl)
            if (ace is CommonAce { AceQualifier: AceQualifier.AccessAllowed } allowed &&
                (allowed.AccessMask & 0x500D0002) != 0 &&
                !allowed.SecurityIdentifier.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) && !allowed.SecurityIdentifier.IsWellKnown(WellKnownSidType.LocalSystemSid)) return false;
        return true;
    }
    // COM 的常见 HRESULT 会被运行时映射为 FileNotFoundException 等托管异常。
    private static bool IsMissing(Exception error) => (uint)error.HResult is 0x80070002 or 0x80070003;
    private static bool SameUser(string user, string owner) => user == owner ||
        new NTAccount(user).Translate(typeof(SecurityIdentifier)).Value == owner;

    private sealed class TaskSession : IDisposable
    {
        private readonly List<object> _objects = [];
        public dynamic Service { get; }
        public TaskSession()
        {
            Service = Track(Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!);
            try { Service.Connect(); } catch { Dispose(); throw; }
        }
        public dynamic Track(object value) { _objects.Add(value); return value; }
        public dynamic Folder(bool create = false)
        {
            try { return Track(Service.GetFolder("\\" + FolderName)); }
            catch (Exception error) when (create && IsMissing(error))
            {
                dynamic root = Track(Service.GetFolder("\\"));
                return Track(root.CreateFolder(FolderName, "O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;BU)"));
            }
        }
        public void Dispose() { for (var i = _objects.Count - 1; i >= 0; i--) if (Marshal.IsComObject(_objects[i])) Marshal.ReleaseComObject(_objects[i]); }
    }
}
