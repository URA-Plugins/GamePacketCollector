using System.Threading.Channels;
using System.Text.Json;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using GamePacketCollector.Capture;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;

namespace GamePacketCollector;

public sealed partial class GamePacketCollectorPlugin : IPlugin
{
    const int TargetUploadBytesPerSecond = 256 * 1024;

    static readonly TimeSpan RetriableUploadRetryDelay = TimeSpan.FromSeconds(10);
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    static readonly EndpointGroupOption[] OptionalEndpointGroupOptions =
    [
        new("gacha", "抽卡结果 (Gacha)"),
        new("room-match", "自定义比赛 (RoomMatch)"),
        new("race", "比赛与结果 (Race)"),
    ];

    readonly Channel<string> uploadQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    PacketUploadConfig uploadConfig = new();
    PacketExchangeBuffer? exchangeBuffer;
    IReadOnlyDictionary<string, PacketCaptureEndpoint>? captureEndpointsByPath;
    PacketUploader? uploader;
    HttpClient? httpClient;
    int permanentUploadFailureNotified;
    string pendingDirectory = string.Empty;
    string failedDirectory = string.Empty;

    static string DataDirectory => Path.Combine("PluginData", "游戏包采集");
    string ConfigPath => Path.Combine(DataDirectory, "config.json");

    public void Initialize(IPluginContext context)
    {
        if (!File.Exists(ConfigPath))
        {
            context.Events.OnStarted(
                cancellationToken => ConfigureFirstRunAsync(context, cancellationToken));
            TerminalUi.Log(
                "GamePacketCollector",
                $"GamePacketCollector 尚未配置；启动后将显示首次配置。配置文件: {ConfigPath}",
                UiSeverity.Info);
            return;
        }

        uploadConfig = PacketUploadConfig.Load(ConfigPath, JsonOptions);
        TerminalUi.Log("GamePacketCollector", Activate(context), UiSeverity.Info);
    }

