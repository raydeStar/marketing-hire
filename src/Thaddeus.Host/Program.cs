using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Thaddeus.Core;
using Thaddeus.Infrastructure;
using Thaddeus.Host;

if (args.FirstOrDefault() == "--capture-codex-allowance") { Environment.ExitCode = await CodexAllowanceCapture.Run(args); return; }
if (args.FirstOrDefault() == "--linux-supervise") { Environment.ExitCode = await LinuxWorkerHost.Run(args); return; }
if (args is ["--credential-helper"]) { Environment.ExitCode = await CredentialHelper.Run(); return; }
if (args is ["--choose-application-folder"]) { Environment.ExitCode = NativeApplicationFolderDialog.RunHelper(); return; }
if (args is ["--package-capabilities"]) { Console.WriteLine(Wire.Pack(ApplicationPackage.Capabilities)); return; }
if (args.FirstOrDefault() == "--verify-package") { Environment.ExitCode = await PackageMaintenance.Run(args); return; }
if (args.Length > 0 && args[0] is "--study-backup" or "--study-restore") { Environment.ExitCode = await StudyMaintenance.Run(args); return; }
var reopening = false;
while (true)
{
using var maintenance = new MaintenanceControl();
DesktopLaunch? desktop;
FileStream? launchLease;
try
{
    desktop = DesktopLaunch.Parse(args, AppContext.BaseDirectory);
    if (reopening && desktop != null) desktop = desktop with { NoBrowser = true };
    if (desktop != null && !reopening && await desktop.Reopen(150)) return;
    try { launchLease = desktop?.Acquire(); }
    catch (InvalidOperationException)
    {
        // A second click can arrive while the first process is still starting.
        if (desktop != null && !reopening && await desktop.Reopen(3000)) return;
        throw;
    }
}
catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
{
    DesktopLaunchFailure.Report(args, error.Message);
    Environment.ExitCode = 1; return;
}
using var desktopLease = launchLease;
var builder = desktop == null ? WebApplication.CreateBuilder(args) : WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = desktop.Package });
desktop?.Configure(builder);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 150_000);
var root = builder.Configuration["Thaddeus:Data"] ?? Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".data");
var localOrigin = builder.Configuration["Thaddeus:LocalOrigin"] ?? "http://localhost:5179";
var phoneOrigin = builder.Configuration["Thaddeus:PhoneOrigin"];
var workerPort = builder.Configuration.GetValue<int?>("Thaddeus:WorkerPort");
if (workerPort is < 1024 or > 65535 || workerPort == new Uri(localOrigin).Port || (phoneOrigin != null && workerPort == new Uri(phoneOrigin).Port))
    throw new ArgumentException("WorkerPort must be a separate unprivileged loopback port.");
