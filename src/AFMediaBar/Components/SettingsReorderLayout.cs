// 计算变高列表的插入位置与占位位移，不修改集合或持有界面资源。
namespace AFMediaBar.Components;

internal static class SettingsReorderLayout
{
    internal static int FindTarget(double[] tops, double[] extents, int source, double center, int minimum)
    {
        var target = 0;
        for (var index = 0; index < tops.Length; index++)
            if (index != source && center > tops[index] + extents[index] / 2d) target++;
        return Math.Clamp(target, minimum, tops.Length - 1);
    }

    internal static double[] GetOffsets(double[] tops, double[] extents, int source, int target)
    {
        var order = Enumerable.Range(0, tops.Length).ToList();
        order.RemoveAt(source);
        order.Insert(target, source);
        var offsets = new double[tops.Length];
        var top = tops[0];
        foreach (var index in order)
        {
            offsets[index] = top - tops[index];
            top += extents[index];
        }
        return offsets;
    }
}
