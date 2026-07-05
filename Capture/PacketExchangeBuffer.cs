using System.Security.Cryptography;
using System.Text;
using UmamusumeResponseAnalyzer.Plugin;

namespace GamePacketCollector.Capture;

public sealed class PacketExchangeBuffer
{
    const int MaxPendingPerEndpoint = 8;

    static readonly byte[] Separator = [0];

    readonly object gate = new();
    readonly Dictionary<Type, Queue<PendingRequest>> pendingRequests = [];

    public void RecordRequest(PacketCaptureEndpoint endpoint, byte[] request, GameHttpHeaders headers)
    {
        ValidatePacketIdemKeyHeaders(headers);

        lock (gate)
        {
            if (!pendingRequests.TryGetValue(endpoint.EndpointType, out var queue))
            {
                queue = new();
                pendingRequests[endpoint.EndpointType] = queue;
            }

            queue.Enqueue(new([.. request], headers));
            while (queue.Count > MaxPendingPerEndpoint)
                queue.Dequeue();
        }
    }

    public PacketCaptureExchange? RecordResponse(PacketCaptureEndpoint endpoint, byte[] response)
    {
        PendingRequest request;
        lock (gate)
        {
            if (!pendingRequests.TryGetValue(endpoint.EndpointType, out var queue) || queue.Count == 0)
                return null;

            request = queue.Dequeue();
        }

        var responseCopy = response.ToArray();
        return new(
            PacketIdemKey: CreatePacketIdemKey(endpoint.Path, request.Headers, request.Payload, responseCopy),
            EndpointType: endpoint.EndpointType.FullName ?? endpoint.EndpointType.Name,
            EndpointPath: endpoint.Path,
            Group: endpoint.Group,
            Sid: request.Headers.Sid,
            AppVersion: request.Headers.AppVer,
            GameDataVersion: request.Headers.ResVer,
            ViewerId: request.Headers.ViewerId,
            Device: request.Headers.Device,
            DeviceSubtype: request.Headers.DeviceSubtype,
            Request: request.Payload,
            Response: responseCopy,
            CapturedAt: DateTimeOffset.UtcNow);
    }

    static void ValidatePacketIdemKeyHeaders(GameHttpHeaders headers)
    {
        RequireHeader(headers.ViewerId, "X-Hachimi-viewerid");
        RequireHeader(headers.ResVer, "X-Hachimi-res-ver");
        RequireHeader(headers.AppVer, "X-Hachimi-app-ver");
    }

    static string CreatePacketIdemKey(string endpointPath, GameHttpHeaders headers, byte[] request, byte[] response)
    {
        var requestHash = SHA256.HashData(request);
        var responseHash = SHA256.HashData(response);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendUtf8(hash, RequireHeader(headers.ViewerId, "X-Hachimi-viewerid"));
        AppendSeparator(hash);
        AppendUtf8(hash, RequireHeader(headers.ResVer, "X-Hachimi-res-ver"));
        AppendSeparator(hash);
        AppendUtf8(hash, RequireHeader(headers.AppVer, "X-Hachimi-app-ver"));
        AppendSeparator(hash);
        AppendUtf8(hash, endpointPath);
        AppendSeparator(hash);
        hash.AppendData(requestHash);
        AppendSeparator(hash);
        hash.AppendData(responseHash);

        return ToBase64Url(hash.GetHashAndReset());
    }

    static string RequireHeader(string? value, string headerName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"GamePacketCollector requires {headerName} to create PacketIdemKey.")
            : value;

    static void AppendUtf8(IncrementalHash hash, string value)
        => hash.AppendData(Encoding.UTF8.GetBytes(value));

    static void AppendSeparator(IncrementalHash hash)
        => hash.AppendData(Separator);

    static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    sealed record PendingRequest(byte[] Payload, GameHttpHeaders Headers);
}