NetworkBoundary.Origin(localOrigin, true);
if (phoneOrigin != null) NetworkBoundary.Origin(phoneOrigin, false);
var phoneMode = builder.Configuration["Thaddeus:PhoneMode"] ?? "direct";
if (phoneMode is not ("direct" or "tailscale")) throw new ArgumentException("PhoneMode must be direct or tailscale.");
if (phoneMode == "tailscale" && phoneOrigin == null) throw new ArgumentException("Tailscale proxy mode requires the exact phone HTTPS origin.");
CustomerLogin.LoadPrivateSettings(builder.Configuration, root);
var customerLogin = CustomerLogin.Register(builder, phoneOrigin);
var listenUrls = phoneOrigin == null || phoneMode == "tailscale" ? localOrigin : localOrigin + ";" + phoneOrigin;
builder.WebHost.UseUrls(workerPort == null ? listenUrls : listenUrls + ";http://127.0.0.1:" + workerPort);
var googleOAuthOrigin = McpConnections.GoogleRedirect(localOrigin).GetLeftPart(UriPartial.Authority);
var origins = new[] { localOrigin, phoneOrigin, googleOAuthOrigin }.OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddRateLimiter(o => o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartition.GetFixedWindowLimiter((c.Connection.RemoteIpAddress?.ToString() ?? "unknown") + (GuessableSecret(c.Request.Path) ? ":auth" : ":api"), key => new() { PermitLimit = key.EndsWith(":auth", StringComparison.Ordinal) ? 12 : 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
builder.Services.AddSingleton(_ => new Store(root));
builder.Services.AddSingleton<Security>();
builder.Services.AddSingleton<MarketingBackend>();
builder.Services.AddSingleton<CodexAllowanceHistory>();
builder.Services.AddSingleton<CodexAllowanceMonitor>();
builder.Services.AddHostedService(services => services.GetRequiredService<CodexAllowanceMonitor>());
builder.Services.AddHostedService<MarketingRunwayPump>();
builder.Services.AddSingleton<ICompanyMeetingRuntime>(services => services.GetRequiredService<MarketingBackend>());
builder.Services.AddSingleton<OrganizationDirectory>();
builder.Services.AddSingleton<CompanyWiki>();
builder.Services.AddSingleton<EmployeeFiles>();
builder.Services.AddSingleton<PublishedPages>();
builder.Services.AddSingleton<WorkspaceLibrary>();
builder.Services.AddSingleton<Scorecard>();
// Shifts use the scripted stand-in model unless live OpenClaw shifts are explicitly configured.
builder.Services.AddSingleton<IShiftRuntime>(services => builder.Configuration["Marketing:ShiftRuntime"] == "openclaw" ? new OpenClawShiftRuntime(services.GetRequiredService<MarketingBackend>()) : new ScriptedShiftRuntime());
builder.Services.AddSingleton<EmployeeShifts>();
builder.Services.AddHostedService<EmployeeShiftPump>();
builder.Services.AddSingleton<MemberRoles>();
builder.Services.AddSingleton<CompanyMeetings>();
builder.Services.AddSingleton<BrowserLaunchTickets>();
if (desktop != null) { builder.Services.AddSingleton(desktop); builder.Services.AddHostedService<DesktopReopenService>(); }
builder.Services.AddSingleton<ICredentialVault, ProcessCredentialVault>();
builder.Services.AddSingleton(services => new ModelConnections(services.GetRequiredService<Store>(), services.GetRequiredService<ICredentialVault>(),
    builder.Configuration["Thaddeus:ApiKey"], builder.Configuration["Thaddeus:ApiKeyEndpoint"]));
builder.Services.AddSingleton<IProviderCredentials>(services => services.GetRequiredService<ModelConnections>());
builder.Services.AddSingleton(services => new McpConnections(services.GetRequiredService<Store>(), services.GetRequiredService<ICredentialVault>(), localOrigin));
builder.Services.AddSingleton<IConnectedToolBroker>(services => services.GetRequiredService<McpConnections>());
builder.Services.AddSingleton(_ => new WindowsDelegationDispatcher(localOrigin));
builder.Services.AddSingleton<ConnectedEmailDelegationDispatcher>();
builder.Services.AddSingleton<ConnectedBriefDelegationDispatcher>();
builder.Services.AddSingleton<ConnectedInboxWatchDispatcher>();
builder.Services.AddSingleton<IDelegationDispatcher, HostDelegationDispatcher>();
builder.Services.AddSingleton<DelegationScheduler>();
builder.Services.AddHostedService<DelegationPump>();
builder.Services.AddSingleton<IValidator, PlanValidator>();
builder.Services.AddSingleton<IAgentPolicy, EvidencePolicy>();
builder.Services.AddSingleton<Func<ProviderSnapshot, IModelProvider>>(services => p => p.Kind switch
{
    "scripted" => new ScriptedProvider(),
    "compatible" => new CompatibleProvider(p, null, new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) { Timeout = TimeSpan.FromMinutes(10) }, services.GetRequiredService<IProviderCredentials>()),
    _ => throw new ArgumentException("Provider profile is unconfigured.")
});
builder.Services.AddSingleton<IBrowserSession, ManagedBrowserSession>();
builder.Services.AddSingleton<Runtime>();
builder.Services.AddSingleton<IPublicWebReader>(_ => new PublicWebReader());
builder.Services.AddSingleton<IPublicFeedReader, PublicFeedReader>();
builder.Services.AddSingleton<FeedRefresh>();
builder.Services.AddHostedService<FeedPump>();
builder.Services.AddSingleton<SearchConnections>();
builder.Services.AddSingleton<TemporaryPublicSearch>();
builder.Services.AddSingleton<IPublicSearchCredentials>(services => services.GetRequiredService<SearchConnections>());
builder.Services.AddSingleton<IPublicSearch>(services => new BravePublicSearch(services.GetRequiredService<IPublicSearchCredentials>()));
builder.Services.AddSingleton<IModelAccessGate, ModelAccessGate>();
builder.Services.AddSingleton<IInferenceTransport>(services => new CompatibleInference(
    new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) { Timeout = TimeSpan.FromMinutes(10) },
    null, services.GetRequiredService<IProviderCredentials>()));
WorkerMcp.Register(builder.Services);
builder.Services.AddSingleton(services => DevelopmentWorkerSetup.Create(services.GetRequiredService<Store>(),
    builder.Configuration["Thaddeus:DevelopmentWorkerInstallation"], workerPort ?? new Uri(localOrigin).Port));
builder.Services.AddSingleton<IResearchWorkerFactory>(services => services.GetRequiredService<HostWorkerSetup>());
builder.Services.AddSingleton<IResearchWorkspaceStorage, QemuWorkspaceStorage>();
builder.Services.AddSingleton<ResearchCoordinator>();
builder.Services.AddHostedService<ResearchPump>();
builder.Services.AddSingleton<IHostProcessRunner, HostProcessRunner>();
builder.Services.AddSingleton<HostRequirements>();
builder.Services.AddSingleton<ISandboxBackend>(services => new DockerSandboxBackend(
    services.GetRequiredService<IHostProcessRunner>(),
    DockerSandboxBackend.FindExecutable(builder.Configuration["Thaddeus:SandboxExecutable"]),
    services.GetRequiredService<Store>()));
var app = builder.Build();
if (phoneMode == "tailscale") app.UseForwardedHeaders(NetworkBoundary.TailscaleProxy(phoneOrigin!));
var store = app.Services.GetRequiredService<Store>();
var runtime = app.Services.GetRequiredService<Runtime>();
var security = app.Services.GetRequiredService<Security>();
runtime.Recover();
var research = app.Services.GetRequiredService<ResearchCoordinator>();
await research.Initialize();
var keyFile = Path.Combine(store.Root, "host-key.txt");
if (!File.Exists(keyFile)) File.WriteAllText(keyFile, Security.Random());
var hostKeyHash = Wire.Hash(File.ReadAllText(keyFile).Trim());
app.Use(async (c, next) =>
{
    var origin = $"{c.Request.Scheme}://{c.Request.Host}";
    if (c.Request.Headers.ContainsKey("Tailscale-Funnel-Request")) { c.Response.StatusCode = 403; return; }
    c.Response.Headers["X-Content-Type-Options"] = "nosniff";
    c.Response.Headers["Referrer-Policy"] = "no-referrer";
    c.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (c.Request.ContentLength > 150_000) { c.Response.StatusCode = 413; return; }
    using var admitted = c.Request.Path.StartsWithSegments("/api") || WorkerMcp.IsWorkerRequest(c)
        ? maintenance.Admit(c.Request.Method == "GET" && (c.Request.Path == "/api/events" || c.Request.Path == "/api/maintenance")) : maintenance.Admit(observation: true);
    if (admitted == null) { c.Response.StatusCode = 503; await c.Response.WriteAsJsonAsync(new { error = "The study is closing for maintenance. Keep the maintenance page open." }); return; }
    if (WorkerMcp.IsWorkerRequest(c))
    {
        c.Response.Headers.CacheControl = "no-store";
        if (!WorkerMcp.Authenticate(c, app.Services.GetRequiredService<WorkerAuthorization>(), localOrigin, workerPort)) { c.Response.StatusCode = 403; return; }
        await next(); return;
    }
    if (!origins.Contains(origin) || (workerPort != null && c.Connection.LocalPort == workerPort)) { c.Response.StatusCode = 403; return; }
    // The app shell must revalidate so an upgraded host never serves a stale index; hashed assets may cache.
    if (!c.Request.Path.StartsWithSegments("/api")) { if (!c.Request.Path.StartsWithSegments("/assets")) c.Response.Headers.CacheControl = "no-cache"; await next(); return; }
    c.Response.Headers.CacheControl = "no-store";
    var oauthCallback = HttpMethods.IsGet(c.Request.Method) && c.Request.Path == "/api/settings/mcp/google/callback";
    if (c.Request.Headers.TryGetValue("Origin", out var given) && given != origin) { c.Response.StatusCode = 403; return; }
    if (c.Request.Headers["Sec-Fetch-Site"] == "cross-site" && !oauthCallback) { c.Response.StatusCode = 403; return; }
    var mutation = c.Request.Method is not ("GET" or "HEAD");
    var fileUpload = HttpMethods.IsPost(c.Request.Method) && c.Request.Path == "/api/uploads" && c.Request.HasFormContentType;
    if (mutation && (c.Request.Headers["Origin"] != origin || !(c.Request.HasJsonContentType() || fileUpload))) { c.Response.StatusCode = 403; return; }
    var anonymous = oauthCallback || c.Request.Path == "/api/auth/customer" || c.Request.Path == "/api/auth/customer/login" || c.Request.Path == "/api/auth/login" || c.Request.Path == "/api/auth/launch" || c.Request.Path == "/api/auth/claim-launch" || c.Request.Path == "/api/pair/claim" || c.Request.Path == "/api/pair/exchange";
    var session = security.Authenticate(c);
    if (!anonymous && session == null) { c.Response.StatusCode = 401; return; }
    if (!anonymous && mutation && c.Request.Headers["X-CSRF"] != session!.Csrf) { c.Response.StatusCode = 403; return; }
    if (session is { Owner: false } && app.Services.GetRequiredService<MemberRoles>().Explicit(session.PrincipalId) is { } memberRole)
    {
        // A teammate with an owner-assigned role reaches only that role's routes; endpoints then check capabilities.
        c.Items["role"] = memberRole;
        if (!MemberRoles.Reaches(memberRole, c.Request.Method, c.Request.Path.Value ?? "")) { c.Response.StatusCode = 403; return; }
    }
    else if (session is { Owner: false })
    {
        // A campaign collaborator keeps this restricted scope after access is revoked.
        // Older paired devices with no campaign role retain their established app routes.
        var campaignScoped = session.CampaignOnly ||
            app.Services.GetRequiredService<MarketingBackend>().HasEverCampaignMembership(session.PrincipalId);
        if (campaignScoped) c.Items["role"] = MemberRole.Reviewer;
        var path = c.Request.Path.Value ?? "";
        var sharedCampaign = path.StartsWith("/api/marketing/campaigns/", StringComparison.Ordinal);
        var nativeShared = path.StartsWith("/api/marketing/runway/", StringComparison.Ordinal) &&
            (path.EndsWith("/shared", StringComparison.Ordinal) ||
             path.EndsWith("/shared/suggestions", StringComparison.Ordinal));
        if (campaignScoped && path is not ("/api/session" or "/api/state" or "/api/marketing/state" or "/api/organization" or "/api/auth/logout" or "/api/auth/customer" or "/api/auth/customer/login") &&
            !sharedCampaign && !nativeShared)
        { c.Response.StatusCode = 403; return; }
    }
    c.Items["session"] = session;
    // A marketing or meeting write makes the shared state snapshot stale for every reader:
    // invalidate before the write runs and again before its response reaches the writer.
    if (mutation && (c.Request.Path.StartsWithSegments("/api/marketing") || c.Request.Path.StartsWithSegments("/api/meetings")))
    {
        var marketingState = app.Services.GetRequiredService<MarketingBackend>();
        marketingState.InvalidateState();
        c.Response.OnStarting(() => { marketingState.InvalidateState(); return Task.CompletedTask; });
    }
    try { await next(); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException)
    {
        if (!c.Response.HasStarted) { c.Response.StatusCode = ex is KeyNotFoundException ? 404 : ex is InvalidOperationException ? 409 : 400; await c.Response.WriteAsJsonAsync(new { error = ex is JsonException ? "Invalid request JSON." : ex.Message }); }
    }
});
app.UseRateLimiter();
if (customerLogin != null) app.UseAuthentication();
CustomerLogin.Map(app, customerLogin);
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapMcp("/worker/{runId}/mcp");
WorkerModels.Map(app);
bool Owner(HttpContext c) => c.Items["session"] is DeviceSession { Owner: true };
// Only endpoints that accept a guessable secret (host key, pairing code) share the strict per-address
// sign-in budget. Identity-provider sign-in and one-time 192-bit launch tickets use the ordinary budget,
// so many people behind one venue address can still sign in.
// Company records kept as host ledgers travel with the backup unchanged.
static JsonElement? ExportLedger(Store store, string key) => store.Setting(key) is { } json ? JsonDocument.Parse(json).RootElement.Clone() : null;
static bool GuessableSecret(PathString path) => path == "/api/auth/login" || path == "/api/auth/launch" || path.StartsWithSegments("/api/pair");
bool Local(HttpContext c) => NetworkBoundary.IsLocalOwnerOrigin(c, localOrigin);
app.MapGet("/api/maintenance", (HttpContext c) => !Owner(c) || !Local(c) ? Results.StatusCode(403) : Results.Ok(maintenance.View(store)));
app.MapPost("/api/maintenance/start", async (HttpContext c, MaintenanceRequest request) =>
{
    if (!Owner(c) || !Local(c)) { c.Response.StatusCode = 403; return; }
    // The Windows shortcut starts with explicit settings; it still belongs to the published estate.
    var restoredLaunch = desktop ?? (workerPort is { } port && File.Exists(Path.Combine(builder.Environment.ContentRootPath, "package-manifest.json"))
        ? new DesktopLaunch(builder.Environment.ContentRootPath, store.Root, localOrigin, port,
            builder.Configuration["Thaddeus:DevelopmentWorkerInstallation"], true) : null);
    var plan = maintenance.Prepare(store, (DeviceSession)c.Items["session"]!, request, localOrigin, builder.Environment.ContentRootPath, restoredLaunch);
    try { await c.Response.WriteAsJsonAsync(MaintenanceControl.ClosingView(plan)); await c.Response.CompleteAsync(); }
    finally { maintenance.CloseStreams(); app.Lifetime.StopApplication(); }
});
app.MapPost("/api/auth/login", (HttpContext c, LoginRequest r) =>
{
    if (!Local(c) || Wire.Hash(r.Key) != hostKeyHash) return Results.Unauthorized();
    var s = security.Issue(c, "Host browser", true); return Results.Ok(new { s.Id, s.Csrf, s.Owner, s.Name });
});
app.MapGet("/api/session", (HttpContext c) => { var s = (DeviceSession)c.Items["session"]!; return Results.Ok(new { s.Id, s.Csrf, s.Owner, s.Name, s.AccountId, s.PrincipalId }); });
app.MapGet("/api/state", (HttpContext c, SearchConnections search) =>
    c.Items["session"] is DeviceSession { Owner: false } && Access.Can(c, Capability.ReadWorkspace)
    // Contributors and managers see the workspace's pages and media, never the owner's private study.
    ? Results.Ok(new { runs = Array.Empty<object>(), pages = Array.Empty<object>(), chats = Array.Empty<object>(),
        memories = Array.Empty<object>(), library = Array.Empty<object>(), uploads = store.Uploads(),
        artifacts = store.ArtifactSummaries(), provider = new { kind = "none" }, writes = "off" })
    : c.Items["session"] is DeviceSession { Owner: false } scoped &&
    (Access.Role(c) is not null || scoped.CampaignOnly || app.Services.GetRequiredService<MarketingBackend>().HasEverCampaignMembership(scoped.Id))
    ? Results.Ok(new { runs = Array.Empty<object>(), pages = Array.Empty<object>(), chats = Array.Empty<object>(),
        memories = Array.Empty<object>(), library = Array.Empty<object>(), uploads = Array.Empty<object>(),
        artifacts = Array.Empty<object>(), provider = new { kind = "none" }, writes = "off" })
    : Results.Ok(new { runs = store.List(), pages = store.Pages(), chats = store.Chats(), memories = store.Memories(), library = store.Library(), uploads = store.Uploads(), artifacts = store.ArtifactSummaries(), myPage = store.MyPage(), browserAvailable = runtime.BrowserAvailable, feeds = store.Feeds(), delegations = store.DelegationJobs(), delegationOccurrences = store.DelegationOccurrences(), provider = Wire.Unpack<ProviderSnapshot>(store.Setting("provider") ?? Wire.Pack(new ProviderSnapshot())), writes = store.Setting("writes") ?? "ask", approvalRules = runtime.ApprovalRules(), phoneOrigin, hostMustRemainAwake = true, research = research.Availability, search = search.Summary, retainedResearchWorkspaces = research.HasRetainedWork }));
app.MapPost("/api/demo/seed", () =>
{
    var fixtures = Path.Combine(app.Environment.ContentRootPath, "fixtures", "notes");
    if (!Directory.Exists(fixtures)) fixtures = Path.Combine(app.Environment.ContentRootPath, "..", "..", "fixtures", "notes");
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
app.MapPost("/api/runs/{id}/approve", async (string id, DecisionRequest r, HttpContext c) =>
{
    if (r.Remember != null && !Owner(c)) return Results.StatusCode(403);
    if (r.Remember != null && store.Get(id)?.Research != null)
        throw new ArgumentException("Research approvals are exact one-time reviews and cannot be remembered.");
    var decided = store.Get(id)?.Research != null
        ? await research.Decide(id, r.ApprovalId, r.Digest, r.Allow, c.RequestAborted)
        : await runtime.Decide(id, r.ApprovalId, r.Digest, r.Allow, r.Remember);
    return Results.Ok(decided);
});
app.MapPost("/api/runs/{id}/cancel", async (string id) => { if (store.Get(id)?.Research != null) await research.Cancel(id); else await runtime.Cancel(id); return Results.Ok(); });
app.MapPost("/api/delegations/{id}/cancel", (HttpContext c, string id, DelegationVersionRequest request) =>
    !Owner(c) ? Results.StatusCode(403) : Results.Ok(store.CancelDelegation(id, request.Version, DateTimeOffset.UtcNow)));
app.MapPost("/api/delegations/{id}/pause", (HttpContext c, string id, DelegationVersionRequest request) =>
    !Owner(c) ? Results.StatusCode(403) : Results.Ok(store.PauseBrief(id, request.Version, DateTimeOffset.UtcNow)));
app.MapPost("/api/delegations/{id}/resume", (HttpContext c, string id, DelegationVersionRequest request) =>
    !Owner(c) ? Results.StatusCode(403) : Results.Ok(store.ResumeBrief(id, request.Version, DateTimeOffset.UtcNow)));
app.MapPost("/api/delegations/{id}/inbox-instruction", (HttpContext c, string id, InboxWatchInstructionRequest request) =>
    !Owner(c) ? Results.StatusCode(403) : Results.Ok(store.EditInboxWatchInstruction(id, request.Version, request.Instruction, DateTimeOffset.UtcNow)));
app.MapPost("/api/delegation-occurrences/{id}/read", (HttpContext c, string id, DelegationVersionRequest request) =>
    !Owner(c) ? Results.StatusCode(403) : Results.Ok(store.ReadDelegationOccurrence(id, request.Version, DateTimeOffset.UtcNow)));
app.MapPost("/api/runs/{id}/guidance", async (string id, ExecutionGuidance r, HttpContext c) => Results.Ok(await research.Steer(id, r, c.RequestAborted)));
app.MapPost("/api/runs/{id}/recovery/inspect", async (string id, RecoveryInspectRequest r, HttpContext c) => !Owner(c) ? Results.StatusCode(403) : Results.Ok(await research.InspectRecovery(id, r.Version, c.RequestAborted)));
app.MapPost("/api/runs/{id}/recovery/restore", async (string id, RecoveryRestoreRequest r, HttpContext c) => !Owner(c) ? Results.StatusCode(403) : Results.Ok(await research.RestoreCheckpoint(id, r.Digest, c.RequestAborted)));
app.MapPost("/api/runs/{id}/workspace/inspect", async (string id, HttpContext c) => !Owner(c) ? Results.StatusCode(403) : Results.Ok(await research.InspectWorkspace(id, c.RequestAborted)));
app.MapPost("/api/runs/{id}/workspace/remove", async (string id, WorkspaceRemovalRequest r, HttpContext c) =>
{
    if (!Owner(c)) return Results.StatusCode(403);
    if (r.Confirmation != "REMOVE WORKSPACE") throw new ArgumentException("Type REMOVE WORKSPACE to confirm removal of the private disk, transcript and logs.");
    return Results.Ok(await research.RemoveWorkspace(id, r.Digest, c.RequestAborted));
});
app.MapPost("/api/auth/launch", (HttpContext c, LoginRequest r, BrowserLaunchTickets tickets) =>
    !Local(c) || Wire.Hash(r.Key) != hostKeyHash ? Results.Unauthorized() : Results.Ok(tickets.Issue()));
app.MapPost("/api/auth/claim-launch", (HttpContext c, LaunchClaimRequest r, BrowserLaunchTickets tickets) =>
{
    if (!Local(c) || !tickets.Claim(r.Ticket)) return Results.Json(new { error = "This launch link expired or was already used. Open Thaddeus again, or use the host access key." }, statusCode: 401);
    var s = c.Items["session"] is DeviceSession { Owner: true } current ? current : security.Issue(c, "Host browser", true);
    return Results.Ok(new { s.Id, s.Csrf, s.Owner, s.Name });
});
app.MapPost("/api/runs/{id}/answer", async (string id, AnswerRequest answer, HttpContext c) =>
    Results.Ok(store.Get(id)?.Research != null ? await research.Answer(id, answer.QuestionId, answer.Answer, c.RequestAborted)
        : await runtime.AnswerQuestion(id, answer.QuestionId, answer.Answer, c.RequestAborted)));
app.MapPost("/api/runs/{id}/resume", async (string id, HttpContext c) =>
{
    var run = store.Get(id);
    if (run?.Browser != null) return Results.Ok(await runtime.ControlBrowser(id, "resume"));
    if (run?.Research != null) return Results.Ok(await research.Resume(id, c.RequestAborted));
    if (run?.State != RunState.Paused) throw new InvalidOperationException("Only safe paused work can resume.");
    if (run.Execution != null) throw new InvalidOperationException("Isolated execution is not qualified yet. Your answer is saved; no task was dispatched.");
    _ = Task.Run(() => runtime.Execute(id)); return Results.Ok();
});
app.MapPost("/api/runs/{id}/browser/{command}", async (string id, string command, HttpContext c) =>
    !Owner(c) || !Local(c) ? Results.StatusCode(403) : Results.Ok(await runtime.ControlBrowser(id, command)));
app.MapGet("/api/runs/{id}/replay", (string id, long? after) => store.Events(after ?? 0, id));
app.MapGet("/api/events", async (HttpContext c, long? after) =>
{
    using var streamLifetime = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted, maintenance.Closing);
    var streamToken = streamLifetime.Token;
    c.Response.ContentType = "text/event-stream"; c.Response.Headers["X-Accel-Buffering"] = "no";
    var cursor = long.TryParse(c.Request.Headers["Last-Event-ID"], out var last) ? last : after ?? 0;
    var memoryCursor = store.MemoryCursor();
    var libraryCursor = store.LibraryCursor();
    var uploadCursor = store.UploadCursor();
    var feedRevision = store.FeedRevision();
    var artifactCursor = store.ArtifactCursor();
    var myPageVersion = store.MyPage().Version;
    try
    {
    while (!streamToken.IsCancellationRequested && security.Authenticate(c) != null)
    {
        foreach (var evt in store.Events(cursor)) { await c.Response.WriteAsync($"id: {evt.Cursor}\ndata: {Wire.Pack(evt)}\n\n", streamToken); cursor = evt.Cursor; }
        var memoryChanged = store.MemoryCursor();
        if (memoryChanged != memoryCursor) { await c.Response.WriteAsync("data: {\"type\":\"memory.changed\"}\n\n", streamToken); memoryCursor = memoryChanged; }
        var libraryChanged = store.LibraryCursor();
        var uploadChanged = store.UploadCursor();
        if (libraryChanged != libraryCursor || uploadChanged != uploadCursor) { await c.Response.WriteAsync("data: {\"type\":\"library.changed\"}\n\n", streamToken); libraryCursor = libraryChanged; uploadCursor = uploadChanged; }
        var artifactChanged = store.ArtifactCursor();
        if (artifactChanged != artifactCursor) { await c.Response.WriteAsync("data: {\"type\":\"artifact.changed\"}\n\n", streamToken); artifactCursor = artifactChanged; }
        var myPageChanged = store.MyPage().Version;
        if (myPageChanged != myPageVersion) { await c.Response.WriteAsync("data: {\"type\":\"my-page.changed\"}\n\n", streamToken); myPageVersion = myPageChanged; }
        var feedChanged = store.FeedRevision();
        if (feedChanged != feedRevision) { await c.Response.WriteAsync("data: {\"type\":\"feed.changed\"}\n\n", streamToken); feedRevision = feedChanged; }
        await c.Response.WriteAsync(": heartbeat\n\n", streamToken); await c.Response.Body.FlushAsync(streamToken);
        await Task.Delay(750, streamToken);
    }
    }
    catch (OperationCanceledException) when (streamToken.IsCancellationRequested) { }
});
app.MapGet("/api/my-page", () => store.MyPage());
app.MapPut("/api/my-page", (MyPageEdit edit) => store.EditMyPage(edit));
app.MapPut("/api/library/{id}", (string id, LibraryEdit edit) => store.EditLibrary(id, edit));
FeedEndpoints.Map(app);
MarketingEndpoints.Map(app);
app.MapGet("/api/organization", (OrganizationDirectory directory, HttpContext context) =>
    Access.Can(context, Capability.ReadWorkspace)
        ? Results.Ok(new { directory = (object)directory.Read(), canConfigure = Owner(context) })
        : Results.Ok(new { directory = (object)new { version = 1,
            departments = new[] { new { id = "marketing", name = "Marketing", purpose = "Shared campaign review" } },
            agents = new[] { new { id = "marketing-main", name = "Marketing employee", role = "AI employee",
                departmentId = "marketing", kind = "employee", runtimeKey = "marketing" } },
            updatedAt = DateTimeOffset.UtcNow.ToString("O") }, canConfigure = false }));
app.MapPut("/api/organization", (OrganizationDirectory directory, CompanyDirectoryChange change, HttpContext context) =>
    Owner(context) ? Results.Ok(directory.Update(change)) : Results.StatusCode(403));
app.MapGet("/api/company-wiki", (CompanyWiki wiki, HttpContext context) =>
    Access.Can(context, Capability.ReadWorkspace) ? Results.Ok(wiki.List()) : Results.StatusCode(403));
app.MapGet("/api/company-wiki/{id}/history", (CompanyWiki wiki, string id, HttpContext context) =>
    Access.Can(context, Capability.ReadWorkspace) ? Results.Ok(wiki.History(id)) : Results.StatusCode(403));
app.MapPut("/api/company-wiki", (CompanyWiki wiki, WikiChange change, HttpContext context) =>
    Access.Can(context, Capability.EditWiki) ? Results.Ok(wiki.Save(change, Access.Actor(context))) : Results.StatusCode(403));
app.MapGet("/api/organization/agents/{agentId}/files", (EmployeeFiles files, string agentId, HttpContext context) =>
    Access.Can(context, Capability.ReadWorkspace) ? Results.Ok(files.List(agentId)) : Results.StatusCode(403));
app.MapGet("/api/organization/agents/{agentId}/files/{name}/history", (EmployeeFiles files, string agentId, string name, HttpContext context) =>
    Access.Can(context, Capability.ReadWorkspace) ? Results.Ok(files.History(agentId, name)) : Results.StatusCode(403));
app.MapPut("/api/organization/agents/{agentId}/files", (EmployeeFiles files, string agentId, EmployeeFileChange change, HttpContext context) =>
    Access.Can(context, Capability.EditTeamFiles) ? Results.Ok(files.Save(agentId, change, Access.Actor(context))) : Results.StatusCode(403));
// Library organization: shared folders and tags for workspace items, plus each reader's own pins.
app.MapGet("/api/workspace-library", (WorkspaceLibrary library, HttpContext context) =>
    Access.Can(context, Capability.ReadWorkspace) ? Results.Ok(library.View(Access.Session(context)!.PrincipalId)) : Results.StatusCode(403));
app.MapPut("/api/workspace-library/entries/{key}", (WorkspaceLibrary library, string key, LibraryEntryChange change, HttpContext context) =>
    Access.Can(context, Capability.EditWiki) ? Results.Ok(library.SaveEntry(key, change, Access.Actor(context), Access.Session(context)!.PrincipalId)) : Results.StatusCode(403));
app.MapPut("/api/workspace-library/folders", (WorkspaceLibrary library, LibraryFoldersChange change, HttpContext context) =>
    Access.Can(context, Capability.EditWiki) ? Results.Ok(library.SaveFolders(change, Access.Actor(context), Access.Session(context)!.PrincipalId)) : Results.StatusCode(403));
app.MapPut("/api/workspace-library/pins", (WorkspaceLibrary library, LibraryPinsChange change, HttpContext context) =>
    Access.Can(context, Capability.ReadWorkspace) ? Results.Ok(library.SavePins(Access.Session(context)!.PrincipalId, change)) : Results.StatusCode(403));
// The scorecard: one primary KPI, leading indicators and experiments, fed by CSV or a published Google Sheet.
app.MapGet("/api/scorecard", (Scorecard scorecard, HttpContext context) =>
    Access.Can(context, Capability.ReadWorkspace) ? Results.Ok(scorecard.View()) : Results.StatusCode(403));
app.MapPost("/api/scorecard/import", async (Scorecard scorecard, ScoreImportRequest request, HttpContext context) =>
{
    if (!Access.Can(context, Capability.WorkOnTasks)) return Results.StatusCode(403);
    string csv;
    try { csv = request.Url is { Length: > 0 } url ? await Scorecard.FetchSheet(url, context.RequestAborted) : request.Csv ?? ""; }
    catch (Exception error) when (error is IOException or HttpRequestException or OperationCanceledException)
    { return Results.Json(new { error = error is OperationCanceledException ? "The sheet took too long to answer." : error.Message }, statusCode: 502); }
    var (_, rows, metrics) = scorecard.Import(request, csv, Access.Actor(context));
    return Results.Ok(new { rows, metrics, scorecard = scorecard.View() });
});
app.MapPut("/api/scorecard/metrics/{key}", (Scorecard scorecard, string key, ScoreMetricChange change, HttpContext context) =>
    Access.Can(context, Capability.WorkOnTasks) ? Results.Ok(new { ledger = scorecard.UpdateMetric(key, change).Version, scorecard = scorecard.View() }) : Results.StatusCode(403));
app.MapPost("/api/scorecard/experiments", (Scorecard scorecard, ScoreExperimentRequest request, HttpContext context) =>
    Access.Can(context, Capability.WorkOnTasks) ? Results.Ok(scorecard.AddExperiment(request, Access.Actor(context))) : Results.StatusCode(403));
app.MapPost("/api/scorecard/experiments/{id}/decision", (Scorecard scorecard, string id, JsonElement body, HttpContext context) =>
{
    if (!Owner(context)) return Results.StatusCode(403);
    var outcome = body.TryGetProperty("outcome", out var value) ? value.GetString() : null;
    if (outcome is not ("scale" or "iterate" or "stop")) return Results.BadRequest(new { error = "Choose scale, iterate or stop." });
    var note = body.TryGetProperty("note", out var text) ? text.GetString() ?? "" : "";
    return Results.Ok(scorecard.Decide(id, outcome, "Owner decision: " + outcome + (note.Length > 0 ? ". " + note : ".")));
});
// Shifts: the employee works the operating loop on its own for 1 to 24 hours. Only the owner starts or stops one.
app.MapGet("/api/shifts", (EmployeeShifts shifts, HttpContext context) =>
    Access.Can(context, Capability.ReadWorkspace) ? Results.Ok(shifts.View()) : Results.StatusCode(403));
app.MapPost("/api/shifts", (EmployeeShifts shifts, ShiftStartRequest request, HttpContext context) =>
    Owner(context) ? Results.Ok(shifts.Start(request, Access.Actor(context))) : Results.StatusCode(403));
app.MapPost("/api/shifts/{id}/{action}", async (EmployeeShifts shifts, string id, string action, HttpContext context) =>
{
    if (!Owner(context)) return Results.StatusCode(403);
    return Results.Ok(action == "cycle" ? await shifts.RunCycle(id, context.RequestAborted) : await shifts.Control(id, action, context.RequestAborted));
});
// Owner-assigned teammate roles. Approvals, team access and backups stay owner-only regardless of role.
app.MapGet("/api/team/roles", (MemberRoles roles, HttpContext context) =>
    Owner(context) ? Results.Ok(roles.List()) : Results.StatusCode(403));
app.MapPut("/api/team/roles/{principalId}", (MemberRoles roles, string principalId, MemberRoleChange change, HttpContext context) =>
{
    if (!Owner(context)) return Results.StatusCode(403);
    var teammate = security.ActiveDevice(principalId) is { Owner: false } || security.Account(principalId) is { Owner: false };
    return teammate ? Results.Ok(roles.Set(principalId, change.Role, Access.Actor(context))) : Results.NotFound();
});
app.MapGet("/api/published-pages", (PublishedPages pages, HttpContext context) =>
    Access.Can(context, Capability.ReadWorkspace) ? Results.Ok(pages.List().Select(page => new { page.Slug, page.ArtifactId, page.ArtifactVersion, page.Title, page.Digest, page.PublishedBy, page.PublishedAt })) : Results.StatusCode(403));
app.MapPost("/api/artifacts/{id}/publish", (PublishedPages pages, string id, PublishRequest request, HttpContext context) =>
    Access.Can(context, Capability.PublishPages) ? Results.Ok(pages.Publish(id, request, Access.Actor(context))) : Results.StatusCode(403));
app.MapPost("/api/published-pages/{slug}/unpublish", (PublishedPages pages, string slug, HttpContext context) =>
    Access.Can(context, Capability.PublishPages) ? (pages.Unpublish(slug) ? Results.Ok() : Results.NotFound()) : Results.StatusCode(403));
// A published page is readable by anyone who can reach this host, rendered from its frozen copy.
app.MapGet("/p/{slug}", (PublishedPages pages, Store store, string slug, HttpContext context) =>
{
    if (pages.Find(slug) is not { } published) return Results.NotFound();
    context.Response.Headers["Content-Security-Policy"] = PublishedPages.Policy;
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
    return Results.Content(PublishedPages.Render(published with { Page = PageMedia.Inline(published.Page, store) }), "text/html; charset=utf-8");
});
ArtifactAppEndpoints.Map(app);
UploadEndpoints.Map(app);
TemporarySearchEndpoints.Map(app);
app.MapGet("/api/knowledge", (string path) => store.Page(path) is { } p ? Results.Ok(p) : Results.NotFound());
app.MapGet("/api/revisions", (string path) => store.Revisions(path));
app.MapPut("/api/knowledge", (EditRequest r) => runtime.EditPage(r.Path, r.Content, r.Version));
app.MapPut("/api/memories/{id}", (string id, RememberRequest r) => store.Remember(id, r));
app.MapPost("/api/memories/{id}/forget", (string id, MemoryVersionRequest r) => store.ForgetMemory(id, r.Version));
app.MapGet("/api/runs/{id}/reconciliation", (string id) => runtime.InspectReconciliation(id));
app.MapPost("/api/runs/{id}/reconciliation", async (string id, ReconcileRequest r, HttpContext c) => Results.Ok(store.Get(id)?.Research != null
    ? await research.ReconcileImport(id, r.ObservedVersion, r.Mode, c.RequestAborted) : await runtime.Reconcile(id, r.ObservedVersion, r.Mode)));
app.MapPost("/api/chat", async (ChatRequest r, HttpContext c, McpConnections connections) =>
{
    var provider = Wire.Unpack<ProviderSnapshot>(store.Setting("provider") ?? Wire.Pack(new ProviderSnapshot()));
    if (r.Mode == "research" && (r.ArtifactId != null || r.UploadIds is {Length: > 0} || r.SuggestIdeas)) throw new ArgumentException("App context is available in chat, not research.");
    if (r.Mode == "research") return Results.Ok(await research.Submit(new(r.Content, r.ReadScope ?? [], r.Web, r.Budget, r.Memories), provider, c.RequestAborted));
    if (r.Mode != "chat") throw new ArgumentException("Choose chat or research.");
    if (r.ReadScope is { Length: > 0 } || r.Web != null || r.Memories is { Length: > 0 }) throw new ArgumentException("Scoped research requires research mode.");
    if (r.ArtifactId == null && r.UploadIds is not {Length: > 0} && !r.SuggestIdeas && Runtime.ApprovalSettingsIntent(r.Content))
        return Results.Ok(runtime.PrepareApprovalSettings(r.Content, provider));
    var connectionSetup = r.ArtifactId == null && r.UploadIds is not {Length: > 0} && !r.SuggestIdeas
        ? Runtime.ConnectionSetupIntent(r.Content) : null;
    if (connectionSetup != null) return Results.Ok(runtime.PrepareConnectionSetup(r.Content, provider, connectionSetup));
    if (r.ArtifactId == null && r.UploadIds is not {Length: > 0} && !r.SuggestIdeas)
    {
        var product = Runtime.GoogleCapabilityIntent(r.Content);
        if (product == null && Runtime.ConnectionStatusFollowUpIntent(r.Content)) product = runtime.RecentGoogleConnectionProduct();
        if (product != null)
        {
            var google = connections.GoogleState();
            if (!google.Products.Contains(product, StringComparer.Ordinal))
                return Results.Ok(runtime.PrepareConnectionSetup(r.Content, provider, "google", product, missing: true));
            if (Runtime.ConnectionStatusFollowUpIntent(r.Content))
                return Results.Ok(runtime.PrepareGoogleConnectionStatus(r.Content, provider, google.Accounts, google.Products));
        }
    }
    var run = runtime.Converse(r.Content, provider, r.Budget, r.ArtifactId, r.LocalDate, r.UploadIds, r.SuggestIdeas, r.BrowserBudget);
    _ = Task.Run(() => runtime.Execute(run.Id));
    return Results.Ok(run);
});
app.MapPost("/api/chat/{id}/retry", (string id, ChatRetryRequest request) =>
{
    var provider = Wire.Unpack<ProviderSnapshot>(store.Setting("provider") ?? Wire.Pack(new ProviderSnapshot()));
    var run = runtime.RetryConversation(id, request.OperationId, provider);
    if (run.State == RunState.Queued) _ = Task.Run(() => runtime.Execute(run.Id));
    return Results.Ok(run);
});
app.MapPut("/api/settings/provider", async (HttpContext c, ProviderSnapshot p, ModelConnections connections) =>
{
    if (!Owner(c)) return Results.StatusCode(403);
    await connections.SaveLegacy(p, c.RequestAborted); return Results.Ok(p);
});
app.MapPost("/api/settings/approval-rules/remove", (HttpContext c, ApprovalRuleChange r) =>
    !Owner(c) ? Results.StatusCode(403) : Results.Ok(runtime.RemoveApprovalRule(r.Scope)));
app.MapGet("/api/settings/soul", (HttpContext c) => !Owner(c) ? Results.StatusCode(403) : Results.Ok(new
{
    soul = store.Soul(),
    history = store.SoulHistory(),
    filePath = store.SoulPath
}));
app.MapPut("/api/settings/soul", (HttpContext c, SoulEditRequest edit) =>
{
    if (!Owner(c)) return Results.StatusCode(403);
    var soul = store.UpdateSoul(edit.Content, edit.Version, "settings", Guid.NewGuid().ToString("N"));
    return Results.Ok(new { soul, history = store.SoulHistory(), filePath = store.SoulPath });
});
app.MapGet("/api/settings/identity", (HttpContext c) => !Owner(c) ? Results.StatusCode(403) : Results.Ok(new
{
    identity = store.Identity(),
    history = store.IdentityHistory(),
    filePath = store.IdentityPath
}));
app.MapPut("/api/settings/identity", (HttpContext c, IdentityEditRequest edit) =>
{
    if (!Owner(c)) return Results.StatusCode(403);
    var identity = store.UpdateIdentity(edit.Content, edit.Version, Guid.NewGuid().ToString("N"));
    return Results.Ok(new { identity, history = store.IdentityHistory(), filePath = store.IdentityPath });
});
app.MapGet("/api/settings/user", (HttpContext c) => !Owner(c) ? Results.StatusCode(403) : Results.Ok(new
{
    user = store.User(),
    history = store.UserHistory(),
    filePath = store.UserPath
}));
app.MapPut("/api/settings/user", (HttpContext c, UserEditRequest edit) =>
{
    if (!Owner(c)) return Results.StatusCode(403);
    var user = store.UpdateUser(edit.Content, edit.Version, "settings", Guid.NewGuid().ToString("N"));
    return Results.Ok(new { user, history = store.UserHistory(), filePath = store.UserPath });
});
app.MapGet("/api/settings/connection", async (HttpContext c, ModelConnections connections) => !Owner(c) || !Local(c) ? Results.StatusCode(403) : Results.Ok(await connections.View(c.RequestAborted)));
app.MapGet("/api/settings/search", async (HttpContext c, SearchConnections connections) => !Owner(c) || !Local(c) ? Results.StatusCode(403) : Results.Ok(await connections.View(c.RequestAborted)));
app.MapGet("/api/settings/mcp", async (HttpContext c, McpConnections connections) => !Owner(c) || !Local(c) ? Results.StatusCode(403) : Results.Ok(await connections.View(c.RequestAborted)));
app.MapPost("/api/settings/mcp/google/start", async (HttpContext c, GoogleMcpStart edit, McpConnections connections) =>
{
    if (!Owner(c) || !Local(c)) return Results.StatusCode(403);
    return Results.Ok(await connections.BeginGoogle(edit, c.RequestAborted));
});
app.MapPut("/api/settings/mcp/google/client", async (HttpContext c, GoogleClientImport edit, McpConnections connections) =>
{
    if (!Owner(c) || !Local(c)) return Results.StatusCode(403);
    await connections.ImportGoogleClient(edit, c.RequestAborted); return Results.Ok(await connections.View(c.RequestAborted));
});
app.MapPost("/api/settings/mcp/google/client/remove", async (HttpContext c, McpConnectorChange change, McpConnections connections) =>
{
    if (!Owner(c) || !Local(c)) return Results.StatusCode(403);
    await connections.ForgetGoogleClient(change, c.RequestAborted); return Results.Ok(await connections.View(c.RequestAborted));
});
app.MapGet("/api/settings/mcp/google/status/{id}", (HttpContext c, string id, McpConnections connections) =>
    !Owner(c) || !Local(c) ? Results.StatusCode(403) : Results.Ok(connections.GoogleStatus(id)));
app.MapGet("/api/settings/mcp/google/callback", (HttpContext c, McpConnections connections, string? code, string? state, string? iss, string? error) =>
{
    connections.CompleteGoogle(code, state, iss, error);
    return Results.Content("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Return to Thaddeus</title><style>body{font:16px system-ui;max-width:36rem;margin:12vh auto;padding:2rem;color:#222}h1{font-family:Georgia,serif}p{line-height:1.6}</style></head><body><main><h1>Return to Thaddeus</h1><p>Google returned your sign-in response. Thaddeus is now verifying the permissions you selected and will show the connected account in the open chat window.</p><p>This window may be closed.</p></main></body></html>", "text/html; charset=utf-8");
});
app.MapPut("/api/settings/mcp", async (HttpContext c, McpConnectorEdit edit, McpConnections connections) =>
{
    if (!Owner(c) || !Local(c)) return Results.StatusCode(403);
    await connections.Save(edit, c.RequestAborted); return Results.Ok(await connections.View(c.RequestAborted));
});
app.MapPost("/api/settings/mcp/{id}/refresh", async (HttpContext c, string id, McpConnectorChange change, McpConnections connections) =>
{
    if (!Owner(c) || !Local(c)) return Results.StatusCode(403);
    await connections.Refresh(id, change, c.RequestAborted); return Results.Ok(await connections.View(c.RequestAborted));
});
app.MapPost("/api/settings/mcp/{id}/remove", async (HttpContext c, string id, McpConnectorChange change, McpConnections connections) =>
{
    if (!Owner(c) || !Local(c)) return Results.StatusCode(403);
    await connections.Forget(id, change, c.RequestAborted); return Results.Ok(await connections.View(c.RequestAborted));
});
app.MapPut("/api/settings/search/budget", (HttpContext c, SearchBudgetEdit edit) =>
    !Owner(c) || !Local(c) ? Results.StatusCode(403) : Results.Ok(store.SetSearchBudget(edit)));
app.MapPut("/api/settings/search/usage", async (HttpContext c, SearchUsageEdit edit, SearchConnections connections) =>
{
    if (!Owner(c) || !Local(c)) return Results.StatusCode(403);
    await connections.SetUsage(edit, c.RequestAborted); return Results.Ok(await connections.View(c.RequestAborted));
});
app.MapPut("/api/settings/search", async (HttpContext c, SearchConnectionEdit edit, SearchConnections connections) =>
{
    if (!Owner(c) || !Local(c)) return Results.StatusCode(403);
    await connections.Save(edit, c.RequestAborted); return Results.Ok(await connections.View(c.RequestAborted));
});
app.MapPost("/api/settings/search/check", async (HttpContext c, CredentialRemoval edit, SearchConnections connections) =>
{
    if (!Owner(c) || !Local(c)) return Results.StatusCode(403);
    return Results.Ok(await connections.Check(edit.Version, c.RequestAborted));
});
app.MapPost("/api/settings/search/credentials/{id}/remove", async (HttpContext c, string id, CredentialRemoval removal, SearchConnections connections) =>
{
    if (!Owner(c) || !Local(c)) return Results.StatusCode(403);
    await connections.Forget(id, removal.Version, c.RequestAborted); return Results.Ok(await connections.View(c.RequestAborted));
});
app.MapPut("/api/settings/connection", async (HttpContext c, ConnectionEdit edit, ModelConnections connections) =>
{
    if (!Owner(c) || !Local(c)) return Results.StatusCode(403);
    await connections.Save(edit, c.RequestAborted); return Results.Ok(await connections.View(c.RequestAborted));
});
app.MapPost("/api/settings/connection/credentials/{id}/remove", async (HttpContext c, string id, CredentialRemoval removal, ModelConnections connections) =>
{
    if (!Owner(c) || !Local(c)) return Results.StatusCode(403);
    await connections.Forget(id, removal.Version, c.RequestAborted); return Results.Ok(await connections.View(c.RequestAborted));
});
app.MapGet("/api/settings/diagnostics", (HttpContext c) => Owner(c) ? Results.Ok(ProviderDiagnostics.Describe(store, Wire.Unpack<ProviderSnapshot>(store.Setting("provider") ?? Wire.Pack(new ProviderSnapshot())))) : Results.StatusCode(403));
app.MapGet("/api/settings/worker", (HttpContext c, HostWorkerSetup setup) => Owner(c) ? Results.Ok(setup.View) : Results.StatusCode(403));
app.MapPost("/api/settings/worker/requirements", async (HttpContext c, HostRequirements requirements) =>
{
    if (!Owner(c)) return Results.StatusCode(403);
    var packagedLinux = false;
    if (LinuxWorkerHost.Supported)
    {
        try { _ = LinuxWorkerHost.Supervisor(AppContext.BaseDirectory, Environment.ProcessPath); packagedLinux = true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
    }
    return Results.Ok(await requirements.Check(packagedLinux, c.RequestAborted));
});
app.MapPost("/api/settings/worker/check", async (HttpContext c, HostWorkerSetup setup) => !Owner(c) ? Results.StatusCode(403)
    : Results.Ok(await research.ConfigureWorker(setup.Check, c.RequestAborted)));
app.MapPost("/api/settings/worker/cancel", (HttpContext c, HostWorkerSetup setup, WorkerCheckCancelRequest request) => !Owner(c) ? Results.StatusCode(403)
    : Results.Ok(setup.CancelCheck(request.CheckId)));
app.MapPost("/api/settings/worker", async (HttpContext c, HostWorkerSetup setup, WorkerEnrollmentRequest request) => !Owner(c) ? Results.StatusCode(403)
    : Results.Ok(await research.ConfigureWorker(_ => Task.FromResult(setup.SetEnabled(request.InstallationDigest, request.Enabled)), c.RequestAborted)));
app.MapPost("/api/settings/sandbox/inspect", async (HttpContext c, ISandboxBackend sandbox) =>
{
    if (!Owner(c)) return Results.StatusCode(403);
    var report = await sandbox.Inspect(c.RequestAborted);
    store.Setting("sandbox-inspection", Wire.Pack(report));
    return Results.Ok(report);
});
app.MapGet("/api/settings/sandbox", (HttpContext c) => !Owner(c) ? Results.StatusCode(403) :
    Results.Ok(new { lastInspection = store.Setting("sandbox-inspection") is { } report ? Wire.Unpack<SandboxInspection>(report) : null,
        executionEnabled = false, requiredVersion = DockerSandboxBackend.PinnedVersion }));
app.MapPost("/api/settings/test", async (HttpContext c, IProviderCredentials credentials) =>
{
    if (!Owner(c)) return Results.StatusCode(403);
    var p = Wire.Unpack<ProviderSnapshot>(store.Setting("provider") ?? Wire.Pack(new ProviderSnapshot()));
    if (p.Kind == "scripted") return Results.Ok(new { status = "Scripted provider ready · simulated model behavior", models = Array.Empty<string>() });
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(15) };
    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(CompatibleProvider.Endpoint(p), "models"));
    if (await credentials.Read(p, c.RequestAborted) is { } key) request.Headers.Authorization = new("Bearer", key);
    try
    {
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, c.RequestAborted); response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(256_000, c.RequestAborted);
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(c.RequestAborted));
        if (!data.RootElement.TryGetProperty("data", out var models) || models.ValueKind != JsonValueKind.Array) throw new JsonException();
        var names = models.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("id").GetString()!).Where(id => id.Length is > 0 and <= 200).Take(100).ToArray();
        return Results.Ok(new { status = "Discovery succeeded. Tool behavior remains unverified until a run completes.", models = names });
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException) { return Results.Ok(new { status = "Discovery failed. Check configuration; manual model ID remains available.", models = Array.Empty<string>() }); }
});
app.MapPut("/api/settings/permissions", (HttpContext c, PermissionRequest r) => { if (!Owner(c)) return Results.StatusCode(403); if (r.Writes is not ("off" or "ask")) throw new ArgumentException("Agent writes support Off or Ask only."); store.Setting("writes", r.Writes); return Results.Ok(); });
app.MapGet("/api/devices", (HttpContext c) => Owner(c) ? Results.Ok(new { devices = security.Devices(), pending = security.Pending() }) : Results.StatusCode(403));
app.MapPost("/api/devices/{id}/revoke", (HttpContext c, string id) => { if (!Owner(c)) return Results.StatusCode(403); security.Revoke(id); return Results.Ok(); });
app.MapPost("/api/pair/start", (HttpContext c) => Owner(c) && Local(c) ? Results.Ok(security.StartPair()) : Results.StatusCode(403));
app.MapPost("/api/pair/claim", (HttpContext c, PairRequest r) => phoneOrigin != null && c.Request.IsHttps ? Results.Ok(security.Claim(c, r.Code, r.Name)) : Results.BadRequest(new { error = "Trusted phone HTTPS is not configured." }));
app.MapPost("/api/pair/{id}/confirm", (HttpContext c, string id) => { if (!Owner(c) || !Local(c)) return Results.StatusCode(403); security.Confirm(id); return Results.Ok(); });
app.MapPost("/api/pair/exchange", (HttpContext c) => { var s = security.Exchange(c); return s == null ? Results.Accepted() : Results.Ok(new { s.Id, s.Csrf, s.Owner, s.Name }); });
app.MapGet("/api/export", (HttpContext c) => Owner(c) ? Results.File(System.Text.Encoding.UTF8.GetBytes(Wire.Pack(new { schemaVersion = Store.CurrentSchemaVersion, uploads = store.Uploads().Select(file => new {file, contentBase64 = Convert.ToBase64String(store.UploadContent(file.Id))}), myPage = store.MyPage(), artifacts = store.Artifacts(), artifactRevisions = store.ArtifactRevisions(), databaseSchemaVersion = Store.CurrentSchemaVersion, identity = store.Identity(), identityRevisions = store.IdentityHistory(), soul = store.Soul(), soulRevisions = store.SoulHistory(), user = store.User(), userRevisions = store.UserHistory(), writes = store.WriteOperations(), runs = store.List(), events = store.AllEvents(), pages = store.Pages(), revisions = store.Pages().Select(p => p.Path).Concat(store.WriteOperations().Select(w => w.Page.Path)).Distinct().ToDictionary(path => path, path => store.Revisions(path)), chats = store.Chats(), memories = store.MemoryRecords(), memoryChanges = store.MemoryChanges(), library = store.Library(), libraryChanges = store.LibraryChanges(), feeds = store.Feeds(), delegations = store.DelegationJobs(), delegationGrants = store.DelegationJobs().Select(job => store.DelegationGrant(job.GrantId)), delegationOccurrences = store.DelegationOccurrences(), inboxWatchStates = store.DelegationJobs().Where(job => job.Kind == "inbox-watch").Select(job => store.InboxWatchState(job.Id)), todoBatchOperations = store.TodoBatchOperations(), companyDirectory = ExportLedger(store, "company-directory-v1"), companyWiki = ExportLedger(store, "company-wiki-v1"), employeeFiles = ExportLedger(store, "employee-files-v1"), publishedPages = ExportLedger(store, "published-pages-v1"), workspaceLibrary = ExportLedger(store, "workspace-library-v1"), scorecard = ExportLedger(store, "scorecard-v1"), employeeShifts = ExportLedger(store, "employee-shifts-v1") })), "application/json", "thaddeus-export.json") : Results.StatusCode(403));
app.MapPost("/api/data/delete", async (HttpContext c, DeleteRequest r) => { if (!Owner(c)) return Results.StatusCode(403); if (r.Confirmation != "DELETE MY DATA") throw new ArgumentException("Type DELETE MY DATA to confirm."); await research.DeletePersonalData(c.RequestAborted); return Results.Ok(); });
app.MapFallbackToFile("index.html");
if (desktop != null) app.Lifetime.ApplicationStarted.Register(() => desktop.OpenBrowser(app.Services.GetRequiredService<BrowserLaunchTickets>(), app.Logger));
using var tray = desktop != null && OperatingSystem.IsWindows()
    ? new WindowsTray(() => desktop.OpenBrowser(app.Services.GetRequiredService<BrowserLaunchTickets>(), app.Logger),
        app.Lifetime.StopApplication, app.Logger)
    : null;
