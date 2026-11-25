using System.Collections;

namespace DesktopPet.Core;

/// <summary>Validated manifest durations and cumulative frame boundaries.</summary>
internal sealed class FrameTimeline(double[] durations, double[] boundaries) : IReadOnlyList<double>
{
    public int Count => durations.Length;
    public double this[int index] => durations[index];
    public IEnumerator<double> GetEnumerator() => ((IEnumerable<double>)durations).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int FindFrame(double elapsedMilliseconds)
    {
        var position = double.IsFinite(elapsedMilliseconds) && elapsedMilliseconds >= 0
            ? elapsedMilliseconds % boundaries[^1]
            : 0;
        var left = 0;
        var right = boundaries.Length - 1;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (position < boundaries[middle]) right = middle;
            else left = middle + 1;
        }
        return left;
    }
}
