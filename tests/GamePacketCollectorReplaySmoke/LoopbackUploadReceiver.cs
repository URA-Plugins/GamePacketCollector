using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

sealed class LoopbackUploadReceiver : IDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource stopping = new();
    readonly object gate = new();
    readonly Dictionary<UploadIdentity, HashSet<string?>> expected = [];
    readonly HashSet<UploadIdentity> validated = [];
    readonly Task serveTask;
    long expectedBytes;
    int uploadedPairs;
    long uploadedRawBytes;

    public LoopbackUploadReceiver()
    {
        listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/GamePackets";
        serveTask = ServeAsync(stopping.Token);
    }

    public string Url { get; }
    public int UploadedPairs => Volatile.Read(ref uploadedPairs);
    public long UploadedRawBytes => Interlocked.Read(ref uploadedRawBytes);

    public void Expect(string path, HttpMessageEnvelope request, HttpMessageEnvelope response)
    {
        var identity = new UploadIdentity(path, request.Headers.AppVer, request.Headers.ResVer,
            request.Headers.ViewerId, Convert.ToHexString(SHA256.HashData(request.Body)),
            Convert.ToHexString(SHA256.HashData(response.Body)));
        lock (gate)
        {
            if (!expected.TryGetValue(identity, out var sids))
            {
                expected.Add(identity, sids = []);
                expectedBytes += request.Body.Length + response.Body.Length;
            }
            sids.Add(request.Headers.Sid);
        }
    }

    public async Task WaitForDrainAsync(string pendingDirectory)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30 + expectedBytes * 2.0 / (256 * 1024));
        while (true)
        {
            if (serveTask.IsCompleted)
                await serveTask;
            lock (gate)
                if (expected.Count != 0 && validated.Count == expected.Count && !Directory.EnumerateFiles(pendingDirectory).Any())
                    return;
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Loopback uploads incomplete: expected unique pairs={expected.Count}, validated={validated.Count}, pending={Directory.EnumerateFiles(pendingDirectory).Count()}.");
            await Task.Delay(10);
        }
    }

    async Task ServeAsync(CancellationToken token)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(token);
            using var stream = client.GetStream();
            var headerBytes = new List<byte>();
            var next = new byte[1];
            while (headerBytes.Count < 4 || !headerBytes.TakeLast(4).SequenceEqual("\r\n\r\n"u8.ToArray()))
            {
                if (headerBytes.Count == 32 * 1024)
                    throw new InvalidDataException("Loopback HTTP headers exceeded 32 KiB.");
                await stream.ReadExactlyAsync(next, token);
                headerBytes.Add(next[0]);
            }
            var headers = Encoding.ASCII.GetString([.. headerBytes]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            if (headers[0] != "PUT /GamePackets HTTP/1.1")
                throw new InvalidDataException("Collector did not send PUT /GamePackets.");
            var length = int.Parse(headers.Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))[15..].Trim());
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, token);
            using var document = JsonDocument.Parse(body);
            var json = document.RootElement;
            var request = json.GetProperty("request").GetBytesFromBase64();
            var response = json.GetProperty("response").GetBytesFromBase64();
            var identity = new UploadIdentity(json.GetProperty("endpointPath").GetString()!,
                json.GetProperty("appVersion").GetString(), json.GetProperty("gameDataVersion").GetString(),
                json.GetProperty("viewerId").GetString(), Convert.ToHexString(SHA256.HashData(request)),
                Convert.ToHexString(SHA256.HashData(response)));
            lock (gate)
            {
                if (json.GetProperty("schemaVersion").GetString() != "2" || json.GetProperty("kind").GetString() != "game-packet"
                    || !expected.TryGetValue(identity, out var sids) || !sids.Contains(json.GetProperty("sid").GetString()))
                    throw new InvalidDataException("Collector loopback upload changed its source pair bytes, endpoint or headers.");
                validated.Add(identity);
            }
            Interlocked.Increment(ref uploadedPairs);
            Interlocked.Add(ref uploadedRawBytes, request.Length + response.Length);
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), token);
        }
    }

    public void Dispose()
    {
        stopping.Cancel();
        listener.Stop();
        try { serveTask.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        stopping.Dispose();
    }

    sealed record UploadIdentity(string Path, string? AppVersion, string? DataVersion,
        string? ViewerId, string RequestHash, string ResponseHash);
}
