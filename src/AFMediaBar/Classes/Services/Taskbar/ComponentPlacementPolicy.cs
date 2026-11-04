// 对象摆放的纯几何策略；输入均为同一任务栏客户区物理像素，不读取平台或设置。
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services;

/// <summary>从空闲区扣除其他对象，并按实际移动距离选择最近完整落点。</summary>
public static class ComponentPlacementPolicy
{
    public static IReadOnlyList<TaskbarPrimaryRange> Subtract(IEnumerable<TaskbarPrimaryRange> ranges,
        IEnumerable<TaskbarPrimaryRange> occupied, int gap = 0)
    {
        var free = ranges.Where(range => range.Length > 0).ToList();
        foreach (var item in occupied)
        {
            var start = item.Start - Math.Max(0, gap);
            var end = item.End + Math.Max(0, gap);
            free = free.SelectMany(range => end <= range.Start || start >= range.End ? new[] { range } :
                new[] { new TaskbarPrimaryRange(range.Start, Math.Min(range.End, start)),
                    new TaskbarPrimaryRange(Math.Max(range.Start, end), range.End) }.Where(part => part.Length > 0)).ToList();
        }
        return free.OrderBy(range => range.Start).ToArray();
    }

    public static int? Nearest(IEnumerable<TaskbarPrimaryRange> ranges, int desired, int length)
    {
        if (length <= 0) return null;
        return ranges.Where(range => range.Length >= length)
            .Select(range => (int?)Math.Clamp(desired, range.Start, range.End - length))
            .OrderBy(position => Math.Abs((long)position!.Value - desired)).ThenBy(position => position).FirstOrDefault();
    }
}
