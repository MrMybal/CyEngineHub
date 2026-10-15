namespace CyEngineHub.Desktop.ImGui;

// Card size never depends on the number of items or on the length of their text.
internal readonly record struct UiCardGrid(float Width, float Height, float Gap, int Columns)
{
    public const float InterfaceScale = 1.15f;
    public float RowStride => Height + Gap;
    public static UiCardGrid Create(float available, float width, float height, float gap = 10) =>
        new(width, height, gap, Math.Max(1, (int)Math.Floor((available + gap) / (width + gap))));
    public int Rows(int count) => (count + Columns - 1) / Columns;
    public (int Start, int End) VisibleRows(int count, float scroll, float viewport) =>
        (Math.Clamp((int)(scroll / RowStride) - 1, 0, Rows(count)),
         Math.Clamp((int)Math.Ceiling((scroll + viewport) / RowStride) + 1, 0, Rows(count)));
}
