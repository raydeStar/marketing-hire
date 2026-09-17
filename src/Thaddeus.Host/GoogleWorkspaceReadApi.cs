using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>Small, read-only Google Workspace adapters used by the built-in Google connection.</summary>
internal static class GoogleWorkspaceReadApi
{
    public const string GmailSearch = "gmail.messages.search";
    public const string GmailGet = "gmail.messages.get";
    public const string CalendarList = "calendar.calendars.list";
    public const string CalendarEvents = "calendar.events.list";
    public const string CalendarEvent = "calendar.events.get";
    public const string CalendarFreeBusy = "calendar.freebusy.query";

    public static McpToolRecord[] GmailTools(Func<string, string> modelName) =>
    [
        Tool(GmailSearch, modelName, "Search read-only Gmail messages and return from address, subject, timestamp, snippet, and a Gmail link without changing the mailbox.", new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", maxLength = 500, description = "Optional Gmail search query, such as in:inbox or is:unread." },
                maxResults = new { type = "integer", minimum = 1, maximum = 50, @default = 10 },
                since = new { type = "string", description = "Optional ISO-8601 timestamp; only newer messages are returned." },
                unreadOnly = new { type = "boolean", @default = false }
            },
            additionalProperties = false
        }),
        Tool(GmailGet, modelName, "Read one exact Gmail message by ID, including bounded plain-text content, without marking it read or changing it.", new
        {
            type = "object",
            properties = new { messageId = new { type = "string", minLength = 1, maxLength = 512 } },
            required = new[] { "messageId" },
            additionalProperties = false
        })
    ];

    public static McpToolRecord[] CalendarTools(Func<string, string> modelName) =>
    [
        Tool(CalendarList, modelName, "List calendars available to the connected Google account without changing them.", new
        {
            type = "object",
            properties = new { maxResults = new { type = "integer", minimum = 1, maximum = 50, @default = 20 } },
            additionalProperties = false
        }),
        Tool(CalendarEvents, modelName, "List read-only Google Calendar events in an exact time range without accepting, editing, or deleting them.", new
        {
            type = "object",
            properties = new
            {
                calendarId = new { type = "string", maxLength = 512, @default = "primary" },
                timeMin = new { type = "string", description = "Inclusive ISO-8601 range start." },
                timeMax = new { type = "string", description = "Exclusive ISO-8601 range end." },
                query = new { type = "string", maxLength = 500 },
                maxResults = new { type = "integer", minimum = 1, maximum = 50, @default = 20 }
            },
            required = new[] { "timeMin", "timeMax" },
            additionalProperties = false
        }),
        Tool(CalendarEvent, modelName, "Read one exact Google Calendar event without changing it.", new
        {
            type = "object",
            properties = new
            {
                calendarId = new { type = "string", maxLength = 512, @default = "primary" },
                eventId = new { type = "string", minLength = 1, maxLength = 1024 }
            },
            required = new[] { "eventId" },
            additionalProperties = false
        }),
        Tool(CalendarFreeBusy, modelName, "Read free and busy periods for up to 20 exact Google calendars in a bounded time range.", new
        {
            type = "object",
            properties = new
            {
                timeMin = new { type = "string", description = "Inclusive ISO-8601 range start." },
                timeMax = new { type = "string", description = "Exclusive ISO-8601 range end." },
                calendarIds = new { type = "array", minItems = 1, maxItems = 20, items = new { type = "string", maxLength = 512 } }
            },
            required = new[] { "timeMin", "timeMax", "calendarIds" },
            additionalProperties = false
        })
    ];

    public static bool IsGmailTool(string name) => name is GmailSearch or GmailGet;
    public static bool IsCalendarTool(string name) => name is CalendarList or CalendarEvents or CalendarEvent or CalendarFreeBusy;

    public static async Task VerifyGmail(HttpClient http, string accessToken, CancellationToken cancellation)
    {
        using var response = await Send(http, accessToken, HttpMethod.Get,
            "https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults=1&includeSpamTrash=false&fields=resultSizeEstimate", null, cancellation);
        await Ensure(response, "Gmail read", cancellation);
    }

    public static async Task VerifyCalendar(HttpClient http, string accessToken, CancellationToken cancellation)
    {
        using var response = await Send(http, accessToken, HttpMethod.Get,
            "https://www.googleapis.com/calendar/v3/users/me/calendarList?maxResults=1&fields=items(id)", null, cancellation);
        await Ensure(response, "Google Calendar read", cancellation);
    }

    public static async Task<JsonElement> CallGmail(HttpClient http, string accessToken, string name, JsonElement arguments,
        CancellationToken cancellation) => name switch
    {
        GmailSearch => await SearchGmail(http, accessToken, arguments, cancellation),
        GmailGet => await GetGmail(http, accessToken, arguments, cancellation),
        _ => throw new InvalidOperationException("The reviewed Gmail read tool is unavailable.")
    };

    public static async Task<JsonElement> CallCalendar(HttpClient http, string accessToken, string name, JsonElement arguments,
        CancellationToken cancellation) => name switch
    {
        CalendarList => await ListCalendars(http, accessToken, arguments, cancellation),
        CalendarEvents => await ListEvents(http, accessToken, arguments, cancellation),
        CalendarEvent => await GetEvent(http, accessToken, arguments, cancellation),
        CalendarFreeBusy => await FreeBusy(http, accessToken, arguments, cancellation),
        _ => throw new InvalidOperationException("The reviewed Google Calendar read tool is unavailable.")
    };

    private static McpToolRecord Tool(string name, Func<string, string> modelName, string description, object schema) =>
        new(name, modelName(name), description, JsonSerializer.SerializeToElement(schema, Wire.Json), "read external data");

    private static async Task<JsonElement> SearchGmail(HttpClient http, string token, JsonElement arguments, CancellationToken cancellation)
    {
        Fields(arguments, "query", "maxResults", "since", "unreadOnly");
        var limit = Number(arguments, "maxResults", 10, 1, 50);
        var query = Optional(arguments, "query", 500) ?? "";
        if (Boolean(arguments, "unreadOnly")) query = Join(query, "is:unread");
        if (Optional(arguments, "since", 100) is { } since)
        {
            var instant = Instant(since, "Gmail since");
            query = Join(query, "after:" + instant.ToUnixTimeSeconds());
        }
        var url = "https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults=" + limit +
            "&includeSpamTrash=false&fields=messages(id%2CthreadId)%2CnextPageToken%2CresultSizeEstimate" +
            (query.Length == 0 ? "" : "&q=" + Uri.EscapeDataString(query));
        using var response = await Send(http, token, HttpMethod.Get, url, null, cancellation);
        using var document = await Json(response, "Gmail read", cancellation);
        var root = document.RootElement;
        var messages = new List<object>();
        if (root.TryGetProperty("messages", out var listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in listed.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var value) ? value.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Gmail returned an unreadable message list.");
                messages.Add(await GmailMetadata(http, token, id, cancellation));
            }
        }
        return JsonSerializer.SerializeToElement(new
        {
            messages,
            nextPageToken = root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null,
            resultSizeEstimate = root.TryGetProperty("resultSizeEstimate", out var size) && size.TryGetInt32(out var count) ? count : messages.Count,
            readOnly = true
        }, Wire.Json);
    }

    private static async Task<object> GmailMetadata(HttpClient http, string token, string id, CancellationToken cancellation)
    {
        var url = "https://gmail.googleapis.com/gmail/v1/users/me/messages/" + Uri.EscapeDataString(id) +
            "?format=metadata&metadataHeaders=From&metadataHeaders=Subject&metadataHeaders=Date&fields=id%2CthreadId%2ClabelIds%2Csnippet%2CinternalDate%2Cpayload%2Fheaders";
        using var response = await Send(http, token, HttpMethod.Get, url, null, cancellation);
        using var document = await Json(response, "Gmail read", cancellation);
        return NormalizeMessage(document.RootElement, includeBody: false);
    }

    private static async Task<JsonElement> GetGmail(HttpClient http, string token, JsonElement arguments, CancellationToken cancellation)
    {
        Fields(arguments, "messageId");
        var id = Required(arguments, "messageId", 512);
        var url = "https://gmail.googleapis.com/gmail/v1/users/me/messages/" + Uri.EscapeDataString(id) + "?format=full";
        using var response = await Send(http, token, HttpMethod.Get, url, null, cancellation);
        using var document = await Json(response, "Gmail read", cancellation);
        return JsonSerializer.SerializeToElement(NormalizeMessage(document.RootElement, includeBody: true), Wire.Json);
    }

    private static object NormalizeMessage(JsonElement root, bool includeBody)
    {
        var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Gmail returned an unreadable message.");
        var thread = root.TryGetProperty("threadId", out var threadValue) ? threadValue.GetString() : null;
        var headers = root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("headers", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToArray() : [];
        string? Header(string name)
        {
            foreach (var item in headers)
                if (item.TryGetProperty("name", out var key) && string.Equals(key.GetString(), name, StringComparison.OrdinalIgnoreCase) &&
                    item.TryGetProperty("value", out var value)) return value.GetString();
            return null;
        }
        var labels = root.TryGetProperty("labelIds", out var labelList) && labelList.ValueKind == JsonValueKind.Array
            ? labelList.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray() : [];
        var internalDate = root.TryGetProperty("internalDate", out var timestamp) && long.TryParse(timestamp.GetString(), out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToString("O") : null;
        return new
        {
            id,
            threadId = thread,
            sender = Header("From"),
            subject = Header("Subject"),
            receivedAt = internalDate,
            dateHeader = Header("Date"),
            snippet = root.TryGetProperty("snippet", out var snippet) ? snippet.GetString() : null,
            body = includeBody && root.TryGetProperty("payload", out payload) ? MessageBody(payload, 20_000) : null,
            labelIds = labels,
            webLink = "https://mail.google.com/mail/u/0/#inbox/" + Uri.EscapeDataString(id),
            readOnly = true
        };
    }

    private static string MessageBody(JsonElement payload, int limit)
    {
        var output = new StringBuilder();
        Visit(payload);
        return output.ToString().Trim();

        void Visit(JsonElement part)
        {
            if (output.Length >= limit || part.ValueKind != JsonValueKind.Object) return;
            var mime = part.TryGetProperty("mimeType", out var type) ? type.GetString() : null;
            if (mime == "text/plain" && part.TryGetProperty("body", out var body) && body.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String)
            {
                try
                {
                    var encoded = data.GetString()!.Replace('-', '+').Replace('_', '/');
                    encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
                    var text = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                    output.Append(text.AsSpan(0, Math.Min(text.Length, limit - output.Length)));
                    output.AppendLine();
                }
                catch (FormatException) { }
            }
            if (part.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
                foreach (var child in parts.EnumerateArray()) Visit(child);
        }
    }

    private static async Task<JsonElement> ListCalendars(HttpClient http, string token, JsonElement arguments, CancellationToken cancellation)
    {
        Fields(arguments, "maxResults");
        var limit = Number(arguments, "maxResults", 20, 1, 50);
        var url = "https://www.googleapis.com/calendar/v3/users/me/calendarList?maxResults=" + limit +
            "&fields=items(id%2Csummary%2Cdescription%2Clocation%2CtimeZone%2Cprimary%2Cselected%2CaccessRole)%2CnextPageToken";
        using var response = await Send(http, token, HttpMethod.Get, url, null, cancellation);
        using var document = await Json(response, "Google Calendar read", cancellation);
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> ListEvents(HttpClient http, string token, JsonElement arguments, CancellationToken cancellation)
    {
        Fields(arguments, "calendarId", "timeMin", "timeMax", "query", "maxResults");
        var calendar = Optional(arguments, "calendarId", 512) ?? "primary";
        var (start, end) = Range(arguments);
        var limit = Number(arguments, "maxResults", 20, 1, 50);
        var query = Optional(arguments, "query", 500);
        var url = "https://www.googleapis.com/calendar/v3/calendars/" + Uri.EscapeDataString(calendar) + "/events?singleEvents=true&orderBy=startTime&maxResults=" + limit +
            "&timeMin=" + Uri.EscapeDataString(start.ToString("O")) + "&timeMax=" + Uri.EscapeDataString(end.ToString("O")) +
            "&fields=items(id%2Cstatus%2Csummary%2Cdescription%2Clocation%2Cstart%2Cend%2ChtmlLink%2Cattendees(email%2CresponseStatus%2Cself))%2CnextPageToken" +
            (query == null ? "" : "&q=" + Uri.EscapeDataString(query));
        using var response = await Send(http, token, HttpMethod.Get, url, null, cancellation);
        using var document = await Json(response, "Google Calendar read", cancellation);
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> GetEvent(HttpClient http, string token, JsonElement arguments, CancellationToken cancellation)
    {
        Fields(arguments, "calendarId", "eventId");
        var calendar = Optional(arguments, "calendarId", 512) ?? "primary";
        var id = Required(arguments, "eventId", 1024);
        var url = "https://www.googleapis.com/calendar/v3/calendars/" + Uri.EscapeDataString(calendar) + "/events/" + Uri.EscapeDataString(id) +
            "?fields=id%2Cstatus%2Csummary%2Cdescription%2Clocation%2Cstart%2Cend%2ChtmlLink%2Cattendees(email%2CresponseStatus%2Cself)";
        using var response = await Send(http, token, HttpMethod.Get, url, null, cancellation);
        using var document = await Json(response, "Google Calendar read", cancellation);
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> FreeBusy(HttpClient http, string token, JsonElement arguments, CancellationToken cancellation)
    {
        Fields(arguments, "timeMin", "timeMax", "calendarIds");
        var (start, end) = Range(arguments);
        if (!arguments.TryGetProperty("calendarIds", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() is < 1 or > 20)
            throw new ArgumentException("Choose 1 through 20 exact calendar IDs.");
        var calendars = ids.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null).ToArray();
        if (calendars.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 512)) throw new ArgumentException("A calendar ID is invalid.");
        var content = JsonContent.Create(new { timeMin = start.ToString("O"), timeMax = end.ToString("O"), items = calendars.Select(id => new { id }) }, options: Wire.Json);
        using var response = await Send(http, token, HttpMethod.Post, "https://www.googleapis.com/calendar/v3/freeBusy", content, cancellation);
        using var document = await Json(response, "Google Calendar read", cancellation);
        return document.RootElement.Clone();
    }

    private static (DateTimeOffset Start, DateTimeOffset End) Range(JsonElement arguments)
    {
        var start = Instant(Required(arguments, "timeMin", 100), "calendar range start");
        var end = Instant(Required(arguments, "timeMax", 100), "calendar range end");
        if (end <= start || end - start > TimeSpan.FromDays(366)) throw new ArgumentException("The calendar range must be positive and no longer than 366 days.");
        return (start, end);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient http, string token, HttpMethod method, string url, HttpContent? content,
        CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try { return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation); }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
        { throw new InvalidOperationException("Google Workspace could not be reached for the reviewed read-only request.", error); }
    }

    private static async Task Ensure(HttpResponseMessage response, string service, CancellationToken cancellation)
    {
        if (response.IsSuccessStatusCode) return;
        _ = await response.Content.ReadAsStringAsync(cancellation);
        throw new InvalidOperationException(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? $"{service} access is unavailable, revoked, or not enabled for this Google project."
            : $"{service} returned HTTP {(int)response.StatusCode}; no data was accepted.");
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response, string service, CancellationToken cancellation)
    {
        await Ensure(response, service, cancellation);
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation)); }
        catch (JsonException error) { throw new InvalidOperationException(service + " returned an unreadable response.", error); }
    }

    private static void Fields(JsonElement arguments, params string[] allowed)
    {
        if (arguments.ValueKind != JsonValueKind.Object || arguments.GetRawText().Length > 30_000 ||
            arguments.EnumerateObject().Any(item => !allowed.Contains(item.Name, StringComparer.Ordinal)))
            throw new ArgumentException("The Google read request contains unsupported arguments.");
    }

    private static string Required(JsonElement arguments, string name, int maximum) =>
        Optional(arguments, name, maximum) ?? throw new ArgumentException("The Google read request needs " + name + ".");

    private static string? Optional(JsonElement arguments, string name, int maximum)
    {
        if (!arguments.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) throw new ArgumentException(name + " must be text.");
        var text = value.GetString()?.Trim() ?? "";
        if (text.Length is < 1 || text.Length > maximum || text.Any(char.IsControl)) throw new ArgumentException(name + " is outside its allowed length.");
        return text;
    }

    private static int Number(JsonElement arguments, string name, int fallback, int minimum, int maximum)
    {
        if (!arguments.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < minimum || number > maximum)
            throw new ArgumentException(name + $" must be between {minimum} and {maximum}.");
        return number;
    }

    private static bool Boolean(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException(name + " must be true or false.");
        return value.GetBoolean();
    }

    private static DateTimeOffset Instant(string value, string label)
    {
        if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AllowWhiteSpaces | System.Globalization.DateTimeStyles.AssumeUniversal, out var instant))
            throw new ArgumentException("The " + label + " must be an ISO-8601 timestamp.");
        return instant.ToUniversalTime();
    }

    private static string Join(string left, string right) => string.IsNullOrWhiteSpace(left) ? right : left.Trim() + " " + right;
}
