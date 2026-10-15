namespace CyEngineHub.Desktop.ImGui;

internal readonly record struct UiVisibleRange(int Start, int End, float Offset, float TotalHeight);

// Variable-height virtualisation: wrapped descriptions must not overlap neighbouring rows.
internal sealed class UiListViewport
{
    private float[] _heights = [];
    private readonly float _estimate;
    public UiListViewport(float estimate = 90) => _estimate = Math.Max(1, estimate);

    public UiVisibleRange Range(int count, float scroll, float viewport)
    {
        if (_heights.Length != count) _heights = new float[count];
        float total = 0, offset = 0;
        var start = 0;
        var end = count;
        var foundStart = false;
        var foundEnd = false;
        for (var index = 0; index < count; index++)
        {
            var height = _heights[index] > 0 ? _heights[index] : _estimate;
            if (!foundStart && total + height >= Math.Max(0, scroll - _estimate))
            {
                start = index;
                offset = total;
                foundStart = true;
            }
            if (!foundEnd && total >= scroll + viewport + _estimate)
            {
                end = index;
                foundEnd = true;
            }
            total += height;
        }
        if (!foundStart) { start = count; offset = total; }
        return new(start, end, offset, total);
    }

    public void Measure(int index, float height)
    {
        if (index >= 0 && index < _heights.Length) _heights[index] = Math.Max(1, height);
    }
}
