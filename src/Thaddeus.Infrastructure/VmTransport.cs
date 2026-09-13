using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thaddeus.Infrastructure;

// Shared host transport boundaries. A VM guest supplies data, never the destination or HTTP client policy.
public sealed class VmBrokerProxy : IDisposable
{
    private readonly QemuBrokerRoute route;
    private readonly HttpClient http;
    public VmBrokerProxy(QemuBrokerRoute route) : this(route, new SocketsHttpHandler
    { UseProxy = false, UseCookies = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5) }) { }
    internal VmBrokerProxy(QemuBrokerRoute route, HttpMessageHandler handler)
    {
        if (!Regex.IsMatch(route.RunId, @"\A[a-f0-9]{32}\z") || route.Port is < 1024 or > 65535)
            throw new ArgumentException("Invalid task broker route.");
        this.route = route; http = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }
    public async Task<(int status, string body, string contentType)> Forward(JsonElement message, CancellationToken cancellation)
    {
        var path = message.GetProperty("path").GetString(); var method = message.GetProperty("method").GetString();
        if (path != $"/worker/{route.RunId}/mcp" && path != $"/worker/{route.RunId}/v1/chat/completions" || method is not ("GET" or "POST" or "DELETE"))
            throw new IOException("Guest broker route denied.");
        var encoded = message.GetProperty("body").GetString()!; if (encoded.Length > 200000) throw new IOException("Guest request exceeds its bound.");
        var body = Convert.FromBase64String(encoded); if (body.Length > 150000 || method == "GET" && body.Length != 0) throw new IOException("Invalid guest request body.");
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri($"http://127.0.0.1:{route.Port}" + path));
        if (method != "GET") request.Content = new ByteArrayContent(body);
        var total = 0;
        foreach (var header in message.GetProperty("headers").EnumerateObject())
        {
            total += header.Name.Length + header.Value.GetRawText().Length;
            if (total > 16000) throw new IOException("Guest headers exceed their bound.");
            if (header.Name is not ("authorization" or "content-type" or "accept" or "mcp-protocol-version" or "mcp-session-id")) continue;
            var value = header.Value.GetString()!; if (value.Length > 8192 || value.Contains('\r') || value.Contains('\n')) throw new IOException("Invalid guest header.");
            if (header.Name == "content-type") { if (request.Content != null) request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value); }
            else request.Headers.Add(header.Name, value);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromMinutes(5));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        await using var content = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await content.ReadAsync(buffer, deadline.Token)) > 0)
        { if (bytes.Length + count > 1500000) throw new IOException("Broker response exceeds its bound."); bytes.Write(buffer, 0, count); }
        return ((int)response.StatusCode, Convert.ToBase64String(bytes.ToArray()), response.Content.Headers.ContentType?.ToString() ?? "application/json");
    }

    public void Dispose() => http.Dispose();
}

public static class VmJsonFrames
{
    public static async Task Read(Stream stream, Action<JsonElement> receive, int limit, CancellationToken cancellation)
    {
        using var frame = new MemoryStream(); var buffer = new byte[16384]; int count, frames = 0;
        while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
        {
            var start = 0;
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] != 10) continue;
                if (frame.Length + i - start > limit || ++frames > 5000) throw new IOException("VM frame or message count exceeds its bound.");
                frame.Write(buffer, start, i - start);
                using var document = JsonDocument.Parse(frame.GetBuffer().AsMemory(0, (int)frame.Length), new() { MaxDepth = 32 });
                receive(document.RootElement); frame.SetLength(0); start = i + 1;
            }
            if (frame.Length + count - start > limit) throw new IOException("VM frame exceeds its bound.");
            frame.Write(buffer, start, count - start);
        }
        if (frame.Length != 0) throw new IOException("Truncated VM frame.");
    }
}

public sealed class VmBrokerQuota
{
    private readonly object gate = new();
    private readonly HashSet<string> used = [];
    private int active;
    public IDisposable Admit(string id)
    {
        lock (gate)
        {
            if (id.Length is < 6 or > 11 || !Regex.IsMatch(id, @"\Ahttp-[1-9][0-9]{0,5}\z") || used.Contains(id) || used.Count >= 200 || active >= 8)
                throw new IOException("Guest broker request limit or identity violated.");
            used.Add(id); active++;
            return new Lease(this);
        }
    }
    private sealed class Lease(VmBrokerQuota owner) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lock (owner.gate) owner.active--;
        }
    }
}
