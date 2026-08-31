using CyEngineHub.Toolchains.Models;

namespace CyEngineHub.Toolchains.Devices;

public static class AdbDeviceParser
{
    public static IReadOnlyList<AndroidDeviceSnapshot> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var devices = new List<AndroidDeviceSnapshot>();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith('*'))
            {
                continue;
            }

            var tokens = line.Split(
                [' ', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length < 2)
            {
                continue;
            }

            var properties = tokens.Skip(2)
                .Select(token => token.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First()[1], StringComparer.OrdinalIgnoreCase);
            properties.TryGetValue("model", out var model);
            properties.TryGetValue("product", out var product);
            properties.TryGetValue("device", out var device);
            properties.TryGetValue("transport_id", out var transportId);
            var identity = $"{model} {product} {device}";
            devices.Add(new AndroidDeviceSnapshot(
                tokens[0],
                tokens[1],
                Normalize(model),
                Normalize(product),
                Normalize(device),
                transportId ?? string.Empty,
                IsQuest(identity)));
        }

        return devices;
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Inconnu" : value.Replace('_', ' ');

    private static bool IsQuest(string value) =>
        value.Contains("quest", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("hollywood", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("monterey", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("eureka", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("panther", StringComparison.OrdinalIgnoreCase);
}
