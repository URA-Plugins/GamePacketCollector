using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Gallop.Endpoints;
using GamePacketCollector;
using GamePacketCollector.Capture;
using Spectre.Console.Rendering;
using UmamusumeResponseAnalyzer.LiveDisplay;
using UmamusumeResponseAnalyzer.Plugin;

var tests = new (string Name, Action Body)[]
{
    ("PacketCaptureCatalog keeps representative endpoint paths explicit", PacketCaptureCatalogKeepsRepresentativeEndpointPathsExplicit),
    ("PacketCaptureCatalog excludes player controlled setup endpoints", PacketCaptureCatalogExcludesPlayerControlledSetupEndpoints),
    ("PacketExchangeBuffer pairs request and response per endpoint", PacketExchangeBufferPairsRequestAndResponsePerEndpoint),
    ("PacketExchangeBuffer creates deterministic PacketIdemKey", PacketExchangeBufferCreatesDeterministicPacketIdemKey),
    ("PacketExchangeBuffer requires PacketIdemKey headers", PacketExchangeBufferRequiresPacketIdemKeyHeaders),
    ("PacketExchangeBuffer drops unmatched response", PacketExchangeBufferDropsUnmatchedResponse),
    ("PacketUploadEnvelope creates GamePackets upload body", PacketUploadEnvelopeCreatesGamePacketsUploadBody),
    ("PacketUploader sends PUT JSON to configured endpoint", PacketUploaderSendsPutJsonToConfiguredEndpoint),
    ("PacketUploader classifies permanent and retriable failures", PacketUploaderClassifiesPermanentAndRetriableFailures),
    ("PacketUploadConfig persists first run endpoint selection", PacketUploadConfigPersistsFirstRunEndpointSelection),
    ("PacketUploadConfig requires single-mode when enabled", PacketUploadConfigRequiresSingleModeWhenEnabled),
    ("Plugin rejects removed event endpoint group", PluginRejectsRemovedEventEndpointGroup),
    ("Plugin does not register analyzers when upload is disabled", PluginDoesNotRegisterAnalyzersWhenUploadIsDisabled),
    ("Plugin registers raw analyzers for configured endpoint groups", PluginRegistersRawAnalyzersForConfiguredEndpointGroups),
    ("Programmatic analyzer handler writes pending exchange", ProgrammaticAnalyzerHandlerWritesPendingExchange),
    ("Plugin deletes pending after successful upload without writing sent", PluginDeletesPendingAfterSuccessfulUploadWithoutWritingSent),
    ("Plugin moves permanent upload failure to failed", PluginMovesPermanentUploadFailureToFailed),
    ("Plugin keeps pending file after retriable upload failure", PluginKeepsPendingFileAfterRetriableUploadFailure),
};

foreach (var (name, body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}");
        Console.Error.WriteLine(ex);
        Environment.Exit(1);
    }
}

static void PacketCaptureCatalogKeepsRepresentativeEndpointPathsExplicit()
{
    var selectedEndpoints = PacketCaptureCatalog.SelectedEndpoints
        .ToDictionary(x => x.Path, StringComparer.Ordinal);

    AssertEndpoint(selectedEndpoints, "/single_mode/exec_command", "single-mode");
    AssertEndpoint(selectedEndpoints, "/single_mode/start", "single-mode");
    AssertEndpoint(selectedEndpoints, "/single_mode_arc/start", "single-mode");
    AssertEndpoint(selectedEndpoints, "/single_mode_onsen/start", "single-mode");
    AssertEndpoint(selectedEndpoints, "/single_mode_ramen/check_event", "single-mode");
    AssertEndpoint(selectedEndpoints, "/single_mode_pioneer/check_point", "single-mode");
    AssertEndpoint(selectedEndpoints, "/single_mode_ramen/check_point", "single-mode");
    AssertEndpoint(selectedEndpoints, "/single_mode_cook/tasting", "single-mode");
    AssertEndpoint(selectedEndpoints, "/single_mode_ramen/tasting", "single-mode");
    AssertEndpoint(selectedEndpoints, "/single_mode_ramen/save_skill_filter_set", "single-mode");
    AssertEndpoint(selectedEndpoints, "/single_mode_arc/arc_race_analyze", "single-mode");
    AssertEndpoint(selectedEndpoints, "/single_mode_breeders/bc_race_entry", "single-mode");
    AssertEndpoint(selectedEndpoints, "/generate_succession/finish", "single-mode");
    AssertEndpoint(selectedEndpoints, "/room_match/create_room", "room-match");
    AssertEndpoint(selectedEndpoints, "/team_stadium/start", "race");
    AssertEndpoint(selectedEndpoints, "/gacha/exec", "gacha");

    AssertEndpointMissing(selectedEndpoints, "/single_mode_arc/check_point");
    AssertEndpointMissing(selectedEndpoints, "/single_mode_arc/tasting");
    AssertEndpointMissing(selectedEndpoints, "/single_mode_onsen/arc_race_analyze");
    AssertEndpointMissing(selectedEndpoints, "/story_event/roulette_exec");
    AssertEndpointMissing(selectedEndpoints, "/factor_research/factor_update");
    AssertTrue(!selectedEndpoints.Values.Any(x => x.Group == "event"), "Unexpected selected event endpoint group.");
}

