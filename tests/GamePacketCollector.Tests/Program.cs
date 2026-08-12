using System.Net;
using System.Text.Json;
using Gallop.Endpoints;
using GamePacketCollector;
using GamePacketCollector.Capture;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.Time;
using UmamusumeResponseAnalyzer.Plugin;

var tests = new (string Name, Action Body)[]
{
    ("PacketCaptureCatalog keeps representative endpoint paths explicit", PacketCaptureCatalogKeepsRepresentativeEndpointPathsExplicit),
    ("PacketCaptureCatalog includes exactly 14 get choice reward endpoints", PacketCaptureCatalogIncludesExactly14GetChoiceRewardEndpoints),
    ("PacketCaptureCatalog excludes player controlled setup endpoints", PacketCaptureCatalogExcludesPlayerControlledSetupEndpoints),
    ("PacketExchangeBuffer pairs request and response per endpoint", PacketExchangeBufferPairsRequestAndResponsePerEndpoint),
    ("PacketExchangeBuffer preserves FIFO request SID pairs", PacketExchangeBufferPreservesFifoRequestSidPairs),
    ("PacketExchangeBuffer creates deterministic PacketIdemKey", PacketExchangeBufferCreatesDeterministicPacketIdemKey),
    ("PacketExchangeBuffer requires upload headers", PacketExchangeBufferRequiresUploadHeaders),
    ("PacketExchangeBuffer drops unmatched response", PacketExchangeBufferDropsUnmatchedResponse),
    ("PacketExchangeBuffer preserves raw responses without parsing headers", PacketExchangeBufferPreservesRawResponsesWithoutParsingHeaders),
    ("PacketUploadEnvelope creates GamePackets upload body", PacketUploadEnvelopeCreatesGamePacketsUploadBody),
    ("PacketUploader sends PUT JSON to configured endpoint", PacketUploaderSendsPutJsonToConfiguredEndpoint),
    ("PacketUploader classifies permanent and retriable failures", PacketUploaderClassifiesPermanentAndRetriableFailures),
    ("PacketUploadConfig persists accepted endpoint selection", PacketUploadConfigPersistsAcceptedEndpointSelection),
    ("PacketUploadConfig requires single-mode when enabled", PacketUploadConfigRequiresSingleModeWhenEnabled),
    ("Plugin atomically publishes concurrent captures", PluginAtomicallyPublishesConcurrentCaptures),
    ("Plugin Dispose is idempotent before initialization", PluginDisposeIsIdempotentBeforeInitialization),
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

static void PacketCaptureCatalogIncludesExactly14GetChoiceRewardEndpoints()
{
    var paths = PacketCaptureCatalog.SelectedEndpoints
        .Where(x => x.Path.EndsWith("/get_choice_reward", StringComparison.Ordinal))
        .Select(x => x.Path)
        .Order(StringComparer.Ordinal)
        .ToArray();

    AssertArrayEqual([
        "/umamusume/single_mode/get_choice_reward",
        "/umamusume/single_mode_arc/get_choice_reward",
        "/umamusume/single_mode_breeders/get_choice_reward",
        "/umamusume/single_mode_cook/get_choice_reward",
        "/umamusume/single_mode_free/get_choice_reward",
        "/umamusume/single_mode_legend/get_choice_reward",
        "/umamusume/single_mode_live/get_choice_reward",
        "/umamusume/single_mode_mecha/get_choice_reward",
        "/umamusume/single_mode_onsen/get_choice_reward",
        "/umamusume/single_mode_pioneer/get_choice_reward",
        "/umamusume/single_mode_ramen/get_choice_reward",
        "/umamusume/single_mode_sport/get_choice_reward",
        "/umamusume/single_mode_team/get_choice_reward",
        "/umamusume/single_mode_venus/get_choice_reward",
    ], paths);
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

    buffer.RecordRequest(endpoint, new byte[] { 0x01, 0x02 }, headers);

    var response = PacketFixtures.Response;
    var exchange = buffer.RecordResponse(endpoint, response);

    AssertTrue(exchange is not null);
    AssertTrue(!string.IsNullOrWhiteSpace(exchange!.PacketIdemKey));
    AssertTrue(!exchange.PacketIdemKey.StartsWith("game-packet-collector:", StringComparison.Ordinal));
    AssertEqual(endpoint.EndpointType.FullName, exchange.EndpointType);
    AssertEqual(endpoint.Path, exchange.EndpointPath);
    AssertEqual(endpoint.Group, exchange.Group);
    AssertEqual("1.2.3", exchange.AppVersion);
    AssertEqual("2026070301", exchange.GameDataVersion);
    AssertEqual("123456789", exchange.ViewerId);
    AssertEqual("request-sid-1", exchange.RequestSid);
    AssertBytes([0x01, 0x02], exchange.Request);
    AssertBytes(response, exchange.Response);
}

static void PacketExchangeBufferPreservesFifoRequestSidPairs()
{
    var buffer = new PacketExchangeBuffer();
    var endpoint = PacketCaptureCatalog.SelectedEndpoints.Single(x => x.EndpointType == typeof(GameApi.Gacha.Exec));
    buffer.RecordRequest(endpoint, new byte[] { 0x01 }, TestHeaders() with { Sid = "request-sid-1" });
    buffer.RecordRequest(endpoint, new byte[] { 0x02 }, TestHeaders() with { Sid = "request-sid-2" });

    var first = buffer.RecordResponse(endpoint, PacketFixtures.Response)!;
    var second = buffer.RecordResponse(endpoint, PacketFixtures.AlternateResponse)!;

    AssertEqual("request-sid-1", first.RequestSid);
    AssertEqual("request-sid-2", second.RequestSid);
    AssertBytes(PacketFixtures.Response, first.Response);
    AssertBytes(PacketFixtures.AlternateResponse, second.Response);
}

static void PacketExchangeBufferCreatesDeterministicPacketIdemKey()
{
    var baseline = CreateExchange([0x01, 0x02], PacketFixtures.Response);
    var same = CreateExchange([0x01, 0x02], PacketFixtures.Response);
    var changedRequest = CreateExchange([0x01, 0x03], PacketFixtures.Response);
    var changedResponse = CreateExchange([0x01, 0x02], PacketFixtures.AlternateResponse);
    var changedViewer = CreateExchange([0x01, 0x02], PacketFixtures.Response, TestHeaders() with { ViewerId = "987654321" });
    var changedAppVersion = CreateExchange([0x01, 0x02], PacketFixtures.Response, TestHeaders() with { AppVer = "9.9.9" });
    var changedGameDataVersion = CreateExchange([0x01, 0x02], PacketFixtures.Response, TestHeaders() with { ResVer = "2026070401" });
    var roomMatchEndpoint = PacketCaptureCatalog.SelectedEndpoints.Single(x => x.EndpointType == typeof(GameApi.RoomMatch.CreateRoom));
    var changedEndpoint = CreateExchange([0x01, 0x02], PacketFixtures.Response, endpoint: roomMatchEndpoint);

    AssertEqual(baseline.PacketIdemKey, same.PacketIdemKey);
    AssertNotEqual(baseline.PacketIdemKey, changedRequest.PacketIdemKey);
    AssertNotEqual(baseline.PacketIdemKey, changedResponse.PacketIdemKey);
    AssertNotEqual(baseline.PacketIdemKey, changedViewer.PacketIdemKey);
    AssertNotEqual(baseline.PacketIdemKey, changedAppVersion.PacketIdemKey);
    AssertNotEqual(baseline.PacketIdemKey, changedGameDataVersion.PacketIdemKey);
    AssertNotEqual(baseline.PacketIdemKey, changedEndpoint.PacketIdemKey);
}

static void PacketExchangeBufferRequiresUploadHeaders()
{
    AssertUploadHeaderRequired(TestHeaders() with { Sid = null }, "X-Hachimi-sid");
    AssertUploadHeaderRequired(TestHeaders() with { ViewerId = null }, "X-Hachimi-viewerid");
    AssertUploadHeaderRequired(TestHeaders() with { ResVer = null }, "X-Hachimi-res-ver");
    AssertUploadHeaderRequired(TestHeaders() with { AppVer = null }, "X-Hachimi-app-ver");
}

static void AssertUploadHeaderRequired(GameHttpHeaders headers, string headerName)
{
    var endpoint = PacketCaptureCatalog.SelectedEndpoints.Single(x => x.EndpointType == typeof(GameApi.Gacha.Exec));
    var buffer = new PacketExchangeBuffer();

    var error = AssertThrows<InvalidOperationException>(() =>
        buffer.RecordRequest(endpoint, new byte[] { 0x01, 0x02 }, headers));

    AssertTrue(error.Message.Contains(headerName, StringComparison.Ordinal), error.Message);
}
static void PacketExchangeBufferDropsUnmatchedResponse()
{
    var buffer = new PacketExchangeBuffer();
    var endpoint = PacketCaptureCatalog.SelectedEndpoints.Single(x => x.EndpointType == typeof(GameApi.Gacha.Exec));

    var exchange = buffer.RecordResponse(endpoint, new byte[] { 0x03, 0x04 });

    AssertTrue(exchange is null);
}

static void PacketExchangeBufferPreservesRawResponsesWithoutParsingHeaders()
{
    var endpoint = PacketCaptureCatalog.SelectedEndpoints.Single(x => x.EndpointType == typeof(GameApi.Gacha.Exec));
    foreach (var response in new[]
             {
                 PacketFixtures.ResponseWithoutDataHeaders,
                 PacketFixtures.ResponseWithoutSid,
                 new byte[] { 0xC1 },
             })
    {
        var buffer = new PacketExchangeBuffer();
        buffer.RecordRequest(endpoint, new byte[] { 0x01, 0x02 }, TestHeaders());

        var exchange = buffer.RecordResponse(endpoint, response);

        AssertTrue(exchange is not null);
        AssertBytes(response, exchange!.Response);
    }
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
        RequestSid: "request-sid-1",
        Request: [0x01, 0x02],
        Response: PacketFixtures.Response);

    var envelope = PacketUploadEnvelope.FromExchange(exchange);

    AssertEqual("2", envelope.SchemaVersion);
    AssertEqual("idem-key", envelope.PacketIdemKey);
    AssertEqual(endpoint.EndpointType.FullName, envelope.EndpointType);
    AssertEqual(endpoint.Path, envelope.EndpointPath);
    AssertEqual(endpoint.Group, envelope.Group);
    AssertEqual("1.2.3", envelope.AppVersion);
    AssertEqual("2026070301", envelope.GameDataVersion);
    AssertEqual("123456789", envelope.ViewerId);
    AssertEqual("request-sid-1", envelope.Sid);
    AssertBytes([0x01, 0x02], envelope.Request);
    AssertBytes(PacketFixtures.Response, envelope.Response);

    using var document = JsonDocument.Parse(JsonSerializer.Serialize(
        envelope,
        new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    AssertArrayEqual([
        "appVersion",
        "endpointPath",
        "endpointType",
        "gameDataVersion",
        "group",
        "kind",
        "packetIdemKey",
        "request",
        "response",
        "schemaVersion",
        "serverRegionHint",
        "sid",
        "viewerId",
    ], document.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray());
    AssertEqual("2", document.RootElement.GetProperty("schemaVersion").GetString());
    AssertEqual("request-sid-1", document.RootElement.GetProperty("sid").GetString());
    AssertTrue(!document.RootElement.TryGetProperty("requestSid", out _));
    AssertTrue(!document.RootElement.TryGetProperty("responseSid", out _));
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

static void PacketUploadConfigPersistsAcceptedEndpointSelection()
{
    using var workspace = TempWorkspace.CreateWithoutConfig();
    using var currentDirectory = CurrentDirectoryScope.Enter(workspace.Path);
    using IApplication application = Application.Create(new VirtualTimeProvider());
    application.Init(DriverRegistry.Names.ANSI);
    application.Driver!.SetScreenSize(100, 30);
    var plugin = new GamePacketCollectorPlugin();
    try
    {
        var configPath = Path.Combine(workspace.Path, "PluginData", "游戏包采集", "config.json");
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        RunPromptOnOwner(
            application,
            () => plugin.ConfigPromptAsync(application),
            () =>
            {
                application.InjectKey(Key.Enter);
                application.InjectKey(Key.CursorDown);
                application.InjectKey(Key.Enter);
                application.InjectKey(Key.CursorDown);
                application.InjectKey(Key.CursorDown);
                application.InjectKey(Key.CursorDown);
                application.InjectKey(Key.Enter);
            });
        var config = PacketUploadConfig.Load(configPath, jsonOptions);

        AssertTrue(config.Enabled);
        AssertArrayEqual(["single-mode", "gacha"], config.EndpointGroups);

        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        AssertTrue(document.RootElement.GetProperty("enabled").GetBoolean());
        AssertArrayEqual(
            ["single-mode", "gacha"],
            document.RootElement.GetProperty("endpointGroups").EnumerateArray().Select(x => x.GetString()!).ToArray());
    }
    finally
    {
        plugin.Dispose();
    }
}

static void PacketUploadConfigRequiresSingleModeWhenEnabled()
{
    using var workspace = TempWorkspace.CreateWithoutConfig();
    var configPath = Path.Combine(workspace.Path, "PluginData", "游戏包采集", "config.json");

    var error = AssertThrows<InvalidOperationException>(() =>
        new PacketUploadConfig
        {
            Enabled = true,
            EndpointGroups = ["gacha"],
        }.Save(configPath, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    AssertTrue(error.Message.Contains("single-mode", StringComparison.Ordinal), error.Message);
    AssertTrue(!File.Exists(configPath), "Invalid config must fail before writing the file.");
}

static void PluginDisposeIsIdempotentBeforeInitialization()
{
    var plugin = new GamePacketCollectorPlugin();

    plugin.Dispose();
    plugin.Dispose();
}

static void PluginAtomicallyPublishesConcurrentCaptures()
{
    using var workspace = TempWorkspace.CreateWithoutConfig();
    using var currentDirectory = CurrentDirectoryScope.Enter(workspace.Path);
    var dataDirectory = Path.Combine(workspace.Path, "PluginData", "游戏包采集");
    Directory.CreateDirectory(dataDirectory);
    new PacketUploadConfig
    {
        Enabled = true,
        EndpointGroups = [PacketUploadConfig.SingleModeEndpointGroup],
    }.Save(
        Path.Combine(dataDirectory, "config.json"),
        new(JsonSerializerDefaults.Web));

    var context = new CapturePluginContext();
    var plugin = new GamePacketCollectorPlugin();
    try
    {
        var missingHost = AssertThrows<InvalidOperationException>(() => plugin.Initialize(context));
        AssertEqual("TerminalUi 尚未初始化。", missingHost.Message);

        var endpoint = PacketCaptureCatalog.SelectedEndpoints
            .Single(x => x.EndpointType == typeof(GameApi.SingleMode.ExecCommand));
        var descriptor = GameEndpointCatalog.ByPath[endpoint.Path];
        var headers = TestHeaders();
        const int captureCount = 8;
        for (var index = 0; index < captureCount; index++)
            context.AnalyzerRegistry.DispatchRequest(
                descriptor,
                new byte[] { checked((byte)index), 0x02 },
                headers).GetAwaiter().GetResult();

        Task.WaitAll(
            Enumerable.Range(0, captureCount)
                .Select(_ => Task.Run(async () =>
                    await context.AnalyzerRegistry.DispatchResponse(
                        descriptor,
                        PacketFixtures.Response,
                        headers)))
                .ToArray());

        var pendingDirectory = Path.Combine(dataDirectory, "pending");
        var files = Directory.GetFiles(pendingDirectory, "*.json", SearchOption.TopDirectoryOnly);
        AssertEqual(captureCount, files.Length);
        AssertTrue(
            !Directory.GetFiles(pendingDirectory, "*.tmp", SearchOption.TopDirectoryOnly).Any(),
            "Atomic publication must clean every unique temporary file.");

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var requestSequence = files.Select(file =>
        {
            var envelope = JsonSerializer.Deserialize<PacketUploadEnvelope>(
                File.ReadAllBytes(file),
                jsonOptions)
                ?? throw new InvalidOperationException("Expected a complete pending envelope.");
            var expectedIdemKey = CreateExchange(
                envelope.Request,
                PacketFixtures.Response,
                endpoint: endpoint).PacketIdemKey;
            AssertEqual(expectedIdemKey, envelope.PacketIdemKey);
            AssertEqual($"{expectedIdemKey}.json", Path.GetFileName(file));
            AssertEqual(endpoint.Path, envelope.EndpointPath);
            AssertBytes([envelope.Request[0], 0x02], envelope.Request);
            AssertBytes(PacketFixtures.Response, envelope.Response);
            return envelope.Request[0];
        }).Order().ToArray();
        AssertArrayEqual(
            Enumerable.Range(0, captureCount).Select(index => checked((byte)index)).ToArray(),
            requestSequence);
    }
    finally
    {
        plugin.Dispose();
    }
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

static void RunPromptOnOwner(
    IApplication application,
    Func<Task> runPrompt,
    Action interact)
{
    var interacted = false;
    EventHandler<Terminal.Gui.App.EventArgs<IApplication>> iteration = (_, _) =>
    {
        if (interacted)
            return;

        interacted = true;
        interact();
    };
    application.Iteration += iteration;
    try
    {
        runPrompt()
            .WaitAsync(TimeSpan.FromSeconds(3))
            .GetAwaiter()
            .GetResult();
        AssertTrue(interacted, "Expected the owner-thread configuration prompt to process input.");
    }
    finally
    {
        application.Iteration -= iteration;
    }
}

static GameHttpHeaders TestHeaders()
    => new(
        Sid: "request-sid-1",
        AppVer: "1.2.3",
        ResVer: "2026070301",
        ViewerId: "123456789",
        Device: "android",
        DeviceSubtype: "phone");

static class PacketFixtures
{
    // De-identified Gallop response map: data={}, data_headers={sid, servertime}.
    public static byte[] Response => Convert.FromHexString(
        "82A46461746180AC646174615F6865616465727382A3736964AE726573706F6E73652D7369642D31AA73657276657274696D65CE66851E00");

    public static byte[] AlternateResponse => Convert.FromHexString(
        "82A46461746180AC646174615F6865616465727382A3736964AE726573706F6E73652D7369642D32AA73657276657274696D65CE66851E00");

    public static byte[] ResponseWithoutDataHeaders => Convert.FromHexString("81A46461746180");

    public static byte[] ResponseWithoutSid => Convert.FromHexString(
        "82A46461746180AC646174615F6865616465727381AA73657276657274696D65CE66851E00");
}

sealed class TempWorkspace : IDisposable
{
    TempWorkspace(string path) => Path = path;

    public string Path { get; }

    public static TempWorkspace CreateWithoutConfig()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gpc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new(path);
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

sealed class CapturePluginContext : IPluginContext
{
    public IApplication Application => throw new NotSupportedException();
    public IPluginHostEvents Events => throw new NotSupportedException();
    public CaptureAnalyzerRegistry AnalyzerRegistry { get; } = new();
    public IPluginAnalyzerRegistry Analyzers => AnalyzerRegistry;
    public bool IsPluginAvailable(string internalName) => false;

    public void RunBackground(Func<CancellationToken, ValueTask> operation) { }
}

sealed class CaptureAnalyzerRegistry : IPluginAnalyzerRegistry
{
    Func<GameEndpointDescriptor, ReadOnlyMemory<byte>, GameHttpHeaders, ValueTask>? request;
    Func<GameEndpointDescriptor, ReadOnlyMemory<byte>, GameHttpHeaders, ValueTask>? response;

    public void Register<TPayload>(
        AnalyzerKind kind,
        IReadOnlyList<EndpointPattern> patterns,
        Func<AnalyzerInvocation<TPayload>, ValueTask> handler,
        int priority = 0)
    {
        if (typeof(TPayload) != typeof(ReadOnlyMemory<byte>))
            throw new InvalidOperationException($"Unexpected analyzer payload type: {typeof(TPayload)}.");
        ValueTask Dispatch(
            GameEndpointDescriptor endpoint,
            ReadOnlyMemory<byte> payload,
            GameHttpHeaders headers)
            => handler(new(endpoint, (TPayload)(object)payload, headers));

        if (kind == AnalyzerKind.Request)
            request = Dispatch;
        else
            response = Dispatch;
    }

    public ValueTask DispatchRequest(
        GameEndpointDescriptor endpoint,
        ReadOnlyMemory<byte> payload,
        GameHttpHeaders headers)
        => (request ?? throw new InvalidOperationException("Raw request analyzer was not registered."))(
            endpoint,
            payload,
            headers);

    public ValueTask DispatchResponse(
        GameEndpointDescriptor endpoint,
        ReadOnlyMemory<byte> payload,
        GameHttpHeaders headers)
        => (response ?? throw new InvalidOperationException("Raw response analyzer was not registered."))(
            endpoint,
            payload,
            headers);
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
