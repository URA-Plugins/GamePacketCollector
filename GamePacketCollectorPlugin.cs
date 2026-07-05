using System.Reflection;
using System.Threading.Channels;
using System.Text.Json;
using Spectre.Console;
using GamePacketCollector.Capture;
using UmamusumeResponseAnalyzer.LiveDisplay;
using UmamusumeResponseAnalyzer.Plugin;

namespace GamePacketCollector;

public sealed partial class GamePacketCollectorPlugin : IPlugin
{
    const int TargetUploadBytesPerSecond = 256 * 1024;

    static readonly TimeSpan RetriableUploadRetryDelay = TimeSpan.FromSeconds(10);
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    static readonly MethodInfo RegisterRequestMethod = ResolveRawRegistrationMethod(nameof(IPluginAnalyzerRegistry.RegisterRequest));
    static readonly MethodInfo RegisterResponseMethod = ResolveRawRegistrationMethod(nameof(IPluginAnalyzerRegistry.RegisterResponse));
    static readonly EndpointGroupOption[] OptionalEndpointGroupOptions =
    [
        new("gacha", "抽卡结果 (Gacha)"),
        new("room-match", "自定义比赛 (RoomMatch)"),
        new("race", "比赛与结果 (Race)"),
    ];

    readonly object pendingFileGate = new();
    readonly List<IDisposable> analyzerRegistrations = [];
    readonly Channel<string> uploadQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });
    readonly CancellationTokenSource uploadCts = new();

    PacketUploadConfig uploadConfig = new();
    PacketExchangeBuffer? exchangeBuffer;
    PacketUploader? uploader;
    HttpClient? httpClient;
    Task? uploaderTask;
    bool disposed;
    string pendingDirectory = string.Empty;
    string failedDirectory = string.Empty;

    public string Name => "游戏包采集";

    public string Author => "URA";

    public string[] Targets => [];

    string DataDirectory => Path.Combine("PluginData", Name);

    public void Initialize(IPluginContext context)
    {
        try
        {
            var dataDirectory = DataDirectory;
            Directory.CreateDirectory(dataDirectory);
            var configPath = Path.Combine(dataDirectory, "config.json");
            uploadConfig = PacketUploadConfig.LoadOrCreate(configPath, JsonOptions, CreateFirstRunConfig);

            var workspace = context.LiveDisplay.CreateWorkspace(Name);
            if (!uploadConfig.Enabled)
            {
                context.LiveDisplay.Log(
                    workspace,
                    $"GamePacketCollector 上传未启用，未注册 raw analyzer。配置文件: {configPath}",
                    LiveDisplaySeverity.Info);
                return;
            }

            var captureEndpoints = ResolveCaptureEndpoints(uploadConfig);
            pendingDirectory = Path.Combine(dataDirectory, "pending");
            failedDirectory = Path.Combine(dataDirectory, "failed");
            Directory.CreateDirectory(pendingDirectory);
            Directory.CreateDirectory(failedDirectory);

            exchangeBuffer = new();
            StartUploader();
            RegisterAnalyzers(context.Analyzers, captureEndpoints);

            context.LiveDisplay.Log(
                workspace,
                $"GamePacketCollector 已注册 {captureEndpoints.Count} 个端点的 raw request/response 采集，上传到 {uploadConfig.UploadUrl}，pending 目录: {pendingDirectory}",
                LiveDisplaySeverity.Info);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Task UpdatePlugin(ProgressContext ctx) => Task.CompletedTask;

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        DisposeAnalyzerRegistrations(analyzerRegistrations);
        analyzerRegistrations.Clear();

        uploadQueue.Writer.TryComplete();
        uploadCts.Cancel();
        try { uploaderTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch { }
        httpClient?.Dispose();
        uploadCts.Dispose();
    }

    ValueTask CaptureRequest(PacketCaptureEndpoint endpoint, byte[] msgpack, GameHttpHeaders headers)
    {
        try
        {
            if (exchangeBuffer is null)
                throw new InvalidOperationException("GamePacketCollector is not initialized.");

            exchangeBuffer.RecordRequest(endpoint, msgpack, headers);
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
#if DEBUG
            throw;
#endif
        }

        return ValueTask.CompletedTask;
    }

    ValueTask CaptureResponse(PacketCaptureEndpoint endpoint, byte[] msgpack, GameHttpHeaders _)
    {
        try
        {
            if (exchangeBuffer is null)
                throw new InvalidOperationException("GamePacketCollector is not initialized.");

            var exchange = exchangeBuffer.RecordResponse(endpoint, msgpack);
            if (exchange is not null)
                WritePendingExchange(exchange);
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
#if DEBUG
            throw;
#endif
        }

        return ValueTask.CompletedTask;
    }

    void WritePendingExchange(PacketCaptureExchange exchange)
    {
        var envelope = PacketUploadEnvelope.FromExchange(exchange, uploadConfig.ServerRegionHint);
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var file = Path.Combine(pendingDirectory, $"{exchange.PacketIdemKey}.json");

        lock (pendingFileGate)
            File.WriteAllText(file, json);

        uploadQueue.Writer.TryWrite(file);
    }

    void StartUploader()
    {
        httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"GamePacketCollector/{PluginVersion()}");
        uploader = new(httpClient, uploadConfig.UploadUrl);
        uploaderTask = Task.Run(() => UploadLoop(uploadCts.Token));

        foreach (var file in Directory.GetFiles(pendingDirectory, "*.json", SearchOption.TopDirectoryOnly))
            uploadQueue.Writer.TryWrite(file);
    }

    async Task UploadLoop(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var file in uploadQueue.Reader.ReadAllAsync(cancellationToken))
                await TryUploadFile(file, cancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    async Task TryUploadFile(string file, CancellationToken cancellationToken)
    {
        if (uploader is null || !File.Exists(file))
            return;

        try
        {
            var fileSize = new FileInfo(file).Length;
            var json = await File.ReadAllTextAsync(file, cancellationToken);
            await uploader.UploadAsync(json, cancellationToken);
            DeleteUploadedPendingFile(file);
            await PaceNextUpload(fileSize, cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (PacketUploadException ex) when (!ex.IsRetriable)
        {
            AnsiConsole.WriteException(ex);
            MoveToFailed(file);
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            if (!uploadQueue.Writer.TryWrite(file))
                return;

            await Task.Delay(RetriableUploadRetryDelay, cancellationToken);
        }
    }

    static async Task PaceNextUpload(long uploadedBytes, CancellationToken cancellationToken)
    {
        if (uploadedBytes <= 0)
            return;

        // Analytics uploads should not flush a historical pending queue at line rate.
        var delay = TimeSpan.FromSeconds((double)uploadedBytes / TargetUploadBytesPerSecond);
        await Task.Delay(delay, cancellationToken);
    }


    void MoveToFailed(string file) => MoveToDatedDirectory(file, failedDirectory);

    static void DeleteUploadedPendingFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
        }
    }
    static void MoveToDatedDirectory(string file, string targetRoot)
    {
        var dayDirectory = Path.Combine(targetRoot, DateTime.UtcNow.ToString("yyyyMMdd"));
        Directory.CreateDirectory(dayDirectory);
        File.Move(file, Path.Combine(dayDirectory, Path.GetFileName(file)), overwrite: true);
    }

    void RegisterAnalyzers(IPluginAnalyzerRegistry registry, IReadOnlyList<PacketCaptureEndpoint> captureEndpoints)
    {
        var registrations = new List<IDisposable>(captureEndpoints.Count * 2);
        try
        {
            foreach (var endpoint in captureEndpoints)
            {
                registrations.Add(RegisterAnalyzer(registry, RegisterRequestMethod, endpoint, CaptureRequest));
                registrations.Add(RegisterAnalyzer(registry, RegisterResponseMethod, endpoint, CaptureResponse));
            }

            analyzerRegistrations.AddRange(registrations);
        }
        catch
        {
            DisposeAnalyzerRegistrations(registrations);
            throw;
        }
    }

    IDisposable RegisterAnalyzer(
        IPluginAnalyzerRegistry registry,
        MethodInfo registrationMethod,
        PacketCaptureEndpoint endpoint,
        Func<PacketCaptureEndpoint, byte[], GameHttpHeaders, ValueTask> capture)
    {
        var closedMethod = registrationMethod.MakeGenericMethod(endpoint.EndpointType);
        Func<byte[], GameHttpHeaders, ValueTask> handler = (payload, headers) => capture(endpoint, payload, headers);
        return (IDisposable)closedMethod.Invoke(registry, [handler, 0])!;
    }

    static IReadOnlyList<PacketCaptureEndpoint> ResolveCaptureEndpoints(PacketUploadConfig config)
    {
        var requestedGroups = config.EndpointGroups.ToHashSet(StringComparer.Ordinal);
        var knownGroups = PacketCaptureCatalog.SelectedEndpoints
            .Select(x => x.Group)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unknownGroups = requestedGroups
            .Except(knownGroups, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknownGroups.Length > 0)
            throw new InvalidOperationException($"Invalid GamePacketCollector endpointGroups: {string.Join(", ", unknownGroups)}");

        return PacketCaptureCatalog.SelectedEndpoints
            .Where(x => requestedGroups.Contains(x.Group))
            .ToArray();
    }

    static PacketUploadConfig CreateFirstRunConfig()
    {
        var consent = LiveDisplayConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("""
是否允许 GamePacketCollector 不匿名上传游戏数据包到 URACloud？

上传内容: 完整 request/response 原包,包含 ViewerId 等真实账号标识。
用途: 统计育成事件效果、事件成功率、剧本特性和其它游戏机制。
数据不会分享给第三方。
""")
                .AddChoices(["否", "否", "是"])) == "是";

        if (!consent)
            return new PacketUploadConfig
            {
                Enabled = false,
                EndpointGroups = [PacketUploadConfig.SingleModeEndpointGroup],
            };

        LiveDisplayConsole.WriteLine("必选: 育成数据 (SingleMode*)。用于统计育成事件效果、事件成功率、剧本特性等。");
        var selectedOptionalLabels = LiveDisplayConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("还要上传哪些可选端点？")
                .NotRequired()
                .InstructionsText("[grey](空格选择,回车确认；不选则只上传育成数据)[/]")
                .AddChoices(OptionalEndpointGroupOptions.Select(x => x.Label)));

        var selectedOptionalLabelSet = selectedOptionalLabels.ToHashSet(StringComparer.Ordinal);
        return new PacketUploadConfig
        {
            Enabled = true,
            EndpointGroups =
            [
                PacketUploadConfig.SingleModeEndpointGroup,
                ..OptionalEndpointGroupOptions
                    .Where(x => selectedOptionalLabelSet.Contains(x.Label))
                    .Select(x => x.Group),
            ],
        };
    }

    static void DisposeAnalyzerRegistrations(IEnumerable<IDisposable> registrations)
    {
        foreach (var registration in registrations)
            registration.Dispose();
    }

    string PluginVersion()
        => GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0";

    static MethodInfo ResolveRawRegistrationMethod(string methodName)
        => typeof(IPluginAnalyzerRegistry)
            .GetMethods()
            .Single(method =>
            {
                if (method.Name != methodName || !method.IsGenericMethodDefinition)
                    return false;

                var parameters = method.GetParameters();
                return method.GetGenericArguments().Length == 1 &&
                       parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(Func<byte[], GameHttpHeaders, ValueTask>) &&
                       parameters[1].ParameterType == typeof(int);
            });

    sealed record EndpointGroupOption(string Group, string Label);
}