if (tray != null)
{
    app.Lifetime.ApplicationStarted.Register(tray.Start);
    app.Lifetime.ApplicationStopping.Register(tray.Dispose);
}
app.Logger.LogInformation("Thaddeus is ready. The host key lives in the private data directory; the raven keeps no secrets in URLs.");
await app.RunAsync();
if (maintenance.Plan is not { } maintenancePlan) break;
desktopLease?.Dispose();
if (!await MaintenanceScreen.Run(maintenancePlan)) break;
reopening = true;
}

public partial class Program;
public record LoginRequest(string Key);
public record WorkerEnrollmentRequest(string InstallationDigest, bool Enabled);
public record WorkerCheckCancelRequest(string CheckId);
public record StartRequest(string Objective, string[] ReadScope, bool DemoFailure = false, Budget? Budget = null);
public record DecisionRequest(string ApprovalId, string Digest, bool Allow, string? Remember = null);
public record ApprovalRuleChange(string Scope);
public record EditRequest(string Path, string Content, string Version);
public record ChatRequest(string Content, string Mode = "chat", string[]? ReadScope = null, PublicWebScope? Web = null, Budget? Budget = null, MemorySelection[]? Memories = null, string? ArtifactId = null, string? LocalDate = null, string[]? UploadIds = null, bool SuggestIdeas = false, Budget? BrowserBudget = null);
public record ChatRetryRequest(string OperationId);
public record PermissionRequest(string Writes);
public record IdentityEditRequest(string Content, string Version);
public record SoulEditRequest(string Content, string Version);
public record UserEditRequest(string Content, string Version);
public record LaunchClaimRequest(string Ticket);
public record PairRequest(string Code, string Name);
public record DeleteRequest(string Confirmation);
public record DelegationVersionRequest(int Version);
public record InboxWatchInstructionRequest(int Version, string Instruction);
public record RecoveryInspectRequest(int Version);
public record RecoveryRestoreRequest(string Digest);
public record ReconcileRequest(string ObservedVersion, string Mode);
public record AnswerRequest(string QuestionId, string Answer);
public record WorkspaceRemovalRequest(string Digest, string Confirmation);
public record MemoryVersionRequest(string Version);
