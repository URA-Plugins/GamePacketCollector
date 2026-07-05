using System.Net;
using System.Text;

namespace GamePacketCollector;

public sealed class PacketUploader(HttpClient httpClient, string uploadUrl)
{
    const int MaxErrorBodyBytes = 4096;

    public async Task UploadAsync(string json, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode)
            return;

        var body = await ReadErrorBodyAsync(response.Content, cancellationToken);
        throw new PacketUploadException(
            response.StatusCode,
            response.ReasonPhrase,
            body,
            IsRetriable(response.StatusCode));
    }

    static bool IsRetriable(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
           (int)statusCode >= 500;

    static async Task<string> ReadErrorBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaxErrorBodyBytes + 1];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
                break;

            total += read;
        }

        var bodyBytes = Math.Min(total, MaxErrorBodyBytes);
        var body = Encoding.UTF8.GetString(buffer, 0, bodyBytes);
        return total > MaxErrorBodyBytes ? body + "... <truncated>" : body;
    }
}

public sealed class PacketUploadException(
    HttpStatusCode statusCode,
    string? reasonPhrase,
    string responseBody,
    bool isRetriable)
    : InvalidOperationException(CreateMessage(statusCode, reasonPhrase, responseBody))
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public bool IsRetriable { get; } = isRetriable;
    public string ResponseBody { get; } = responseBody;

    static string CreateMessage(HttpStatusCode statusCode, string? reasonPhrase, string responseBody)
    {
        var status = $"GamePackets upload failed: {(int)statusCode} {reasonPhrase}".TrimEnd();
        return string.IsNullOrWhiteSpace(responseBody) ? status : $"{status}. {responseBody}";
    }
}