static void AssertEndpoint(IReadOnlyDictionary<string, PacketCaptureEndpoint> selectedEndpoints, string path, string group)
{
    var catalogPath = "/umamusume" + path;
    if (!selectedEndpoints.TryGetValue(catalogPath, out var endpoint))
        throw new InvalidOperationException($"Missing selected endpoint: {catalogPath}");

    AssertEqual(catalogPath, endpoint.Path);
    AssertEqual(group, endpoint.Group);
    AssertTrue(GameEndpointCatalog.ByPath.ContainsKey(catalogPath), $"Gallop endpoint path not found: {catalogPath}");
}

static void AssertEndpointMissing(IReadOnlyDictionary<string, PacketCaptureEndpoint> selectedEndpoints, string path)
{
    var catalogPath = "/umamusume" + path;
    if (selectedEndpoints.ContainsKey(catalogPath))
        throw new InvalidOperationException($"Unexpected selected endpoint: {catalogPath}");
}

static void PacketCaptureCatalogExcludesPlayerControlledSetupEndpoints()
{
    var selectedEndpoints = PacketCaptureCatalog.SelectedEndpoints
        .Select(x => x.EndpointType)
        .ToHashSet();

    foreach (var endpoint in new[]
    {
        typeof(GameApi.GenerateSuccession.ChangeSupportCardDeck),
        typeof(GameApi.GenerateSuccession.ChangeSupportCardDeckName),
        typeof(GameApi.GenerateSuccession.CharaExtendSaveCapacity),
        typeof(GameApi.GenerateSuccession.Remove),
        typeof(GameApi.GenerateSuccession.SavePreset),
        typeof(GameApi.GenerateSuccession.SaveRaceDeck),
        typeof(GameApi.GenerateSuccession.UpdateSuccessionDeckSet),
        typeof(GameApi.IdleSingleMode.MultiRaceReserveDeck),
        typeof(GameApi.PracticeRace.ChangeFavoriteRace),
        typeof(GameApi.PracticeRace.ChangePresetName),
        typeof(GameApi.PracticeRace.GetSavedRaceList),
        typeof(GameApi.PracticeRace.SavePreset),
        typeof(GameApi.RoomMatch.ChangePresetName),
        typeof(GameApi.RoomMatch.ChangeRaceConditionName),
        typeof(GameApi.RoomMatch.DeleteRaceCondition),
        typeof(GameApi.RoomMatch.GetMyRaceConditionList),
        typeof(GameApi.RoomMatch.GetPresetArray),
        typeof(GameApi.RoomMatch.GetRecommendRaceConditionList),
        typeof(GameApi.RoomMatch.SavePreset),
        typeof(GameApi.RoomMatch.SaveRaceCondition),
        typeof(GameApi.SingleMode.ChangeRunningStyle),
        typeof(GameApi.SingleModeBreeders.ChangeDressOption),
        typeof(GameApi.SingleModeBreeders.ChangeRunningStyle),
        typeof(GameApi.SingleModePioneer.PriorityFacilitySave),
        typeof(GameApi.SingleModeTeam.SaveTeamEditFlag),
        typeof(GameApi.SingleModeVenus.ChangeRunningStyle),
    })
    {
        AssertTrue(!selectedEndpoints.Contains(endpoint), $"Unexpected selected endpoint: {endpoint.FullName}");
    }
}

static void PacketExchangeBufferPairsRequestAndResponsePerEndpoint()
{
    var buffer = new PacketExchangeBuffer();
    var endpoint = PacketCaptureCatalog.SelectedEndpoints.Single(x => x.EndpointType == typeof(GameApi.Gacha.Exec));
    var headers = TestHeaders();

    buffer.RecordRequest(endpoint, [0x01, 0x02], headers);

    var exchange = buffer.RecordResponse(endpoint, [0x03, 0x04]);

    AssertTrue(exchange is not null);
    AssertTrue(!string.IsNullOrWhiteSpace(exchange!.PacketIdemKey));
    AssertTrue(!exchange.PacketIdemKey.StartsWith("game-packet-collector:", StringComparison.Ordinal));
    AssertEqual(endpoint.EndpointType.FullName, exchange.EndpointType);
    AssertEqual(endpoint.Path, exchange.EndpointPath);
    AssertEqual(endpoint.Group, exchange.Group);
    AssertEqual("1.2.3", exchange.AppVersion);
    AssertEqual("2026070301", exchange.GameDataVersion);
    AssertEqual("123456789", exchange.ViewerId);
    AssertEqual("sid-1", exchange.Sid);
    AssertEqual("android", exchange.Device);
    AssertEqual("phone", exchange.DeviceSubtype);
    AssertBytes([0x01, 0x02], exchange.Request);
    AssertBytes([0x03, 0x04], exchange.Response);
}

