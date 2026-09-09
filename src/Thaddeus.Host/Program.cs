using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Thaddeus.Core;
using Thaddeus.Infrastructure;
using Thaddeus.Host;

var builder = WebApplication.CreateBuilder(args);
var root = builder.Configuration["Thaddeus:Data"] ?? Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".data");
var localOrigin = builder.Configuration["Thaddeus:LocalOrigin"] ?? "http://localhost:5179";
var phoneOrigin = builder.Configuration["Thaddeus:PhoneOrigin"];
if (!new Uri(localOrigin).IsLoopback) throw new InvalidOperationException("LocalOrigin must be loopback.");
if (phoneOrigin != null && (!Uri.TryCreate(phoneOrigin, UriKind.Absolute, out var phoneUri) || phoneUri.Scheme != "https")) throw new InvalidOperationException("PhoneOrigin requires trusted HTTPS.");
builder.WebHost.UseUrls(phoneOrigin == null ? localOrigin : localOrigin + ";" + phoneOrigin);
var origins = new[] { localOrigin, phoneOrigin }.OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddRateLimiter(o => o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartition.GetFixedWindowLimiter((c.Connection.RemoteIpAddress?.ToString() ?? "unknown") + (c.Request.Path.StartsWithSegments("/api/auth") || c.Request.Path.StartsWithSegments("/api/pair") ? ":auth" : ":api"), key => new() { PermitLimit = key.EndsWith(":auth", StringComparison.Ordinal) ? 12 : 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
builder.Services.AddSingleton(_ => new Store(root));
builder.Services.AddSingleton<Security>();
builder.Services.AddSingleton<IValidator, PlanValidator>();
builder.Services.AddSingleton<IAgentPolicy, EvidencePolicy>();
builder.Services.AddSingleton<Func<ProviderSnapshot, IModelProvider>>(_ => p => p.Kind switch
{
    "scripted" => new ScriptedProvider(),
    "compatible" => new CompatibleProvider(p, builder.Configuration["Thaddeus:ApiKey"], new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) }),
    _ => throw new ArgumentException("Provider profile is unconfigured.")
});
builder.Services.AddSingleton<Runtime>();
var app = builder.Build();
var store = app.Services.GetRequiredService<Store>();
var runtime = app.Services.GetRequiredService<Runtime>();
var security = app.Services.GetRequiredService<Security>();
runtime.Recover();
var keyFile = Path.Combine(store.Root, "host-key.txt");
if (!File.Exists(keyFile)) File.WriteAllText(keyFile, Security.Random());
var hostKeyHash = Wire.Hash(File.ReadAllText(keyFile).Trim());
app.Use(async (c, next) =>
{
    var origin = $"{c.Request.Scheme}://{c.Request.Host}";
    c.Response.Headers["X-Content-Type-Options"] = "nosniff";
    c.Response.Headers["Referrer-Policy"] = "no-referrer";
    c.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (!origins.Contains(origin)) { c.Response.StatusCode = 403; return; }
    if (c.Request.ContentLength > 150_000) { c.Response.StatusCode = 413; return; }
    if (!c.Request.Path.StartsWithSegments("/api")) { await next(); return; }
    c.Response.Headers.CacheControl = "no-store";
    if (c.Request.Headers.TryGetValue("Origin", out var given) && given != origin) { c.Response.StatusCode = 403; return; }
    if (c.Request.Headers["Sec-Fetch-Site"] == "cross-site") { c.Response.StatusCode = 403; return; }
    var mutation = c.Request.Method is not ("GET" or "HEAD");
    if (mutation && (c.Request.Headers["Origin"] != origin || !c.Request.HasJsonContentType())) { c.Response.StatusCode = 403; return; }
    var anonymous = c.Request.Path == "/api/auth/login" || c.Request.Path == "/api/pair/claim" || c.Request.Path == "/api/pair/exchange";
    var session = security.Authenticate(c);
    if (!anonymous && session == null) { c.Response.StatusCode = 401; return; }
    if (!anonymous && mutation && c.Request.Headers["X-CSRF"] != session!.Csrf) { c.Response.StatusCode = 403; return; }
    c.Items["session"] = session;
    try { await next(); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
    {
        if (!c.Response.HasStarted) { c.Response.StatusCode = ex is InvalidOperationException ? 409 : 400; await c.Response.WriteAsJsonAsync(new { error = ex is JsonException ? "Invalid request JSON." : ex.Message }); }
    }
});
app.UseRateLimiter();
app.UseDefaultFiles(); app.UseStaticFiles();
bool Owner(HttpContext c) => c.Items["session"] is DeviceSession { Owner: true };
bool Local(HttpContext c) => c.Connection.RemoteIpAddress != null && IPAddress.IsLoopback(c.Connection.RemoteIpAddress);
app.MapPost("/api/auth/login", (HttpContext c, LoginRequest r) =>
{
    if (!Local(c) || Wire.Hash(r.Key) != hostKeyHash) return Results.Unauthorized();
    var s = security.Issue(c, "Host browser", true); return Results.Ok(new { s.Csrf, s.Owner });
});
app.MapGet("/api/session", (HttpContext c) => { var s = (DeviceSession)c.Items["session"]!; return Results.Ok(new { s.Id, s.Csrf, s.Owner }); });
app.MapGet("/api/state", () => new { runs = store.List(), pages = store.Pages(), chats = store.Chats(), provider = Wire.Unpack<ProviderSnapshot>(store.Setting("provider") ?? Wire.Pack(new ProviderSnapshot())), writes = store.Setting("writes") ?? "ask", phoneOrigin, hostMustRemainAwake = true });
app.MapPost("/api/demo/seed", () =>
{
    var fixtures = Path.Combine(app.Environment.ContentRootPath, "..", "..", "fixtures", "notes");
    foreach (var file in Directory.GetFiles(fixtures, "*.md")) { var path = "notes/" + Path.GetFileName(file); if (store.Version(path) == "absent") store.Write(path, File.ReadAllText(file), "absent"); }
    return Results.Ok(store.Pages());
});
app.MapPost("/api/runs", (StartRequest r) =>
{
    var provider = Wire.Unpack<ProviderSnapshot>(store.Setting("provider") ?? Wire.Pack(new ProviderSnapshot()));
    if (r.DemoFailure && provider.Kind != "scripted") throw new ArgumentException("Fault injection is scripted-demo only.");
    var goal = new Goal(r.Objective, r.ReadScope, "plans/", [new("Exact approved write", "deterministic"), new("Factual accuracy and conflict decision", "user")], r.Budget ?? new(), provider);
    var run = runtime.Create(goal, r.DemoFailure); _ = Task.Run(() => runtime.Execute(run.Id)); return Results.Ok(run);
});
app.MapGet("/api/runs/{id}", (string id) => store.Get(id) is { } r ? Results.Ok(r) : Results.NotFound());
app.MapPost("/api/runs/{id}/approve", async (string id, DecisionRequest r) => Results.Ok(await runtime.Decide(id, r.ApprovalId, r.Digest, r.Allow)));
app.MapPost("/api/runs/{id}/cancel", async (string id) => { await runtime.Cancel(id); return Results.Ok(); });
app.MapPost("/api/runs/{id}/resume", (string id) => { if (store.Get(id)?.State != RunState.Paused) throw new InvalidOperationException("Only safe paused work can resume."); _ = Task.Run(() => runtime.Execute(id)); return Results.Ok(); });
app.MapGet("/api/runs/{id}/replay", (string id, long? after) => store.Events(after ?? 0, id));
app.MapGet("/api/events", async (HttpContext c, long? after) =>
{
    c.Response.ContentType = "text/event-stream"; c.Response.Headers["X-Accel-Buffering"] = "no";
    var cursor = long.TryParse(c.Request.Headers["Last-Event-ID"], out var last) ? last : after ?? 0;
    while (!c.RequestAborted.IsCancellationRequested && security.Authenticate(c) != null)
    {
        foreach (var evt in store.Events(cursor)) { await c.Response.WriteAsync($"id: {evt.Cursor}\ndata: {Wire.Pack(evt)}\n\n", c.RequestAborted); cursor = evt.Cursor; }
        await c.Response.WriteAsync(": heartbeat\n\n", c.RequestAborted); await c.Response.Body.FlushAsync(c.RequestAborted);
        await Task.Delay(750, c.RequestAborted);
    }
});
app.MapGet("/api/knowledge", (string path) => store.Page(path) is { } p ? Results.Ok(p) : Results.NotFound());
app.MapGet("/api/revisions", (string path) => store.Revisions(path));
app.MapPut("/api/knowledge", (EditRequest r) => store.Write(r.Path, r.Content, r.Version));
app.MapPost("/api/chat", (ChatRequest r) =>
{
    if (string.IsNullOrWhiteSpace(r.Content) || r.Content.Length > 4000) throw new ArgumentException("Enter a message up to 4,000 characters.");
    store.Chat(new(Guid.NewGuid().ToString("N"), "user", r.Content, DateTimeOffset.UtcNow));
    var text = r.Content.Trim().ToLowerInvariant() is "hello" or "hi" or "hello!" ? "At your service. A little order, with the mystery left intact. Shall we make a plan from your notes?" : "This first slice can turn selected notes into an approval-ready plan. Use Create a plan below. General live conversation is a later milestone.";
    var reply = new ChatMessage(Guid.NewGuid().ToString("N"), "assistant", text, DateTimeOffset.UtcNow); store.Chat(reply); return reply;
});
app.MapPut("/api/settings/provider", (HttpContext c, ProviderSnapshot p) =>
{
    if (!Owner(c)) return Results.StatusCode(403);
    if (p.Kind == "compatible") CompatibleProvider.Endpoint(p); else if (p.Kind != "scripted") throw new ArgumentException("Choose scripted or compatible. Glimmer is unconfigured.");
    store.Setting("provider", Wire.Pack(p)); return Results.Ok(p);
});
app.MapPost("/api/settings/test", async (HttpContext c) =>
{
    if (!Owner(c)) return Results.StatusCode(403);
    var p = Wire.Unpack<ProviderSnapshot>(store.Setting("provider") ?? Wire.Pack(new ProviderSnapshot()));
    if (p.Kind == "scripted") return Results.Ok(new { status = "Scripted provider ready · simulated model behavior", models = Array.Empty<string>() });
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(CompatibleProvider.Endpoint(p), "models"));
    if (builder.Configuration["Thaddeus:ApiKey"] is { } key) request.Headers.Authorization = new("Bearer", key);
    try
    {
        using var response = await client.SendAsync(request, c.RequestAborted); response.EnsureSuccessStatusCode();
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(c.RequestAborted));
        return Results.Ok(new { status = "Discovery succeeded. Tool behavior remains unverified until a run completes.", models = data.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToArray() });
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException) { return Results.Ok(new { status = "Discovery failed. Check configuration; manual model ID remains available.", models = Array.Empty<string>() }); }
});
app.MapPut("/api/settings/permissions", (HttpContext c, PermissionRequest r) => { if (!Owner(c)) return Results.StatusCode(403); if (r.Writes is not ("off" or "ask")) throw new ArgumentException("Agent writes support Off or Ask only."); store.Setting("writes", r.Writes); return Results.Ok(); });
app.MapGet("/api/devices", (HttpContext c) => Owner(c) ? Results.Ok(new { devices = security.Devices(), pending = security.Pending() }) : Results.StatusCode(403));
app.MapPost("/api/devices/{id}/revoke", (HttpContext c, string id) => { if (!Owner(c)) return Results.StatusCode(403); security.Revoke(id); return Results.Ok(); });
app.MapPost("/api/pair/start", (HttpContext c) => Owner(c) && Local(c) ? Results.Ok(security.StartPair()) : Results.StatusCode(403));
app.MapPost("/api/pair/claim", (HttpContext c, PairRequest r) => phoneOrigin != null && c.Request.IsHttps ? Results.Ok(security.Claim(c, r.Code, r.Name)) : Results.BadRequest(new { error = "Trusted phone HTTPS is not configured." }));
app.MapPost("/api/pair/{id}/confirm", (HttpContext c, string id) => { if (!Owner(c) || !Local(c)) return Results.StatusCode(403); security.Confirm(id); return Results.Ok(); });
app.MapPost("/api/pair/exchange", (HttpContext c) => { var s = security.Exchange(c); return s == null ? Results.Accepted() : Results.Ok(new { s.Csrf, s.Owner }); });
app.MapGet("/api/export", (HttpContext c) => Owner(c) ? Results.File(System.Text.Encoding.UTF8.GetBytes(Wire.Pack(new { schemaVersion = 1, runs = store.List(), events = store.List().SelectMany(r => store.Events(0, r.Id)), pages = store.Pages(), revisions = store.Pages().ToDictionary(p => p.Path, p => store.Revisions(p.Path)), chats = store.Chats() })), "application/json", "thaddeus-export.json") : Results.StatusCode(403));
app.MapPost("/api/data/delete", (HttpContext c, DeleteRequest r) => { if (!Owner(c)) return Results.StatusCode(403); if (r.Confirmation != "DELETE MY DATA") throw new ArgumentException("Type DELETE MY DATA to confirm."); if (store.List().Any(r => r.State is RunState.Running or RunState.Queued)) throw new InvalidOperationException("Cancel active work before deleting data."); store.DeletePersonalData(); return Results.Ok(); });
app.MapFallbackToFile("index.html");
app.Logger.LogInformation("Thaddeus is ready. The host key lives in the private data directory; the raven keeps no secrets in URLs.");
app.Run();

public partial class Program;
public record LoginRequest(string Key);
public record StartRequest(string Objective, string[] ReadScope, bool DemoFailure = false, Budget? Budget = null);
public record DecisionRequest(string ApprovalId, string Digest, bool Allow);
public record EditRequest(string Path, string Content, string Version);
public record ChatRequest(string Content);
public record PermissionRequest(string Writes);
public record PairRequest(string Code, string Name);
public record DeleteRequest(string Confirmation);
