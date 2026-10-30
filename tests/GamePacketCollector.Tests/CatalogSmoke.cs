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
    var patterns = GamePacketCollector.Capture.PacketCaptureCatalog.BuildPatterns(selected);
    if (patterns.Any(pattern => pattern.Kind == EndpointPatternKind.Wildcard
            && pattern.Pattern is "/umamusume/*" or "/umamusume/**"))
        throw new InvalidOperationException("GamePacketCollector must not use root wildcards.");
    var expanded = PluginManager.ExpandEndpointPatterns(patterns)
        .Select(endpoint => endpoint.Path)
        .ToHashSet(StringComparer.Ordinal);
    if (!expanded.SetEquals(selectedPaths))
        throw new InvalidOperationException("GamePacketCollector patterns do not round-trip to the exact 473 selected catalog endpoints.");
}

}