static void PacketExchangeBufferCreatesDeterministicPacketIdemKey()
{
    var baseline = CreateExchange([0x01, 0x02], [0x03, 0x04]);
    var same = CreateExchange([0x01, 0x02], [0x03, 0x04]);
    var changedRequest = CreateExchange([0x01, 0x03], [0x03, 0x04]);
    var changedResponse = CreateExchange([0x01, 0x02], [0x03, 0x05]);
    var changedViewer = CreateExchange([0x01, 0x02], [0x03, 0x04], TestHeaders() with { ViewerId = "987654321" });
    var changedAppVersion = CreateExchange([0x01, 0x02], [0x03, 0x04], TestHeaders() with { AppVer = "9.9.9" });
    var changedGameDataVersion = CreateExchange([0x01, 0x02], [0x03, 0x04], TestHeaders() with { ResVer = "2026070401" });
    var roomMatchEndpoint = PacketCaptureCatalog.SelectedEndpoints.Single(x => x.EndpointType == typeof(GameApi.RoomMatch.CreateRoom));
    var changedEndpoint = CreateExchange([0x01, 0x02], [0x03, 0x04], endpoint: roomMatchEndpoint);

    AssertEqual(baseline.PacketIdemKey, same.PacketIdemKey);
    AssertNotEqual(baseline.PacketIdemKey, changedRequest.PacketIdemKey);
    AssertNotEqual(baseline.PacketIdemKey, changedResponse.PacketIdemKey);
    AssertNotEqual(baseline.PacketIdemKey, changedViewer.PacketIdemKey);
    AssertNotEqual(baseline.PacketIdemKey, changedAppVersion.PacketIdemKey);
    AssertNotEqual(baseline.PacketIdemKey, changedGameDataVersion.PacketIdemKey);
    AssertNotEqual(baseline.PacketIdemKey, changedEndpoint.PacketIdemKey);
}

static void PacketExchangeBufferRequiresPacketIdemKeyHeaders()
{
    AssertPacketIdemKeyHeaderRequired(TestHeaders() with { ViewerId = null }, "X-Hachimi-viewerid");
    AssertPacketIdemKeyHeaderRequired(TestHeaders() with { ResVer = null }, "X-Hachimi-res-ver");
    AssertPacketIdemKeyHeaderRequired(TestHeaders() with { AppVer = null }, "X-Hachimi-app-ver");
}

static void AssertPacketIdemKeyHeaderRequired(GameHttpHeaders headers, string headerName)
{
    var endpoint = PacketCaptureCatalog.SelectedEndpoints.Single(x => x.EndpointType == typeof(GameApi.Gacha.Exec));
    var buffer = new PacketExchangeBuffer();

    var error = AssertThrows<InvalidOperationException>(() => buffer.RecordRequest(endpoint, [0x01, 0x02], headers));

    AssertTrue(error.Message.Contains(headerName, StringComparison.Ordinal), error.Message);
}
static void PacketExchangeBufferDropsUnmatchedResponse()
{
    var buffer = new PacketExchangeBuffer();
    var endpoint = PacketCaptureCatalog.SelectedEndpoints.Single(x => x.EndpointType == typeof(GameApi.Gacha.Exec));

    var exchange = buffer.RecordResponse(endpoint, [0x03, 0x04]);

    AssertTrue(exchange is null);
}

static void PacketUploadEnvelopeCreatesGamePacketsUploadBody()
{
    var endpoint = PacketCaptureCatalog.SelectedEndpoints.Single(x => x.EndpointType == typeof(GameApi.Gacha.Exec));
    var exchange = new PacketCaptureExchange(
        PacketIdemKey: "idem-key",
        EndpointType: endpoint.EndpointType.FullName!,
        EndpointPath: endpoint.Path,
        Group: endpoint.Group,
        AppVersion: "1.2.3",
        GameDataVersion: "2026070301",
        ViewerId: "123456789",
        Sid: "sid-1",
        Device: "android",
        DeviceSubtype: "phone",
        Request: [0x01, 0x02],
        Response: [0x03, 0x04],
        CapturedAt: DateTimeOffset.Parse("2026-07-03T12:30:00+08:00"));

    var envelope = PacketUploadEnvelope.FromExchange(exchange);

    AssertEqual("1", envelope.SchemaVersion);
    AssertEqual("idem-key", envelope.PacketIdemKey);
    AssertEqual(endpoint.EndpointType.FullName, envelope.EndpointType);
    AssertEqual(endpoint.Path, envelope.EndpointPath);
    AssertEqual(endpoint.Group, envelope.Group);
    AssertEqual("1.2.3", envelope.AppVersion);
    AssertEqual("2026070301", envelope.GameDataVersion);
    AssertEqual("123456789", envelope.ViewerId);
    AssertEqual("sid-1", envelope.Sid);
    AssertEqual("android", envelope.Device);
    AssertEqual("phone", envelope.DeviceSubtype);
    AssertBytes([0x01, 0x02], envelope.Request);
    AssertBytes([0x03, 0x04], envelope.Response);
}
static void PacketUploaderSendsPutJsonToConfiguredEndpoint()
{
    var handler = new RecordingHttpHandler(HttpStatusCode.NoContent);
    using var httpClient = new HttpClient(handler);
    var uploader = new PacketUploader(httpClient, "https://ura.example/api/GamePackets");

    uploader.UploadAsync("{\"packetIdemKey\":\"x\"}", CancellationToken.None).GetAwaiter().GetResult();

    AssertEqual("PUT", handler.Method);
    AssertEqual("https://ura.example/api/GamePackets", handler.RequestUri);
    AssertEqual("application/json; charset=utf-8", handler.ContentType);
    AssertEqual("{\"packetIdemKey\":\"x\"}", handler.Body);
}
static void PacketUploaderClassifiesPermanentAndRetriableFailures()
{
    var permanent = UploadFailure(HttpStatusCode.BadRequest, "bad packet");
    AssertEqual(HttpStatusCode.BadRequest, permanent.StatusCode);
    AssertTrue(!permanent.IsRetriable);
    AssertTrue(permanent.ResponseBody.Contains("bad packet", StringComparison.Ordinal), permanent.ResponseBody);

    var rateLimited = UploadFailure(HttpStatusCode.TooManyRequests, "slow down");
    AssertTrue(rateLimited.IsRetriable);

    var serverError = UploadFailure(HttpStatusCode.InternalServerError, "boom");
    AssertTrue(serverError.IsRetriable);
}

