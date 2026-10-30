using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.Json;
using Gallop;
using Gallop.Endpoints;
using MessagePack;
using Terminal.Gui.App;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using GameText = UmamusumeResponseAnalyzer.Localization.Game;
using UiText = UmamusumeResponseAnalyzer.Localization.TerminalGui;

static class PackageLoadSmoke
{
    const string InternalName = "GamePacketCollector";
    static readonly GameHttpHeaders Headers = new("zip-smoke-sid", "zip-smoke-app", "zip-smoke-res", "zip-smoke-viewer", null, null);

    internal static void Run(WorkspaceSmokeSession ui)
    {
        var package = FindPackage();
        using var directory = new TempCurrentDirectory(InternalName + "-fresh-zip");
        Config.Initialize();
        InitializeDatabase();
        Directory.CreateDirectory("Plugins");
        File.Copy(package, Path.Combine("Plugins", InternalName + ".zip"));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var dataDirectory = Path.Combine("PluginData", "游戏包采集");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "config.json"), JsonSerializer.Serialize(new
        {
            uploadUrl = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/GamePackets",
            enabled = true, serverRegionHint = "zip-smoke", endpointGroups = new[] { "single-mode", "gacha" }
        }));
        var upload = ReceiveUpload(listener);
        Assembly? loadedAssembly = null;
        try
        {
            PluginManager.Init();
            PluginManager.InitializeLoadedPlugins();
            PluginManager.TriggerStartedAsync().GetAwaiter().GetResult();
            Require(PluginManager.FailedPlugins.Count == 0 && PluginManager.LoadedPlugins.Count == 1,
                $"Fresh {InternalName} ZIP failed to load: {string.Join(",", PluginManager.FailedPlugins)}");
            var plugin = PluginManager.LoadedPlugins.Single();
            loadedAssembly = plugin.GetType().Assembly;
            var context = AssemblyLoadContext.GetLoadContext(loadedAssembly)!;
            var metadata = PluginManager.Metadatas[InternalName];
            Require(metadata.PackagePath == Path.GetFullPath(Path.Combine("Plugins", InternalName + ".zip")), "Loaded package path is not the isolated fresh ZIP.");
            using var archive = ZipFile.OpenRead(package);
            using var entry = archive.GetEntry(metadata.AssemblyEntry)!.Open();
            using var bytes = new MemoryStream();
            entry.CopyTo(bytes);
            bytes.Position = 0;
            using var pe = new PEReader(bytes);
            var reader = pe.GetMetadataReader();
            var mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
            Require(!context.IsCollectible && context != AssemblyLoadContext.Default && loadedAssembly.ManifestModule.ModuleVersionId == mvid,
                "Executed assembly did not come from the fresh ZIP in a fixed load context.");
            foreach (var shared in new[] { typeof(IPlugin).Assembly, typeof(SingleModeChara).Assembly, typeof(IApplication).Assembly })
                Require(ReferenceEquals(context.LoadFromAssemblyName(shared.GetName()), shared), $"Duplicated shared assembly: {shared.GetName().Name}");
            AssertCaptureUpload(upload);
        }
        finally
        {
            PluginManager.ShutdownAsync().GetAwaiter().GetResult();
            ui.Flush();
        }
        Require(PluginManager.LoadedPlugins.Count == 0 && PluginManager.Contexts.Count == 0
            && PluginManager.RequestAnalyzerMethods.Count == 0 && PluginManager.ResponseAnalyzerMethods.Count == 0,
            "ZIP shutdown left instances or analyzer registrations.");
        ui.Bootstrap.SwitchTo();
        Console.WriteLine($"PASS fresh ZIP {InternalName}: MVID, shared assembly identity, business callback, shutdown");
    }

    static string FindPackage()
    {
        var root = Environment.GetEnvironmentVariable("URA_PLUGIN_SMOKE_PACKAGE_ROOT");
        if (string.IsNullOrWhiteSpace(root) && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE")
                ?? throw new InvalidOperationException("GITHUB_WORKSPACE is required in GitHub Actions.");
            root = Path.Combine(workspace, "artifacts");
        }
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("URA_PLUGIN_SMOKE_PACKAGE_ROOT must point to this run's fresh ZIP output directory.");
        var matches = new List<string>();
        foreach (var candidate in Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories))
        {
            using var archive = ZipFile.OpenRead(candidate);
            var entry = archive.GetEntry("manifest.json");
            if (entry is null) continue;
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.GetProperty("InternalName").GetString() == InternalName) matches.Add(candidate);
        }
        Require(matches.Count == 1, $"Expected exactly one {InternalName} ZIP under {root}, found {matches.Count}.");
        var validated = PluginPackageValidator.Validate(matches[0], true);
        string[] dependencies = [];
        Require(validated.Manifest.Dependencies.Order(StringComparer.Ordinal).SequenceEqual(dependencies.Order(StringComparer.Ordinal)), "ZIP manifest Dependencies differ from the plugin contract.");
        return matches[0];
    }

    static void InitializeDatabase()
    {
        WriteDatabaseFile(Database.EVENT_NAME_FILEPATH, """[{"Id":501143706,"TriggerName":"ZIP 角色","Name":"ZIP 选择事件","Choices":[[{"Option":"ZIP 选项","SuccessEffect":"速度 +10"}]]}]""");
        WriteDatabaseFile(Database.NAMES_FILEPATH, "[]");
        WriteDatabaseFile(Database.SKILLS_FILEPATH, "[]");
        WriteDatabaseFile(Database.SKILL_UPGRADE_SPECIALITY_FILEPATH, "[]");
        WriteDatabaseFile(Database.TALENT_SKILLS_FILEPATH, "{}");
        WriteDatabaseFile(Database.FACTOR_IDS_FILEPATH, "{}");
        WriteDatabaseFile(Database.SADDLE_IDS_FILEPATH, "[]");
        WriteDatabaseFile(Database.SUCCESSION_RELATION_FILEPATH, "{}");
        Require(Database.Initialize().GetAwaiter().GetResult() == DatabaseAvailability.Ready, "ZIP database fixture did not initialize.");
    }

    static void WriteDatabaseFile(string path, string json)
    {
        using var file = File.Create(path);
        using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(brotli);
        writer.Write(json);
    }
    static void AssertCaptureUpload(Task<(string Method, string Json)> upload)
    {
        var endpoint = GameEndpointCatalog.ByPath.Values.First(endpoint => endpoint.Path.StartsWith("/umamusume/gacha/", StringComparison.Ordinal)
            && PluginManager.RequestAnalyzerMethods.Any(registration => registration.EndpointType == endpoint.EndpointType));
        var request = MessagePackSerializer.ConvertFromJson("{\"zipSmoke\":\"request\"}");
        var response = MessagePackSerializer.ConvertFromJson("{\"zipSmoke\":\"response\"}");
        Dispatch(AnalyzerKind.Request, endpoint, request);
        Dispatch(AnalyzerKind.Response, endpoint, response);
        var received = upload.GetAwaiter().GetResult();
        using var json = JsonDocument.Parse(received.Json);
        var root = json.RootElement;
        Require(received.Method == "PUT", "Collector did not issue PUT.");
        foreach (var (property, expected) in new[]
        {
            ("schemaVersion", "2"), ("kind", "game-packet"), ("endpointPath", endpoint.Path),
            ("group", "gacha"), ("sid", Headers.Sid), ("appVersion", Headers.AppVer),
            ("gameDataVersion", Headers.ResVer), ("viewerId", Headers.ViewerId), ("serverRegionHint", "zip-smoke")
        })
            Require(root.GetProperty(property).GetString() == expected, $"Collector PUT field {property} did not preserve its input.");
        Require(root.GetProperty("request").GetBytesFromBase64().SequenceEqual(request)
            && root.GetProperty("response").GetBytesFromBase64().SequenceEqual(response)
            && !string.IsNullOrEmpty(root.GetProperty("packetIdemKey").GetString()), "Collector PUT lost raw packet bytes or identity.");
        var pending = Path.Combine("PluginData", "游戏包采集", "pending");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Directory.EnumerateFiles(pending).Any() && DateTime.UtcNow < deadline)
            Thread.Sleep(10);
        Require(!Directory.EnumerateFiles(pending).Any(), "Collector did not clear successfully uploaded pending data.");
        Console.WriteLine("PASS ZIP business GamePacketCollector: real request/response capture, loopback PUT, pending cleanup");
    }

    static async Task<(string Method, string Json)> ReceiveUpload(TcpListener listener)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Missing HTTP request line.");
        var contentLength = 0;
        while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } header)
            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(header[15..].Trim());
        Require(contentLength > 0, "Collector PUT has no body.");
        var body = new char[contentLength];
        Require(await reader.ReadBlockAsync(body.AsMemory(), timeout.Token) == contentLength, "Truncated collector PUT body.");
        await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), timeout.Token);
        return (requestLine.Split(' ')[0], new string(body));
    }

    static void DispatchResponse(Type endpointType, object response)
        => Dispatch(AnalyzerKind.Response, GameEndpointCatalog.ByEndpointType[endpointType], MessagePackSerializer.Serialize(response.GetType(), response));

    static void Dispatch(AnalyzerKind kind, GameEndpointDescriptor endpoint, byte[] payload)
    {
        var registrations = PluginManager.SnapshotAnalyzerRegistrations(kind, endpoint.EndpointType);
        Require(registrations.Length != 0, $"No production registrations matched {kind} {endpoint.Path}.");
        var context = new AnalyzerDispatchContext(endpoint, payload, Headers);
        foreach (var registration in registrations)
        {
            using var callback = PluginManager.TryEnterPluginCallback(registration.Plugin);
            Require(callback is not null && !registration.IsFaulted, $"ZIP analyzer unavailable: {registration.Source}.");
            using var owner = HotkeyManager.RegisterScope(registration.Plugin);
            registration.Handler(context).GetAwaiter().GetResult();
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
