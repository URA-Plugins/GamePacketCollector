namespace GamePacketCollector.Capture;

public sealed record PacketCaptureExchange(
    string PacketIdemKey,
    string EndpointType,
    string EndpointPath,
    string Group,
    string? Sid,
    string? AppVersion,
    string? GameDataVersion,
    string? ViewerId,
    string? Device,
    string? DeviceSubtype,
    byte[] Request,
    byte[] Response,
    DateTimeOffset CapturedAt);

public sealed record PacketUploadEnvelope
{
    public string SchemaVersion { get; init; } = "1";
    public string Kind { get; init; } = "game-packet";
    public required string CapturedAt { get; init; }
    public required string PacketIdemKey { get; init; }
    public required string EndpointType { get; init; }
    public required string EndpointPath { get; init; }
    public required string Group { get; init; }
    public required byte[] Request { get; init; }
    public required byte[] Response { get; init; }
    public string? ServerRegionHint { get; init; }
    public string? Sid { get; init; }
    public string? GameDataVersion { get; init; }
    public string? AppVersion { get; init; }
    public string? ViewerId { get; init; }
    public string? Device { get; init; }
    public string? DeviceSubtype { get; init; }

    public static PacketUploadEnvelope FromExchange(
        PacketCaptureExchange exchange,
        string? serverRegionHint = null) => new()
        {
            CapturedAt = exchange.CapturedAt.ToString("O"),
            PacketIdemKey = exchange.PacketIdemKey,
            EndpointType = exchange.EndpointType,
            EndpointPath = exchange.EndpointPath,
            Group = exchange.Group,
            Request = exchange.Request,
            Response = exchange.Response,
            ServerRegionHint = serverRegionHint,
            Sid = exchange.Sid,
            GameDataVersion = exchange.GameDataVersion,
            AppVersion = exchange.AppVersion,
            ViewerId = exchange.ViewerId,
            Device = exchange.Device,
            DeviceSubtype = exchange.DeviceSubtype,
        };
}