static PacketUploadException UploadFailure(HttpStatusCode statusCode, string responseBody)
{
    var handler = new RecordingHttpHandler(statusCode, responseBody);
    using var httpClient = new HttpClient(handler);
    var uploader = new PacketUploader(httpClient, "https://ura.example/api/GamePackets");

    return AssertThrows<PacketUploadException>(() =>
        uploader.UploadAsync("{\"packetIdemKey\":\"x\"}", CancellationToken.None).GetAwaiter().GetResult());
}

static void PacketUploadConfigPersistsFirstRunEndpointSelection()
{
    using var workspace = TempWorkspace.CreateWithoutConfig();
    var configPath = Path.Combine(workspace.Path, "PluginData", "游戏包采集", "config.json");
    var config = PacketUploadConfig.LoadOrCreate(
        configPath,
        new JsonSerializerOptions(JsonSerializerDefaults.Web),
        () => new PacketUploadConfig
        {
            Enabled = true,
            EndpointGroups = ["single-mode", "gacha", "room-match"],
        });

    AssertTrue(config.Enabled);
    AssertArrayEqual(["single-mode", "gacha", "room-match"], config.EndpointGroups);

    using var document = JsonDocument.Parse(File.ReadAllText(configPath));
    AssertTrue(document.RootElement.GetProperty("enabled").GetBoolean());
    AssertArrayEqual(
        ["single-mode", "gacha", "room-match"],
        document.RootElement.GetProperty("endpointGroups").EnumerateArray().Select(x => x.GetString()!).ToArray());
}

static void PacketUploadConfigRequiresSingleModeWhenEnabled()
{
    using var workspace = TempWorkspace.CreateWithoutConfig();
    var configPath = Path.Combine(workspace.Path, "PluginData", "游戏包采集", "config.json");

    var error = AssertThrows<InvalidOperationException>(() =>
        PacketUploadConfig.LoadOrCreate(
            configPath,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            () => new PacketUploadConfig
            {
                Enabled = true,
                EndpointGroups = ["gacha"],
            }));

    AssertTrue(error.Message.Contains("single-mode", StringComparison.Ordinal), error.Message);
}

static void PluginRejectsRemovedEventEndpointGroup()
{
    using var workspace = TempWorkspace.Create(EnabledConfigJson("single-mode", "event"));
    using var currentDirectory = CurrentDirectoryScope.Enter(workspace.Path);
    var registry = new RecordingAnalyzerRegistry();
    var plugin = new GamePacketCollectorPlugin();

    try
    {
        var error = AssertThrows<InvalidOperationException>(() => plugin.Initialize(new FakePluginContext(registry)));
        AssertTrue(error.Message.Contains("event", StringComparison.Ordinal), error.Message);
    }
    finally
    {
        plugin.Dispose();
    }
}
static void PluginDoesNotRegisterAnalyzersWhenUploadIsDisabled()
{
    using var workspace = TempWorkspace.Create();
    using var currentDirectory = CurrentDirectoryScope.Enter(workspace.Path);
    var registry = new RecordingAnalyzerRegistry();
    var plugin = new GamePacketCollectorPlugin();

    try
    {
        plugin.Initialize(new FakePluginContext(registry));

        AssertTrue(registry.RequestEndpoints.Count == 0, "Disabled plugin should not register request analyzers.");
        AssertTrue(registry.ResponseEndpoints.Count == 0, "Disabled plugin should not register response analyzers.");
    }
    finally
    {
        plugin.Dispose();
    }
}

