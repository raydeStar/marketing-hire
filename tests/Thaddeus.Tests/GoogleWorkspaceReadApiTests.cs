using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class GoogleWorkspaceReadApiTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    [Fact]
    public async Task GmailSearchUsesStableReadApiAndReturnsWatchCompatibleMetadata()
    {
        var requests = new List<(HttpMethod Method, string Url)>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            var url = request.RequestUri!.AbsoluteUri; requests.Add((request.Method, url));
            Assert.Equal("Bearer fixture-token", request.Headers.Authorization?.ToString());
            if (url.Contains("/messages?", StringComparison.Ordinal))
            {
                var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
                Assert.Equal("2", query["maxResults"].ToString());
                Assert.Contains("in:inbox", query["q"].ToString());
                Assert.Contains("after:1789603200", query["q"].ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { messages = new[] { new { id = "mail-1", threadId = "thread-1" } }, resultSizeEstimate = 1 })
                });
            }
            Assert.Contains("/messages/mail-1?format=metadata", url);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    id = "mail-1", threadId = "thread-1", labelIds = new[] { "INBOX" }, snippet = "Please review by Friday.", internalDate = "1789689600000",
                    payload = new { headers = new[] { new { name = "From", value = "Avery <avery@example.test>" }, new { name = "Subject", value = "Contract deadline" } } }
                })
            });
        }));

        var result = await GoogleWorkspaceReadApi.CallGmail(http, "fixture-token", GoogleWorkspaceReadApi.GmailSearch,
            JsonSerializer.SerializeToElement(new { query = "in:inbox", maxResults = 2, since = "2026-09-17T00:00:00Z" }), default);

        var message = Assert.Single(result.GetProperty("messages").EnumerateArray());
        Assert.Equal("mail-1", message.GetProperty("id").GetString());
        Assert.Equal("Contract deadline", message.GetProperty("subject").GetString());
        Assert.Equal("Avery <avery@example.test>", message.GetProperty("sender").GetString());
        Assert.Equal("https://mail.google.com/mail/u/0/#inbox/mail-1", message.GetProperty("webLink").GetString());
        Assert.True(result.GetProperty("readOnly").GetBoolean());
        Assert.All(requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task GmailSearchTreatsAnEmptyOptionalQueryAsOmitted()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            Assert.False(query.ContainsKey("q"));
            Assert.Equal("1", query["maxResults"].ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { resultSizeEstimate = 0 })
            });
        }));

        var result = await GoogleWorkspaceReadApi.CallGmail(http, "fixture-token", GoogleWorkspaceReadApi.GmailSearch,
            JsonSerializer.SerializeToElement(new { query = "", maxResults = 1, unreadOnly = false }), default);

        Assert.Empty(result.GetProperty("messages").EnumerateArray());
        Assert.True(result.GetProperty("readOnly").GetBoolean());
    }

    [Fact]
    public async Task GmailGetReturnsBoundedPlainTextWithoutChangingTheMessage()
    {
        var body = Convert.ToBase64String(Encoding.UTF8.GetBytes("A precise fictional message body.")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("/messages/mail-2?format=full", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    id = "mail-2", threadId = "thread-2", snippet = "A precise fictional message body.", internalDate = "1789689600000",
                    payload = new
                    {
                        mimeType = "multipart/alternative",
                        headers = new[] { new { name = "From", value = "sender@example.test" }, new { name = "Subject", value = "Fictional" } },
                        parts = new[] { new { mimeType = "text/plain", body = new { data = body } } }
                    }
                })
            });
        }));

        var result = await GoogleWorkspaceReadApi.CallGmail(http, "fixture-token", GoogleWorkspaceReadApi.GmailGet,
            JsonSerializer.SerializeToElement(new { messageId = "mail-2" }), default);
        Assert.Equal("A precise fictional message body.", result.GetProperty("body").GetString());
        Assert.True(result.GetProperty("readOnly").GetBoolean());
    }

    [Fact]
    public async Task CalendarReadsUseStableApiAndRejectAnUnboundedRange()
    {
        var requests = new List<(HttpMethod Method, string Url, string Body)>();
        using var http = new HttpClient(new Handler(async (request, cancellation) =>
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellation);
            requests.Add((request.Method, request.RequestUri!.AbsoluteUri, body));
            Assert.Equal("Bearer fixture-token", request.Headers.Authorization?.ToString());
            return new(HttpStatusCode.OK)
            {
                Content = request.RequestUri.AbsolutePath.EndsWith("/freeBusy", StringComparison.Ordinal)
                    ? JsonContent.Create(new { calendars = new { } })
                    : JsonContent.Create(new { items = new[] { new { id = "event-1", summary = "Dentist", htmlLink = "https://calendar.google.com/event?eid=fixture" } } })
            };
        }));
        var range = JsonSerializer.SerializeToElement(new
        {
            calendarId = "primary", timeMin = "2026-09-17T00:00:00Z", timeMax = "2026-09-18T00:00:00Z", maxResults = 10
        });
        var events = await GoogleWorkspaceReadApi.CallCalendar(http, "fixture-token", GoogleWorkspaceReadApi.CalendarEvents, range, default);
        Assert.Equal("Dentist", events.GetProperty("items")[0].GetProperty("summary").GetString());
        Assert.Equal(HttpMethod.Get, requests[0].Method);
        Assert.Contains("singleEvents=true", requests[0].Url);

        await GoogleWorkspaceReadApi.CallCalendar(http, "fixture-token", GoogleWorkspaceReadApi.CalendarFreeBusy,
            JsonSerializer.SerializeToElement(new { timeMin = "2026-09-17T00:00:00Z", timeMax = "2026-09-18T00:00:00Z", calendarIds = new[] { "primary" } }), default);
        Assert.Equal(HttpMethod.Post, requests[1].Method);
        Assert.Contains("\"id\":\"primary\"", requests[1].Body);

        await Assert.ThrowsAsync<ArgumentException>(() => GoogleWorkspaceReadApi.CallCalendar(http, "fixture-token", GoogleWorkspaceReadApi.CalendarEvents,
            JsonSerializer.SerializeToElement(new { timeMin = "2026-01-01T00:00:00Z", timeMax = "2028-01-01T00:00:00Z" }), default));
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public void BuiltInToolsRemainEligibleForProviderNeutralWatchesAndBriefs()
    {
        static ConnectedToolDefinition Connected(string connector, string name, McpToolRecord tool) =>
            new(connector, name, tool.RemoteName, tool.ModelName, tool.Description, tool.InputSchema, tool.Effect, "fixture-v1");
        var mail = GoogleWorkspaceReadApi.GmailTools(name => "mcp_mail_" + name.Replace('.', '_'))
            .Single(tool => tool.RemoteName == GoogleWorkspaceReadApi.GmailSearch);
        var calendar = GoogleWorkspaceReadApi.CalendarTools(name => "mcp_calendar_" + name.Replace('.', '_'))
            .Single(tool => tool.RemoteName == GoogleWorkspaceReadApi.CalendarEvents);
        var mailTool = Connected("mail", "Google Gmail — Read mail", mail);
        var calendarTool = Connected("calendar", "Google Calendar", calendar);

        var watch = Assert.Single(InboxWatchConversation.Eligible([mailTool]));
        Assert.Equal("maxResults", watch.LimitField);
        Assert.Equal("since", watch.SinceField);
        var brief = Assert.Single(DelegationBriefConversation.Eligible([mailTool, calendarTool]));
        Assert.Equal("timeMin", brief.CalendarStartField);
        Assert.Equal("timeMax", brief.CalendarEndField);
    }
}