    public async Task ConfigPromptAsync(IApplication application, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        cancellationToken.ThrowIfCancellationRequested();
        if (application.TopRunnable is null &&
            Environment.CurrentManagedThreadId != application.MainThreadId)
            throw new InvalidOperationException(
                "GamePacketCollector 无法从非 UI thread 启动配置：Terminal.Gui 当前没有正在运行的 session。");

        var isFirstRun = !File.Exists(ConfigPath);
        var draft = !isFirstRun
            ? PacketUploadConfig.Load(ConfigPath, JsonOptions)
            : new PacketUploadConfig();

        PacketUploadConfig savedConfig;
        if (Environment.CurrentManagedThreadId == application.MainThreadId)
        {
            savedConfig = RunConfigDialog(application, draft, isFirstRun, cancellationToken);
        }
        else
        {
            var completion = new TaskCompletionSource<PacketUploadConfig>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            application.Invoke(() =>
            {
                try
                {
                    completion.SetResult(RunConfigDialog(application, draft, isFirstRun, cancellationToken));
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });
            savedConfig = await completion.Task;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(DataDirectory);
        savedConfig.Save(ConfigPath, JsonOptions);
    }

    static PacketUploadConfig RunConfigDialog(
        IApplication application,
        PacketUploadConfig draft,
        bool isFirstRun,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var dialog = new Dialog
        {
            Title = isFirstRun ? "GamePacketCollector 首次配置" : "GamePacketCollector 配置",
            Width = 90,
            Height = 18,
        };
        dialog.Add(new Label
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = 5,
            Text = "上传内容: 完整 request/response 原包，包含 ViewerId 等真实账号标识。\n"
                 + "用途: 统计育成事件效果、事件成功率、剧本特性和其它游戏机制。\n"
                 + "数据不会分享给第三方。",
        });
        var consent = new CheckBox
        {
            Text = "允许不匿名上传游戏数据包到 URACloud",
            Value = draft.Enabled ? CheckState.Checked : CheckState.UnChecked,
            CanFocus = false,
        };
        var singleMode = new CheckBox
        {
            Text = "育成数据 (SingleMode*)（启用上传时必选）",
            Value = CheckState.Checked,
            Enabled = false,
            CanFocus = false,
        };
        var selectedGroups = draft.EndpointGroups.ToHashSet(StringComparer.Ordinal);
        var optional = OptionalEndpointGroupOptions
            .Select(option => new
            {
                Option = option,
                CheckBox = new CheckBox
                {
                    Text = option.Label,
                    Value = selectedGroups.Contains(option.Group) ? CheckState.Checked : CheckState.UnChecked,
                    Enabled = draft.Enabled,
                    CanFocus = false,
                },
            })
            .ToArray();
        var consentItem = new MenuItem { CommandView = consent };
        var singleModeItem = new MenuItem { CommandView = singleMode, Enabled = false };
        var optionalItems = optional
            .Select(item => new MenuItem
            {
                CommandView = item.CheckBox,
                Enabled = draft.Enabled,
            })
            .ToArray();
        consent.ValueChanged += (_, _) =>
        {
            var enabled = consent.Value == CheckState.Checked;
            foreach (var (item, menuItem) in optional.Zip(optionalItems))
            {
                item.CheckBox.Enabled = enabled;
                menuItem.Enabled = enabled;
            }
        };

        var accepted = false;
        var save = new MenuItem("保存", action: () =>
        {
            accepted = true;
            application.RequestStop(dialog);
        });
        var cancel = new MenuItem("取消", action: () => application.RequestStop(dialog));
        var menu = new Menu([consentItem, singleModeItem, ..optionalItems, save, cancel])
        {
            X = 0,
            Y = 6,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        dialog.Add(menu);
        consentItem.SetFocus();

        using (cancellationToken.Register(
                   () => application.Invoke(() => application.RequestStop(dialog))))
            application.Run(dialog);
        cancellationToken.ThrowIfCancellationRequested();
        if (!accepted)
            throw new OperationCanceledException("GamePacketCollector 配置已取消。", cancellationToken);

        var enabledUpload = consent.Value == CheckState.Checked;
        return new()
        {
            UploadUrl = draft.UploadUrl,
            ServerRegionHint = draft.ServerRegionHint,
            Enabled = enabledUpload,
            EndpointGroups =
            [
                PacketUploadConfig.SingleModeEndpointGroup,
                ..(enabledUpload
                    ? optional.Where(x => x.CheckBox.Value == CheckState.Checked).Select(x => x.Option.Group)
                    : []),
            ],
        };
    }

    async ValueTask ConfigureFirstRunAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                await ConfigPromptAsync(context.Application, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                TerminalUi.Log(
                    "GamePacketCollector",
                    "GamePacketCollector 首次配置已取消；未写入配置，也未注册 analyzer。",
                    UiSeverity.Info);
                return;
            }

            uploadConfig = PacketUploadConfig.Load(ConfigPath, JsonOptions);
            var activationMessage = Activate(context);
            TerminalUi.Log("GamePacketCollector", activationMessage, UiSeverity.Info);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    string Activate(IPluginContext context)
    {
        if (!uploadConfig.Enabled)
            return $"GamePacketCollector 上传未启用，未注册 raw analyzer。配置文件: {ConfigPath}";

        var captureEndpoints = ResolveCaptureEndpoints(uploadConfig);
        pendingDirectory = Path.Combine(DataDirectory, "pending");
        failedDirectory = Path.Combine(DataDirectory, "failed");
        Directory.CreateDirectory(pendingDirectory);
        Directory.CreateDirectory(failedDirectory);

        exchangeBuffer = new();
        captureEndpointsByPath = captureEndpoints.ToDictionary(endpoint => endpoint.Path, StringComparer.Ordinal);
        RegisterAnalyzers(context.Analyzers, captureEndpoints);
        StartUploader(context);

        return $"GamePacketCollector 已注册 {captureEndpoints.Count} 个端点的 raw request/response 采集，上传到 {uploadConfig.UploadUrl}，pending 目录: {pendingDirectory}";
    }

    public void Dispose()
    {
        var cleanupExceptions = new List<Exception>();
        void Capture(Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception ex)
            {
                cleanupExceptions.Add(ex);
            }
        }

        Capture(() => uploadQueue.Writer.TryComplete());
        Capture(() => httpClient?.Dispose());

        if (cleanupExceptions.Count > 0)
            throw new AggregateException(cleanupExceptions);
    }

    ValueTask CaptureRequest(PacketCaptureEndpoint endpoint, ReadOnlyMemory<byte> msgpack, GameHttpHeaders headers)
    {
        try
        {
            if (exchangeBuffer is null)
                throw new InvalidOperationException("GamePacketCollector is not initialized.");

            exchangeBuffer.RecordRequest(endpoint, msgpack, headers);
        }
        catch (Exception ex)
        {
            LogCallbackException(ex);
#if DEBUG
            throw;
#endif
        }
        return ValueTask.CompletedTask;
    }

    ValueTask CaptureResponse(PacketCaptureEndpoint endpoint, ReadOnlyMemory<byte> msgpack)
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
            LogCallbackException(ex);
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
        var tempFile = Path.Combine(
            pendingDirectory,
            $".{exchange.PacketIdemKey}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, file, overwrite: true);
        }
        finally
        {
            File.Delete(tempFile);
        }

        uploadQueue.Writer.TryWrite(file);
    }