static void PluginRegistersRawAnalyzersForConfiguredEndpointGroups()
{
    using var workspace = TempWorkspace.Create(EnabledConfigJson("single-mode", "gacha"));
    using var currentDirectory = CurrentDirectoryScope.Enter(workspace.Path);
    var registry = new RecordingAnalyzerRegistry();
    var plugin = new GamePacketCollectorPlugin();

    try
    {
        plugin.Initialize(new FakePluginContext(registry));

        var selectedEndpoints = PacketCaptureCatalog.SelectedEndpoints
            .Where(x => x.Group is "single-mode" or "gacha")
            .Select(x => x.EndpointType)
            .ToHashSet();

        AssertSetEqual(selectedEndpoints, registry.RequestEndpoints, "raw request analyzer endpoints");
        AssertSetEqual(selectedEndpoints, registry.ResponseEndpoints, "raw response analyzer endpoints");
    }
    finally
    {
        plugin.Dispose();
    }
}

static void ProgrammaticAnalyzerHandlerWritesPendingExchange()
{
    using var workspace = TempWorkspace.Create(EnabledConfigJson("single-mode", "gacha"));
    using var currentDirectory = CurrentDirectoryScope.Enter(workspace.Path);
    var registry = new RecordingAnalyzerRegistry();
    var plugin = new GamePacketCollectorPlugin();

    try
    {
        plugin.Initialize(new FakePluginContext(registry));
        registry.RequestHandlers[typeof(GameApi.Gacha.Exec)]([0x01, 0x02], TestHeaders()).GetAwaiter().GetResult();
        registry.ResponseHandlers[typeof(GameApi.Gacha.Exec)]([0x03, 0x04], GameHttpHeaders.Empty).GetAwaiter().GetResult();

        using var packet = workspace.ReadPluginPacket(plugin.Name);
        AssertEqual("1", packet.RootElement.GetProperty("schemaVersion").GetString());
        AssertEqual("game-packet", packet.RootElement.GetProperty("kind").GetString());
        AssertTrue(!packet.RootElement.TryGetProperty("clientSessionId", out _));
        AssertTrue(!packet.RootElement.TryGetProperty("sequence", out _));
        AssertEqual(CreateExchange([0x01, 0x02], [0x03, 0x04]).PacketIdemKey, packet.RootElement.GetProperty("packetIdemKey").GetString());
        AssertEqual(typeof(GameApi.Gacha.Exec).FullName, packet.RootElement.GetProperty("endpointType").GetString());
        AssertEqual("/umamusume/gacha/exec", packet.RootElement.GetProperty("endpointPath").GetString());
        AssertEqual("gacha", packet.RootElement.GetProperty("group").GetString());
        AssertEqual("1.2.3", packet.RootElement.GetProperty("appVersion").GetString());
        AssertEqual("2026070301", packet.RootElement.GetProperty("gameDataVersion").GetString());
        AssertEqual("123456789", packet.RootElement.GetProperty("viewerId").GetString());
        AssertEqual("sid-1", packet.RootElement.GetProperty("sid").GetString());
        AssertEqual("android", packet.RootElement.GetProperty("device").GetString());
        AssertEqual("phone", packet.RootElement.GetProperty("deviceSubtype").GetString());
        AssertEqual("AQI=", packet.RootElement.GetProperty("request").GetString());
        AssertEqual("AwQ=", packet.RootElement.GetProperty("response").GetString());
        AssertTrue(DateTimeOffset.TryParse(packet.RootElement.GetProperty("capturedAt").GetString(), out _));
    }
    finally
    {
        plugin.Dispose();
    }
}


static void PluginDeletesPendingAfterSuccessfulUploadWithoutWritingSent()
{
    using var server = new SingleRequestHttpServer(HttpStatusCode.NoContent);
    using var workspace = TempWorkspace.Create(EnabledConfigJsonForUrl(server.Url, "single-mode", "gacha"));
    using var currentDirectory = CurrentDirectoryScope.Enter(workspace.Path);
    var registry = new RecordingAnalyzerRegistry();
    var plugin = new GamePacketCollectorPlugin();

    try
    {
        plugin.Initialize(new FakePluginContext(registry));
        CaptureGachaPacket(registry);

        server.WaitForRequest();
        WaitUntil(
            () => JsonFileCount(PendingDirectory(workspace, plugin.Name)) == 0,
            TimeSpan.FromSeconds(3),
            "Expected successful upload to delete pending packet.");

        AssertEqual(1, server.RequestCount);
        AssertEqual("PUT", server.Method);
        AssertEqual("/api/GamePackets", server.Path);
        using var uploaded = JsonDocument.Parse(server.Body ?? throw new InvalidOperationException("Expected upload body."));
        AssertEqual(JsonValueKind.Object, uploaded.RootElement.ValueKind);
        AssertEqual("game-packet", uploaded.RootElement.GetProperty("kind").GetString());
        AssertTrue(
            !Directory.Exists(SentDirectory(workspace, plugin.Name)) || JsonFileCount(SentDirectory(workspace, plugin.Name)) == 0,
            "Successful upload should not write sent packet files.");
    }
    finally
    {
        plugin.Dispose();
    }
}

