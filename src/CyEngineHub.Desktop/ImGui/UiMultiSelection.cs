namespace CyEngineHub.Desktop.ImGui;

/// <summary>Stable-key selection in the current displayed order, independent of card realization.</summary>
internal sealed class UiMultiSelection
{
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private string[] _order = [];
    private string? _scope, _anchor;
    public IReadOnlyCollection<string> Keys => _keys;
    public bool Contains(string key) => _keys.Contains(key);
    public void Synchronize(string scope, IEnumerable<string> order)
    {
        if (_scope != scope) { _keys.Clear(); _anchor = null; _scope = scope; }
        _order = order.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _keys.IntersectWith(_order);
        if (_anchor is not null && !Array.Exists(_order, key => key.Equals(_anchor, StringComparison.OrdinalIgnoreCase))) _anchor = null;
    }
    public void Select(string key, bool control, bool shift, bool rightClick = false)
    {
        if (rightClick && _keys.Contains(key)) return;
        if (rightClick) { _keys.Clear(); _keys.Add(key); _anchor = key; return; }
        var anchor = _anchor is null ? -1 : Array.FindIndex(_order, item => item.Equals(_anchor, StringComparison.OrdinalIgnoreCase));
        var current = Array.FindIndex(_order, item => item.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (shift && anchor >= 0 && current >= 0)
        {
            if (!control) _keys.Clear();
            for (var index = Math.Min(anchor, current); index <= Math.Max(anchor, current); index++) _keys.Add(_order[index]);
            return;
        }
        if (!control) _keys.Clear();
        if (!_keys.Add(key) && control) _keys.Remove(key);
        _anchor = key;
    }
}
