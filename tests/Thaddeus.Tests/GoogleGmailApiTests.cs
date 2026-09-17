using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class GoogleGmailApiTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    [Fact]
    public async Task SendsExactReviewedMessageAndReturnsProviderAcceptanceSeparateFromDelivery()
    {
        string? mime = null;
        using var http = new HttpClient(new Handler(async (request, cancellation) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://gmail.googleapis.com/gmail/v1/users/me/messages/send", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal("fixture-access", request.Headers.Authorization.Parameter);
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            var raw = payload.RootElement.GetProperty("raw").GetString()!.Replace('-', '+').Replace('_', '/');
            raw += new string('=', (4 - raw.Length % 4) % 4);
            mime = Encoding.ASCII.GetString(Convert.FromBase64String(raw));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = "provider-message-1", threadId = "thread-1", labelIds = new[] { "SENT" } }) };
        }));
        var arguments = JsonSerializer.SerializeToElement(new { to = "recipient@example.com", subject = "Exact subject", body = "Exact body\nSecond line" });
        var result = await GoogleGmailApi.Send(http, "fixture-access", "sender@example.com", arguments, default);
        Assert.True(result.GetProperty("accepted").GetBoolean());
        Assert.Equal("provider-message-1", result.GetProperty("providerMessageId").GetString());
        Assert.False(result.GetProperty("recipientDeliveryObserved").GetBoolean());
        Assert.Contains("From: sender@example.com\r\n", mime); Assert.Contains("To: recipient@example.com\r\n", mime);
        Assert.Contains("Subject: =?UTF-8?B?", mime); Assert.DoesNotContain("fixture-access", mime);
        var body = Convert.ToBase64String(Encoding.UTF8.GetBytes("Exact body\nSecond line")); Assert.Contains(body, mime);
    }

    [Fact]
    public async Task AmbiguousTransportFailureIsUnknownAndNeverConvertedToAResend()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; throw new HttpRequestException("connection ended after write"); }));
        var arguments = JsonSerializer.SerializeToElement(new { to = "recipient@example.com", subject = "Fixture", body = "One attempt." });
        await Assert.ThrowsAsync<DelegationOutcomeUnknownException>(() => GoogleGmailApi.Send(http, "fixture-access", "sender@example.com", arguments, default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RevokedAccessAndHeaderInjectionFailWithoutAcceptance()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)); }));
        var exact = JsonSerializer.SerializeToElement(new { to = "recipient@example.com", subject = "Fixture", body = "No access." });
        await Assert.ThrowsAsync<InvalidOperationException>(() => GoogleGmailApi.Send(http, "fixture-access", "sender@example.com", exact, default));
        var hostile = JsonSerializer.SerializeToElement(new { to = "recipient@example.com", subject = "Fixture\r\nBcc: hidden@example.com", body = "Blocked." });
        await Assert.ThrowsAsync<ArgumentException>(() => GoogleGmailApi.Send(http, "fixture-access", "sender@example.com", hostile, default));
        Assert.Equal(1, calls);
    }
}
