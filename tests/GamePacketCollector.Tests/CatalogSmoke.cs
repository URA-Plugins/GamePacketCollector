using Gallop.Endpoints;
using UmamusumeResponseAnalyzer.Plugin;
static class CatalogSmoke
{
internal static void Run()
{
    var selected = GamePacketCollector.Capture.PacketCaptureCatalog.SelectedEndpoints;
    var expectedGroups = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["single-mode"] = 383,
        ["room-match"] = 18,
        ["race"] = 69,
        ["gacha"] = 3,
    };
    var actualGroups = selected.GroupBy(endpoint => endpoint.Group, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    if (!actualGroups.OrderBy(pair => pair.Key).SequenceEqual(expectedGroups.OrderBy(pair => pair.Key)))
        throw new InvalidOperationException(
            $"GamePacketCollector group counts mismatch: {string.Join(", ", actualGroups.Select(pair => $"{pair.Key}={pair.Value}"))}.");
    if (selected.Count != 473)
        throw new InvalidOperationException($"GamePacketCollector selected endpoint count must be 473, actual={selected.Count}.");

    var selectedPaths = selected.Select(endpoint => endpoint.Path).ToHashSet(StringComparer.Ordinal);
    var captureNamespaces = new HashSet<string>(StringComparer.Ordinal)
    {
        "generate_succession",
        "idle_single_mode",
        "pre_single_mode",
        "room_match",
        "challenge_match",
        "champions",
        "daily_legend_race",
        "daily_race",
        "legend_race",
        "main_story_race",
        "practice_race",
        "team_building",
        "team_stadium",
        "team_stadium_skip",
        "ultimate_race",
        "gacha",
    };
    var excluded = GameEndpointCatalog.ByPath.Keys.Count(path =>
    {
        var endpointNamespace = path.Split('/')[2];
        return (endpointNamespace == "single_mode"
                || endpointNamespace.StartsWith("single_mode_", StringComparison.Ordinal)
                || captureNamespaces.Contains(endpointNamespace))
            && !selectedPaths.Contains(path);
    });
    if (excluded != 131)
        throw new InvalidOperationException($"GamePacketCollector exclusion count must be 131, actual={excluded}.");

    var patterns = GamePacketCollector.Capture.PacketCaptureCatalog.BuildPatterns(selected);
    if (patterns.Count(pattern => pattern.Kind == EndpointPatternKind.Wildcard) != 21
        || patterns.Any(pattern => pattern.Kind == EndpointPatternKind.Wildcard
            && pattern.Pattern is "/umamusume/*" or "/umamusume/**"))
        throw new InvalidOperationException("GamePacketCollector must use exactly the 21 shared single-mode action wildcard families and no root wildcard.");
    var expanded = PluginManager.ExpandEndpointPatterns(patterns)
        .Select(endpoint => endpoint.Path)
        .ToHashSet(StringComparer.Ordinal);
    if (!expanded.SetEquals(selectedPaths))
        throw new InvalidOperationException("GamePacketCollector patterns do not round-trip to the exact 473 selected catalog endpoints.");
}

}
