using System.Text.Json;

namespace GamePacketCollector;

public sealed class PacketUploadConfig
{
    public const string DefaultUploadUrl = "https://ura.shuise.net/api/GamePackets";
    public const string SingleModeEndpointGroup = "single-mode";

    public string UploadUrl { get; set; } = DefaultUploadUrl;
    public string? ServerRegionHint { get; set; }
    public bool Enabled { get; set; }
    public string[] EndpointGroups { get; set; } = [SingleModeEndpointGroup];

    public static PacketUploadConfig LoadOrCreate(
        string path,
        JsonSerializerOptions jsonOptions,
        Func<PacketUploadConfig>? createConfig = null)
    {
        if (File.Exists(path))
        {
            var loaded = JsonSerializer.Deserialize<PacketUploadConfig>(File.ReadAllText(path), jsonOptions);
            if (loaded is null)
                throw new InvalidOperationException($"Invalid GamePacketCollector config: {path}");

            Validate(loaded, path);
            return loaded;
        }

        var config = createConfig?.Invoke() ?? new PacketUploadConfig();
        Validate(config, path);
        File.WriteAllText(path, JsonSerializer.Serialize(config, jsonOptions));
        return config;
    }

    static void Validate(PacketUploadConfig config, string path)
    {
        if (string.IsNullOrWhiteSpace(config.UploadUrl))
            throw new InvalidOperationException($"Invalid GamePacketCollector config: uploadUrl is required, path={path}");

        if (config.EndpointGroups is null || config.EndpointGroups.Length == 0)
            throw new InvalidOperationException($"Invalid GamePacketCollector config: endpointGroups is required, path={path}");

        var groups = config.EndpointGroups
            .Select(x => x.Trim())
            .ToArray();
        if (groups.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"Invalid GamePacketCollector config: endpointGroups contains an empty value, path={path}");

        config.EndpointGroups = groups
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (config.Enabled && !config.EndpointGroups.Contains(SingleModeEndpointGroup, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Invalid GamePacketCollector config: enabled upload must include mandatory endpoint group '{SingleModeEndpointGroup}', path={path}");
    }
}