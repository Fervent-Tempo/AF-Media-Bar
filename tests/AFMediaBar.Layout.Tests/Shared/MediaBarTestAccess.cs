// 让既有呈现回归在资源所有者提取后继续准备和观察原有行为；仅搜索两个明确的控件级所有者。
using System.Reflection;
using AFMediaBar.Components;

namespace AFMediaBar.Layout.Tests;

internal static class MediaBarTestAccess
{
    internal static Member GetField(string name, BindingFlags flags) => new(name, flags, false);
    internal static Member GetMethod(string name, BindingFlags flags) => new(name, flags, true);

    internal sealed class Member(string name, BindingFlags flags, bool method)
    {
        private (object? Target, MemberInfo Info) Resolve(object? target)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MemberInfo? Find(Type type) => method ? type.GetMethod(name, flags) : type.GetField(name, flags);
            if (Find(typeof(TaskBarMediaControl)) is { } own) return (target, own);
            foreach (var ownerName in new[] { "_artworkHover", "_lyrics" })
            {
                var owner = typeof(TaskBarMediaControl).GetField(ownerName, all)!.GetValue(target)!;
                if (Find(owner.GetType()) is { } member) return (owner, member);
            }
            throw new MissingMemberException(typeof(TaskBarMediaControl).FullName, name);
        }

        internal object? GetValue(object? target)
        {
            var member = Resolve(target);
            return ((FieldInfo)member.Info).GetValue(member.Target);
        }

        internal void SetValue(object? target, object? value)
        {
            var member = Resolve(target);
            ((FieldInfo)member.Info).SetValue(member.Target, value);
        }

        internal object? Invoke(object? target, object?[]? arguments)
        {
            var member = Resolve(target);
            return ((MethodInfo)member.Info).Invoke(member.Target, arguments);
        }
    }
}
