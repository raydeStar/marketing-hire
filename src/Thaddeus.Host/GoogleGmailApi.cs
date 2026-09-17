using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>A deliberately narrow Gmail adapter: one reviewed message in, one provider send receipt out.</summary>
internal static class GoogleGmailApi
{
    public const string ToolName = "gmail.messages.send";

    public static McpToolRecord Tool(string modelName) => new(ToolName, modelName,
        "Send one exact approved email through the connected Gmail account. This sends a message; it does not create a draft.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                to = new { type = "string", description = "One exact recipient email address." },
                subject = new { type = "string", description = "The exact message subject." },
                body = new { type = "string", description = "The exact plain-text message body." }
            },
            required = new[] { "to", "subject", "body" },
            additionalProperties = false
        }, Wire.Json), "write or external action");

    public static async Task<JsonElement> Send(HttpClient http, string accessToken, string sender, JsonElement arguments,
        CancellationToken cancellation)
    {
        var message = Parse(sender, arguments);
        var raw = Base64Url(Mime(message));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send")
        {
            Content = JsonContent.Create(new { raw })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation); }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
        { throw new DelegationOutcomeUnknownException("The Gmail send outcome is unknown. No automatic retry was started."); }
        using (response)
        {
            if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                throw new DelegationOutcomeUnknownException("Gmail returned an ambiguous send outcome. No automatic retry was started.");
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "Gmail access was revoked or no longer permits sending. No email was accepted."
                    : "Gmail rejected the exact reviewed message. No email was accepted.");
            try
            {
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
                var id = body.RootElement.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(id)) throw new JsonException();
                var thread = body.RootElement.TryGetProperty("threadId", out var threadId) ? threadId.GetString() : null;
                var labels = body.RootElement.TryGetProperty("labelIds", out var labelIds) && labelIds.ValueKind == JsonValueKind.Array
                    ? labelIds.EnumerateArray().Select(value => value.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() : [];
                return JsonSerializer.SerializeToElement(new
                {
                    accepted = true,
                    operation = ToolName,
                    providerMessageId = id,
                    providerThreadId = thread,
                    labels,
                    sender = message.Sender,
                    recipient = message.Recipient,
                    recipientDeliveryObserved = false
                }, Wire.Json);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException)
            { throw new DelegationOutcomeUnknownException("Gmail accepted the request but returned no usable message receipt. No automatic retry was started."); }
        }
    }

    private static ExactMessage Parse(string sender, JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object || arguments.EnumerateObject().Any(item => item.Name is not ("to" or "subject" or "body")))
            throw new ArgumentException("The Gmail send request must contain only to, subject and body.");
        var recipient = Required(arguments, "to", 320);
        var subject = Required(arguments, "subject", 500);
        var body = Required(arguments, "body", 20_000);
        if (subject.Contains('\r') || subject.Contains('\n')) throw new ArgumentException("The email subject cannot contain line breaks.");
        return new(Address(sender, "connected sender"), Address(recipient, "recipient"), subject, body);
    }

    private static string Required(JsonElement source, string name, int maximum)
    {
        if (!source.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) throw new ArgumentException($"The email needs an exact {name}.");
        var text = value.GetString()?.Trim() ?? "";
        if (text.Length is < 1 || text.Length > maximum || text.IndexOf('\0') >= 0) throw new ArgumentException($"The email {name} is outside its reviewed limit.");
        return text;
    }

    private static string Address(string value, string label)
    {
        try
        {
            var parsed = new MailAddress(value);
            if (!string.Equals(parsed.Address, value, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(parsed.DisplayName)) throw new FormatException();
            return parsed.Address;
        }
        catch (FormatException) { throw new ArgumentException($"Enter one exact {label} email address without a display name."); }
    }

    private static byte[] Mime(ExactMessage message)
    {
        var subject = Convert.ToBase64String(Encoding.UTF8.GetBytes(message.Subject));
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes(message.Body));
        var mime = $"From: {message.Sender}\r\nTo: {message.Recipient}\r\nSubject: =?UTF-8?B?{subject}?=\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n{Wrap(content)}\r\n";
        return Encoding.ASCII.GetBytes(mime);
    }

    private static string Wrap(string value) => string.Join("\r\n", Enumerable.Range(0, (value.Length + 75) / 76)
        .Select(index => value.Substring(index * 76, Math.Min(76, value.Length - index * 76))));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private sealed record ExactMessage(string Sender, string Recipient, string Subject, string Body);
}
