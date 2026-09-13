using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class BrowserLaunchTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-launch-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    private sealed class TestConnection : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, dispatch) => { context.Connection.RemoteIpAddress = context.Request.Headers.ContainsKey("Test-Remote") ? IPAddress.Parse("192.0.2.5") : IPAddress.Loopback; return dispatch(context); });
            next(app);
        };
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public BrowserLaunchTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestConnection>());
        });
    }
    private HttpClient Client()
    {
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179"); return client;
    }
    private async Task<string> Ticket(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/launch", new { key = File.ReadAllText(Path.Combine(root, "host-key.txt")).Trim() });
        response.EnsureSuccessStatusCode(); Assert.False(response.Headers.Contains("Set-Cookie"));
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString()!;
    }
    [Fact] public void TicketsExpireAndDisappearOnRestart()
    {
        var clock = new Clock(); var tickets = new BrowserLaunchTickets(clock); var ticket = tickets.Issue();
        Assert.False(new BrowserLaunchTickets(clock).Claim(ticket.Ticket));
        clock.Now = ticket.Expires; Assert.False(tickets.Claim(ticket.Ticket));
        Assert.False(tickets.Claim(null)); Assert.False(tickets.Claim("not-a-ticket"));
    }
    [Fact] public async Task ConcurrentClaimsHaveExactlyOneWinner()
    {
        var tickets = new BrowserLaunchTickets(); var ticket = tickets.Issue();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => tickets.Claim(ticket.Ticket))));
        Assert.Single(results, result => result);
    }
    [Fact] public void PendingTicketsAreBoundedAndExpiredCapacityIsReclaimed()
    {
        var clock = new Clock(); var tickets = new BrowserLaunchTickets(clock);
        for (var i = 0; i < 8; i++) tickets.Issue();
        Assert.Throws<InvalidOperationException>(() => tickets.Issue());
        clock.Now = clock.Now.AddMinutes(1); Assert.True(tickets.Claim(tickets.Issue().Ticket));
    }
    [Fact] public async Task ClaimCreatesOneHttpOnlyOwnerSessionWithoutStartingWork()
    {
        using var client = Client(); var ticket = await Ticket(client); var store = factory.Services.GetRequiredService<Store>();
        Assert.Null(store.Setting("sessions"));
        using var response = await client.PostAsJsonAsync("/api/auth/claim-launch", new { ticket }); response.EnsureSuccessStatusCode();
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("owner").GetBoolean());
        var cookie = response.Headers.GetValues("Set-Cookie").Single(); Assert.Contains("httponly", cookie); Assert.Contains("samesite=strict", cookie);
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/claim-launch", new { ticket })).StatusCode);
        Assert.Empty(store.List()); Assert.DoesNotContain(ticket, store.Setting("sessions")!);
    }
    [Theory]
    [InlineData("wrong-key", 401)]
    [InlineData("remote", 401)]
    [InlineData("cross-origin", 403)]
    [InlineData("missing-origin", 403)]
    public async Task LaunchRequiresTheExistingKeyAndExactLocalOrigin(string authority, int status)
    {
        using var client = Client(); var key = File.ReadAllText(Path.Combine(root, "host-key.txt")).Trim();
        if (authority == "wrong-key") key = "invalid";
        if (authority == "remote") client.DefaultRequestHeaders.Add("Test-Remote", "true");
        if (authority is "cross-origin" or "missing-origin") client.DefaultRequestHeaders.Remove("Origin");
        if (authority == "cross-origin") client.DefaultRequestHeaders.Add("Origin", "https://elsewhere.invalid");
        Assert.Equal(status, (int)(await client.PostAsJsonAsync("/api/auth/launch", new { key })).StatusCode);
        Assert.Null(factory.Services.GetRequiredService<Store>().Setting("sessions"));
    }
    [Fact] public async Task AnAlreadyUnlockedOwnerDoesNotAccumulateAnotherSession()
    {
        using var client = Client(); var ticket = await Ticket(client);
        using var first = await client.PostAsJsonAsync("/api/auth/claim-launch", new { ticket }); first.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Cookie", first.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        var store = factory.Services.GetRequiredService<Store>(); var before = store.Setting("sessions");
        ticket = await Ticket(client);
        using var second = await client.PostAsJsonAsync("/api/auth/claim-launch", new { ticket }); second.EnsureSuccessStatusCode();
        Assert.False(second.Headers.Contains("Set-Cookie")); Assert.Equal(before, store.Setting("sessions"));
    }
    [Fact] public async Task RemoteClaimCannotConsumeATicket()
    {
        using var client = Client(); var ticket = await Ticket(client);
        client.DefaultRequestHeaders.Add("Test-Remote", "true");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/claim-launch", new { ticket })).StatusCode);
        client.DefaultRequestHeaders.Remove("Test-Remote");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/claim-launch", new { ticket })).StatusCode);
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        await factory.DisposeAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