static void PluginMovesPermanentUploadFailureToFailed()
{
    using var server = new SingleRequestHttpServer(HttpStatusCode.BadRequest, "bad packet");
    using var workspace = TempWorkspace.Create(EnabledConfigJsonForUrl(server.Url, "single-mode", "gacha"));
    using var currentDirectory = CurrentDirectoryScope.Enter(workspace.Path);
    var registry = new RecordingAnalyzerRegistry();
    var plugin = new GamePacketCollectorPlugin();

    try
    {
        plugin.Initialize(new FakePluginContext(registry));
        CaptureGachaPacket(registry);

        server.WaitForRequest();
        WaitUntil(
            () => JsonFileCount(PendingDirectory(workspace, plugin.Name)) == 0 &&
                  JsonFileCount(FailedDirectory(workspace, plugin.Name)) == 1,
            TimeSpan.FromSeconds(3),
            "Expected permanent upload failure to move packet from pending to failed.");

        AssertEqual(1, server.RequestCount);
    }
    finally
    {
        plugin.Dispose();
    }
}

static void PluginKeepsPendingFileAfterRetriableUploadFailure()
{
    using var server = new SingleRequestHttpServer(HttpStatusCode.InternalServerError, "boom");
    using var workspace = TempWorkspace.Create(EnabledConfigJsonForUrl(server.Url, "single-mode", "gacha"));
    using var currentDirectory = CurrentDirectoryScope.Enter(workspace.Path);
    var registry = new RecordingAnalyzerRegistry();
    var plugin = new GamePacketCollectorPlugin();

    try
    {
        plugin.Initialize(new FakePluginContext(registry));
        CaptureGachaPacket(registry);

        server.WaitForRequest();
        WaitUntil(
            () => JsonFileCount(PendingDirectory(workspace, plugin.Name)) == 1,
            TimeSpan.FromSeconds(3),
            "Expected retriable upload failure to keep packet in pending.");
        AssertEqual(0, JsonFileCount(FailedDirectory(workspace, plugin.Name)));
        AssertEqual(1, server.RequestCount);
    }
    finally
    {
        plugin.Dispose();
    }
}

static void CaptureGachaPacket(RecordingAnalyzerRegistry registry)
{
    registry.RequestHandlers[typeof(GameApi.Gacha.Exec)]([0x01, 0x02], TestHeaders()).GetAwaiter().GetResult();
    registry.ResponseHandlers[typeof(GameApi.Gacha.Exec)]([0x03, 0x04], GameHttpHeaders.Empty).GetAwaiter().GetResult();
}

static string PendingDirectory(TempWorkspace workspace, string pluginName)
    => Path.Combine(workspace.Path, "PluginData", pluginName, "pending");

static string FailedDirectory(TempWorkspace workspace, string pluginName)
    => Path.Combine(workspace.Path, "PluginData", pluginName, "failed");

static string SentDirectory(TempWorkspace workspace, string pluginName)
    => Path.Combine(workspace.Path, "PluginData", pluginName, "sent");

static int JsonFileCount(string directory)
    => Directory.Exists(directory)
        ? Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Length
        : 0;

static void WaitUntil(Func<bool> condition, TimeSpan timeout, string failureMessage)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (condition())
            return;

        Thread.Sleep(20);
    }

    throw new InvalidOperationException(failureMessage);
}

static PacketCaptureExchange CreateExchange(
    byte[] request,
    byte[] response,
    GameHttpHeaders? headers = null,
    PacketCaptureEndpoint? endpoint = null)
{
    var selectedEndpoint = endpoint ?? PacketCaptureCatalog.SelectedEndpoints.Single(x => x.EndpointType == typeof(GameApi.Gacha.Exec));
    var buffer = new PacketExchangeBuffer();
    buffer.RecordRequest(selectedEndpoint, request, headers ?? TestHeaders());
    return buffer.RecordResponse(selectedEndpoint, response) ?? throw new InvalidOperationException("Expected paired exchange.");
}

static void AssertTrue(bool value, string? message = null)
{
    if (!value)
        throw new InvalidOperationException(message ?? "Expected true.");
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void AssertNotEqual<T>(T expected, T actual)
{
    if (EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected value different from {expected}.");
}

static TException AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException ex)
    {
        return ex;
    }

    throw new InvalidOperationException($"Expected exception: {typeof(TException).Name}.");
}

static void AssertSetEqual<T>(HashSet<T> expected, HashSet<T> actual, string name)
    where T : notnull
{
    if (expected.SetEquals(actual))
        return;

    var missing = expected.Except(actual).Select(x => x?.ToString()).Order(StringComparer.Ordinal).ToArray();
    var extra = actual.Except(expected).Select(x => x?.ToString()).Order(StringComparer.Ordinal).ToArray();
    throw new InvalidOperationException(
        $"{name} mismatch. Missing: {string.Join(", ", missing)}. Extra: {string.Join(", ", extra)}.");
}

static void AssertBytes(byte[] expected, byte[] actual)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"Expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}.");
}

static void AssertArrayEqual<T>(T[] expected, T[] actual)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
}

