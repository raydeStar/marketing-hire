using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>Texts the owner on this employee's Plow line when something is ready or it needs an answer. Many owners never
/// open the cockpit: the text is how they hear back. Each message is sent once; a send whose outcome is unknown is never
/// repeated. Quiet while the owner has the cockpit on screen, since they see it there.</summary>
public sealed class OwnerTexts
{
    const string SentKey = "owner-texts-sent-v1";
    public const int MaxLength = 1600, PerHour = 10;
    readonly Store store;
    readonly ILogger<OwnerTexts> logger;
    readonly HttpClient? http;
    readonly Queue<DateTimeOffset> recent = new();
    readonly object gate = new();
    string? chat;
    DateTimeOffset seen = DateTimeOffset.MinValue;

    /// <summary>Where the owner opens the cockpit, for the end of a text.</summary>
    public string? Link { get; }
    public bool Enabled => http != null;
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public OwnerTexts(IConfiguration config, Store store, ILogger<OwnerTexts> logger, HttpMessageHandler? handler = null)
    {
        this.store = store; this.logger = logger;
        Link = config["Thaddeus:PhoneOrigin"] ?? config["Thaddeus:LocalOrigin"];
        var token = config["PLOW_AGENT_TOKEN"];
        // Only a real Plow employee texts: a local cockpit has no line, and the offline fixture has no Plow at all.
        if (config["Thaddeus:PhoneMode"] != "plow" || token is not { Length: > 0 } || config["Thaddeus:OwnerTexts"] == "off" || config["Marketing:ShiftRuntime"] == "scripted") return;
        if (!Uri.TryCreate((config["PLOW_API_BASE"] ?? "https://api.plow.co").TrimEnd('/') + "/v1/", UriKind.Absolute, out var api) ||
            !(api.Scheme == Uri.UriSchemeHttps || api.Scheme == Uri.UriSchemeHttp && api.IsLoopback)) { logger.LogWarning("Owner texts are off: the Plow API address isn't usable."); return; }
        http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.BaseAddress = api; http.Timeout = TimeSpan.FromSeconds(15);
        // On a hosted install this is a placeholder the platform's proxy replaces; locally it is the agent's own credential.
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>The owner's cockpit is on screen (its pages only poll while visible).</summary>
    public void Seen() => seen = Clock();
    public bool Watching => Clock() - seen < TimeSpan.FromMinutes(2);

    /// <summary>Sends <paramref name="text"/> once for <paramref name="key"/>. False when not sent: not on Plow, the owner is
    /// watching the cockpit, already sent, too many this hour, or the send failed (logged, not retried).</summary>
    public async Task<bool> Send(string key, string text, CancellationToken cancellation)
    {
        if (http == null || Watching || text.Trim().Length == 0) return false;
        lock (gate)
        {
            var sent = Sent();
            if (sent.Contains(key)) return false;
            var now = Clock();
            while (recent.Count > 0 && now - recent.Peek() > TimeSpan.FromHours(1)) recent.Dequeue();
            if (recent.Count >= PerHour) { logger.LogWarning("Owner text skipped: {Count} already sent this hour.", recent.Count); return false; }
            recent.Enqueue(now);
            // Recorded before the send: a timeout could still have delivered it, and a repeat would text the owner twice.
            store.Setting(SentKey, JsonSerializer.Serialize(sent.Append(key).TakeLast(300).ToArray()));
        }
        if (text.Length > MaxLength) text = text[..(MaxLength - 1)].TrimEnd() + "…";
        try
        {
            chat ??= await OwnerChat(cancellation);
            using var response = await http.PostAsJsonAsync($"chats/{Uri.EscapeDataString(chat)}/messages", new { body = text, attachment_uids = Array.Empty<string>() }, cancellation);
            if (response.IsSuccessStatusCode) return true;
            if ((int)response.StatusCode is >= 400 and < 500) chat = null;   // the owner's chat may have changed; look it up next time
            logger.LogWarning("Owner text not sent: Plow answered {Status}.", (int)response.StatusCode);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        { logger.LogWarning("Owner text not sent: {Error}", error.Message); }
        return false;
    }

    string[] Sent() => store.Setting(SentKey) is { } value ? JsonSerializer.Deserialize<string[]>(value) ?? [] : [];

    static readonly HashSet<string> Yes = new(StringComparer.Ordinal) { "yes", "y", "yep", "yeah", "yup", "ok", "okay", "sure", "confirm", "confirmed", "approve", "approved", "go", "do", "👍", "✅" };
    static readonly HashSet<string> Not = new(StringComparer.Ordinal) { "no", "nope", "not", "don't", "dont", "wait", "cancel", "stop", "hold" };

    /// <summary>Whether the owner confirmed a change themselves: in their own chat, the employee sent the question carrying
    /// <paramref name="code"/>, and the owner's latest message after it is a plain yes. Read from Plow, where only the owner can
    /// write the owner's messages. Null when confirmed; otherwise what's missing.</summary>
    public async Task<string?> Unconfirmed(string code, CancellationToken cancellation)
    {
        if (http == null) return "Changes by text need the employee's Plow line.";
        chat ??= await OwnerChat(cancellation);
        var page = await http.GetFromJsonAsync<JsonElement>($"chats/{Uri.EscapeDataString(chat)}/messages?limit=30", cancellation);
        static string Str(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        static bool FromOwner(JsonElement message) => Str(message, "direction") == "inbound" && message.TryGetProperty("sender", out var sender) && Str(sender, "type") == "member" && Str(sender, "role") == "owner";
        // Newest first: the owner's latest message, and the question it answers.
        var messages = page.GetProperty("data").EnumerateArray().ToArray();
        var asked = Array.FindIndex(messages, message => Str(message, "direction") == "outbound" && Str(message, "body").Contains($"(change {code})", StringComparison.Ordinal));
        if (asked < 0) return $"The owner hasn't been sent change {code} word for word. Send the confirm text exactly as it was given.";
        var answer = messages.Take(asked).FirstOrDefault(FromOwner);
        if (answer.ValueKind != JsonValueKind.Object) return "The owner hasn't replied to it yet.";
        var words = Str(answer, "body").ToLowerInvariant().Split([' ', '\n', '\t', ',', '.', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || words.Length > 8 || !Yes.Contains(words[0]) || words.Any(Not.Contains)) return "The owner's reply wasn't a yes to it.";
        return null;
    }

    /// <summary>The owner's direct chat on this employee's own line: exactly two participants, this line and its owner.</summary>
    async Task<string> OwnerChat(CancellationToken cancellation)
    {
        var me = await http!.GetFromJsonAsync<JsonElement>("agents/me", cancellation);
        var line = me.TryGetProperty("line", out var own) && own.TryGetProperty("uid", out var uid) ? uid.GetString() : null;
        if (line is not { Length: > 0 }) throw new InvalidOperationException("Plow didn't say which line this employee answers on.");
        var listing = await http!.GetFromJsonAsync<JsonElement>("chats", cancellation);
        if (listing.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True) throw new InvalidOperationException("The chat list is too long to find the owner's chat.");
        static string Str(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        var owners = listing.GetProperty("data").EnumerateArray().Where(item => Str(item, "status") == "active" && item.TryGetProperty("participants", out var people) && people.GetArrayLength() == 2
            && people.EnumerateArray().Any(person => Str(person, "type") == "agent" && Str(person, "relationship") == "self" && person.TryGetProperty("line", out var at) && Str(at, "uid") == line)
            && people.EnumerateArray().Any(person => Str(person, "type") == "member" && Str(person, "role") == "owner")).Select(item => Str(item, "uid")).ToArray();
        return owners.Length == 1 ? owners[0] : throw new KeyNotFoundException(owners.Length == 0 ? "The owner hasn't texted this line yet." : "More than one owner chat on this line.");
    }
}