    void StartUploader(IPluginContext context)
    {
        httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"GamePacketCollector/{PluginVersion()}");
        uploader = new(httpClient, uploadConfig.UploadUrl);

        foreach (var file in Directory.GetFiles(pendingDirectory, "*.json", SearchOption.TopDirectoryOnly))
            uploadQueue.Writer.TryWrite(file);

        context.RunBackground(cancellationToken => new(UploadLoop(cancellationToken)));
    }

    async Task UploadLoop(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var file in uploadQueue.Reader.ReadAllAsync(cancellationToken))
                await TryUploadFile(file, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (PacketUploadException ex) when (!ex.IsRetriable)
        {
            LogUploaderException(ex);
            MoveToDatedDirectory(file, failedDirectory);
            if (Interlocked.Exchange(ref permanentUploadFailureNotified, 1) == 0)
            {
                TerminalUi.Notify(
                    "GamePacketCollector",
                    $"上传失败，数据已移入 failed 目录：{ex.Message}",
                    UiSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            LogUploaderException(ex);
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

    void DeleteUploadedPendingFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex)
        {
            LogUploaderException(ex);
        }
    }
    static void MoveToDatedDirectory(string file, string targetRoot)
    {
        var dayDirectory = Path.Combine(targetRoot, DateTime.UtcNow.ToString("yyyyMMdd"));
        Directory.CreateDirectory(dayDirectory);
        File.Move(file, Path.Combine(dayDirectory, Path.GetFileName(file)), overwrite: true);
    }

    void RegisterAnalyzers(
        IPluginAnalyzerRegistry registry,
        IReadOnlyList<PacketCaptureEndpoint> captureEndpoints)
    {
        var patterns = PacketCaptureCatalog.BuildPatterns(captureEndpoints);
        var byPath = captureEndpointsByPath
                     ?? throw new InvalidOperationException("GamePacketCollector capture endpoint map is missing.");
        registry.Register<ReadOnlyMemory<byte>>(
            AnalyzerKind.Request,
            patterns,
            invocation => CaptureRequest(
                byPath[invocation.Endpoint.Path],
                invocation.Payload,
                invocation.Headers));
        registry.Register<ReadOnlyMemory<byte>>(
            AnalyzerKind.Response,
            patterns,
            invocation => CaptureResponse(
                byPath[invocation.Endpoint.Path],
                invocation.Payload));
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

    void LogCallbackException(Exception exception)
        => TerminalUi.Log("GamePacketCollector", exception.ToString(), UiSeverity.Error);

    void LogUploaderException(Exception exception)
        => TerminalUi.Log("GamePacketCollector", exception.ToString(), UiSeverity.Error);

    string PluginVersion()
        => GetType().Assembly.GetName().Version?.ToString()
           ?? throw new InvalidOperationException("GamePacketCollector assembly version is missing.");

    sealed record EndpointGroupOption(string Group, string Label);
}