static GameHttpHeaders TestHeaders()
    => new(
        Sid: "sid-1",
        AppVer: "1.2.3",
        ResVer: "2026070301",
        ViewerId: "123456789",
        Device: "android",
        DeviceSubtype: "phone");

static string EnabledConfigJson(params string[] endpointGroups)
    => EnabledConfigJsonForUrl("http://127.0.0.1:1/api/GamePackets", endpointGroups);

static string EnabledConfigJsonForUrl(string uploadUrl, params string[] endpointGroups)
    => JsonSerializer.Serialize(
        new PacketUploadConfig
        {
            UploadUrl = uploadUrl,
            Enabled = true,
            EndpointGroups = endpointGroups,
        },
        new JsonSerializerOptions(JsonSerializerDefaults.Web));

sealed class TempWorkspace : IDisposable
{
    const string DisabledConfigJson = """{"uploadUrl":"https://ura.shuise.net/api/GamePackets","serverRegionHint":null,"enabled":false,"endpointGroups":["single-mode"]}""";

    TempWorkspace(string path) => Path = path;

    public string Path { get; }

    public static TempWorkspace Create(string configJson = DisabledConfigJson)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gpc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var pluginDirectory = System.IO.Path.Combine(path, "PluginData", "游戏包采集");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(System.IO.Path.Combine(pluginDirectory, "config.json"), configJson);
        return new(path);
    }

    public static TempWorkspace CreateWithoutConfig()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gpc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(System.IO.Path.Combine(path, "PluginData", "游戏包采集"));
        return new(path);
    }

    public JsonDocument ReadPluginPacket(string pluginName)
    {
        var pending = System.IO.Path.Combine(Path, "PluginData", pluginName, "pending");
        var file = Directory.GetFiles(pending, "*.json", SearchOption.TopDirectoryOnly).Single();
        return JsonDocument.Parse(File.ReadAllText(file));
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

sealed class CurrentDirectoryScope : IDisposable
{
    readonly string previousDirectory;

    CurrentDirectoryScope(string path)
    {
        previousDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(path);
    }

    public static CurrentDirectoryScope Enter(string path) => new(path);

    public void Dispose() => Directory.SetCurrentDirectory(previousDirectory);
}

sealed class FakePluginContext(RecordingAnalyzerRegistry analyzers) : IPluginContext
{
    public ILiveDisplayOutput LiveDisplay { get; } = new FakeLiveDisplayOutput();
    public IPluginHostEvents Events { get; } = new FakePluginHostEvents();
    public IPluginAnalyzerRegistry Analyzers { get; } = analyzers;
}

sealed class RecordingAnalyzerRegistry : IPluginAnalyzerRegistry
{
    public Dictionary<Type, Func<byte[], GameHttpHeaders, ValueTask>> RequestHandlers { get; } = [];
    public Dictionary<Type, Func<byte[], GameHttpHeaders, ValueTask>> ResponseHandlers { get; } = [];
    public HashSet<Type> RequestEndpoints => RequestHandlers.Keys.ToHashSet();
    public HashSet<Type> ResponseEndpoints => ResponseHandlers.Keys.ToHashSet();

    public IDisposable RegisterRequest<TEndpoint>(Func<byte[], ValueTask> handler, int priority = 0)
        where TEndpoint : IGameEndpoint
        => throw new NotSupportedException("GamePacketCollector should register header-aware raw request analyzers.");

    public IDisposable RegisterRequest<TEndpoint>(Func<byte[], GameHttpHeaders, ValueTask> handler, int priority = 0)
        where TEndpoint : IGameEndpoint
        => Register(RequestHandlers, typeof(TEndpoint), handler);

    public IDisposable RegisterResponse<TEndpoint>(Func<byte[], ValueTask> handler, int priority = 0)
        where TEndpoint : IGameEndpoint
        => throw new NotSupportedException("GamePacketCollector should register header-aware raw response analyzers.");

    public IDisposable RegisterResponse<TEndpoint>(Func<byte[], GameHttpHeaders, ValueTask> handler, int priority = 0)
        where TEndpoint : IGameEndpoint
        => Register(ResponseHandlers, typeof(TEndpoint), handler);

    public IDisposable RegisterRequest<TEndpoint, TRequest>(Func<TRequest, ValueTask> handler, int priority = 0)
        where TEndpoint : IGameEndpoint
        => throw new NotSupportedException("GamePacketCollector should only register raw request analyzers.");

    public IDisposable RegisterResponse<TEndpoint, TResponse>(Func<TResponse, ValueTask> handler, int priority = 0)
        where TEndpoint : IGameEndpoint
        => throw new NotSupportedException("GamePacketCollector should only register raw response analyzers.");

    static IDisposable Register(
        Dictionary<Type, Func<byte[], GameHttpHeaders, ValueTask>> handlers,
        Type endpointType,
        Func<byte[], GameHttpHeaders, ValueTask> handler)
    {
        handlers.Add(endpointType, handler);
        return new DisposableAction(() => handlers.Remove(endpointType));
    }
}

sealed class FakePluginHostEvents : IPluginHostEvents
{
    public IDisposable OnStarted(Func<CancellationToken, ValueTask> handler) => DisposableAction.Empty;
}

sealed class FakeLiveDisplayOutput : ILiveDisplayOutput
{
    public LiveDisplayWorkspace? CurrentWorkspace { get; private set; }
    public LiveDisplayWorkspace CreateWorkspace(string title) => LiveDisplayWorkspace.Create(title);
    public void SwitchWorkspace(LiveDisplayWorkspace workspace) => CurrentWorkspace = workspace;
    public void BindWorkspaceHotkey(LiveDisplayWorkspace workspace, ConsoleKey key, ConsoleModifiers modifiers = 0, string? description = null) { }
    public void SetPanel(LiveDisplayWorkspace workspace, string key, string title, IRenderable content, bool fullBleed = false) { }
    public void Log(LiveDisplayWorkspace workspace, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info) { }
    public void MarkupLog(LiveDisplayWorkspace workspace, string markup, LiveDisplaySeverity severity = LiveDisplaySeverity.Info) { }
    public void Notify(LiveDisplayWorkspace workspace, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info, TimeSpan? ttl = null) { }
}

sealed class DisposableAction(Action dispose) : IDisposable
{
    public static readonly IDisposable Empty = new DisposableAction(() => { });

    public void Dispose() => dispose();
}

sealed class RecordingHttpHandler(HttpStatusCode statusCode, string responseBody = "") : HttpMessageHandler
{
    public string? Method { get; private set; }
    public string? RequestUri { get; private set; }
    public string? ContentType { get; private set; }
    public string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Method = request.Method.Method;
        RequestUri = request.RequestUri?.ToString();
        ContentType = request.Content?.Headers.ContentType?.ToString();
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new(statusCode)
        {
            Content = new StringContent(responseBody),
        };
    }
}

