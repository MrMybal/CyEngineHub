using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace CyEngineHub.Desktop.ImGui;

// Cached accessors keep the immediate-mode frame free of repeated reflection scans.
internal static class UiBinding
{
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo[]> Properties = new();

    public static string? Path(string? expression)
    {
        if (expression is null || !expression.StartsWith("{Binding", StringComparison.Ordinal)) return null;
        return expression[8..].Trim().TrimEnd('}').Split(',')[0].Trim();
    }

    public static object? Read(object? context, string? expression)
    {
        var path = Path(expression);
        if (path is null) return expression;
        if (context is null) return null;
        var negate = path.StartsWith('!');
        if (negate) path = path[1..];
        object? value = context;
        foreach (var component in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value is null) break;
            value = value is IReadOnlyDictionary<string, object?> map ? map.GetValueOrDefault(component) : Property(value.GetType(), component)?.GetValue(value);
        }
        return negate ? value is not true : value;
    }

    public static bool Boolean(object? context, string? expression, bool fallback = true)
    {
        if (expression is null) return fallback;
        var value = Read(context, expression);
        return value is bool flag ? flag : bool.TryParse(value?.ToString(), out var parsed) && parsed;
    }

    public static string Text(object? context, string? expression)
    {
        var value = Read(context, expression);
        if (Path(expression) is not null && expression!.IndexOf("StringFormat=", StringComparison.Ordinal) is var index && index >= 0)
        {
            // Remove only the binding's final brace, keeping composite placeholders.
            var format = expression[(index + 13)..^1].Trim().Trim('\'', '"');
            if (format.StartsWith("{}", StringComparison.Ordinal)) format = format[2..];
            try { return string.Format(CultureInfo.CurrentCulture, format, value); }
            catch (FormatException) { }
        }
        return value?.ToString() ?? "";
    }

    public static void Write(object? context, string? expression, object? value)
    {
        var path = Path(expression);
        if (path is null || path.StartsWith('!') || context is null) return;
        if (context is IDictionary<string, object?> map && !path.Contains('.', StringComparison.Ordinal))
        {
            map[path] = value;
            return;
        }
        var components = path.Split('.');
        foreach (var component in components[..^1])
        {
            context = context is null ? null : Property(context.GetType(), component)?.GetValue(context);
        }
        if (context is null || Property(context.GetType(), components[^1]) is not { CanWrite: true } property) return;
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (value is not null && !type.IsInstanceOfType(value)) value = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        if (!Equals(property.GetValue(context), value)) property.SetValue(context, value);
    }

    private static PropertyInfo? Property(Type type, string name) =>
        Properties.GetOrAdd((type, name), key => key.Type.GetProperty(key.Name) is { } found ? [found] : []).FirstOrDefault();
}