sealed class SingleRequestHttpServer : IDisposable
{
    readonly TcpListener listener;
    readonly CancellationTokenSource cts = new();
    readonly Task requestTask;
    readonly HttpStatusCode statusCode;
    readonly string responseBody;
    int requestCount;

    public SingleRequestHttpServer(HttpStatusCode statusCode, string responseBody = "")
    {
        this.statusCode = statusCode;
        this.responseBody = responseBody;
        listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Url = $"http://127.0.0.1:{port}/api/GamePackets";
        requestTask = Task.Run(ServeOneAsync);
    }

    public string Url { get; }
    public int RequestCount => requestCount;
    public string? Method { get; private set; }
    public string? Path { get; private set; }
    public string? Body { get; private set; }

    public void WaitForRequest()
    {
        if (!requestTask.Wait(TimeSpan.FromSeconds(3)))
            throw new InvalidOperationException("Timed out waiting for upload request.");

        if (requestTask.IsFaulted)
            requestTask.GetAwaiter().GetResult();
    }

    async Task ServeOneAsync()
    {
        using var client = await listener.AcceptTcpClientAsync(cts.Token);
        await using var stream = client.GetStream();
        var bytes = await ReadHttpRequestAsync(stream, cts.Token);
        var headerEnd = FindHeaderEnd(bytes);
        var headersText = Encoding.ASCII.GetString(bytes, 0, headerEnd + 4);
        var requestLineEnd = headersText.IndexOf("\r\n", StringComparison.Ordinal);
        var requestLine = headersText[..requestLineEnd].Split(' ', 3);
        var contentLength = ReadContentLength(headersText);

        Method = requestLine[0];
        Path = requestLine[1];
        Body = Encoding.UTF8.GetString(bytes, headerEnd + 4, contentLength);
        Interlocked.Increment(ref requestCount);

        var bodyBytes = Encoding.UTF8.GetBytes(responseBody);
        var responseHeaders = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {(int)statusCode} {statusCode}\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(responseHeaders, cts.Token);
        await stream.WriteAsync(bodyBytes, cts.Token);
    }

    static async Task<byte[]> ReadHttpRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var request = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            request.Write(buffer, 0, read);
            var bytes = request.ToArray();
            var headerEnd = FindHeaderEnd(bytes);
            if (headerEnd < 0)
                continue;

            var headersText = Encoding.ASCII.GetString(bytes, 0, headerEnd + 4);
            var contentLength = ReadContentLength(headersText);
            if (bytes.Length >= headerEnd + 4 + contentLength)
                return bytes;
        }

        throw new InvalidOperationException("Incomplete HTTP request.");
    }

    static int FindHeaderEnd(byte[] bytes)
    {
        for (var i = 0; i <= bytes.Length - 4; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
                return i;
        }

        return -1;
    }

    static int ReadContentLength(string headersText)
    {
        foreach (var line in headersText.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                return int.Parse(line["Content-Length:".Length..].Trim());
        }

        return 0;
    }

    public void Dispose()
    {
        cts.Cancel();
        listener.Stop();
        try { requestTask.Wait(TimeSpan.FromSeconds(1)); }
        catch { }
        cts.Dispose();
    }
}