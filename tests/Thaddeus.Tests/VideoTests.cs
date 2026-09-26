using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class VideoTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "video-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    static readonly object Scenes = new[]
    {
        new { text = "Marketing every week. No marketer.", sub = "Sound off? You're fine.", seconds = 3, narration = "Every startup needs marketing every week.", visual = "", look = "dark", shot = "" },
        new { text = "HireZero works shifts.", sub = "Research, drafts, page copy.", seconds = 4, narration = "HireZero works shifts on your marketing.", visual = "The shift stage strip", look = "dark", shot = "https://hirezero.app/" },
        new { text = "Nothing goes out without your yes.", sub = "", seconds = 4, narration = "Nothing goes out until you approve it.", visual = "The approval card", look = "light", shot = "https://rival.test/pricing" },
        new { text = "Try it: hirezero.app", sub = "Open source · MIT", seconds = 3, narration = "Try it at hirezero.app.", visual = "", look = "accent", shot = "" },
    };

    sealed class VideoRuntime : IShiftRuntime
    {
        public string Name => "scripted";
        public bool Live => false;
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            var data = request.Data;
            var reply = request.Stage switch
            {
                "prioritize" => JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "video", taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>(), note = "The clip." }),
                "create" => JsonSerializer.Serialize(new
                {
                    deliverable = "video", title = "HireZero in 14 seconds",
                    // As models often do: the storyboard as a JSON object, not a string.
                    body = new { format = "vertical", channel = "LinkedIn", caption = "Every startup needs marketing every week. HireZero works the shifts and asks before anything goes out.", scenes = Scenes },
                    rationale = "A sound-off clip for launch week."
                }),
                // The review "improves" the script but breaks its JSON: the storyboard as written is kept.
                "review" => JsonSerializer.Serialize(new { scores = new { strategy = 4, customer = 3, distinctive = 4, channel = 4, brand = 4, action = 4, claims = 4, shareable = 3 }, issues = new[] { "The hook could be sharper" }, revised = new { title = "HireZero in 14 seconds", body = "## Script\n\n```json\n{\"scenes\": [broken\n```" } }),
                _ => JsonSerializer.Serialize(new { learnings = Array.Empty<string>(), nextShiftFocus = "", notebook = new { known = Array.Empty<string>(), decided = Array.Empty<string>(), openQuestions = Array.Empty<string>(), worked = Array.Empty<string>(), didNotWork = Array.Empty<string>(), resolved = Array.Empty<string>() } }),
            };
            return Task.FromResult(new ShiftTurnResult(reply, 500));
        }
    }
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }

    static readonly byte[] FakeMp4 = [0, 0, 0, 24, .. Encoding.ASCII.GetBytes("ftypisom"), 0, 0, 2, 0, .. Encoding.ASCII.GetBytes("isomiso2"), .. new byte[512]];

    [Fact] public async Task AShiftMakesAVideoFilesItAndDraftsItsCaptionForApproval()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<IShiftRuntime>(new VideoRuntime()); services.AddSingleton<IStartupFilter, Loopback>(); });
        });
        var shifts = factory.Services.GetRequiredService<EmployeeShifts>();
        var renders = new List<(Storyboard Board, int Clips, string Mark)>();
        var shots = new List<string[]>();
        shifts.RenderVideo = (board, audio, mark, _) => { renders.Add((board, audio.Keys.Count(key => !key.StartsWith("shot:")), mark)); shots.Add([.. audio.Keys.Where(key => key.StartsWith("shot:"))]); return Task.FromResult(FakeMp4); };
        var shot = new List<string>();
        shifts.Screenshot = (page, _) => { shot.Add(page.AbsoluteUri); return Task.FromResult<byte[]?>([137, 80, 78, 71]); };
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        async Task<JsonElement> Send(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body ?? new { }) };
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, path + " → " + (int)response.StatusCode + " " + text);
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        await Send(HttpMethod.Put, "/api/objectives", new { expectedVersion = 0, content = new { objectives = Array.Empty<object>(), competitors = Array.Empty<object>(), currentFocus = "", nonGoals = Array.Empty<string>(), ownSite = "hirezero.app" } });
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-video", title = "30-second vertical video for launch week", status = "ready", priority = "high", next_action = "Make a short vertical video.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-video", hours = 8, turnBudget = 10 });
        var shift = await Send(HttpMethod.Post, "/api/shifts/shift-video/cycle");
        var create = shift.GetProperty("cycles")[0].GetProperty("stages")[2];
        var summary = create.GetProperty("summary").GetString()!;
        Assert.Contains("Rendered a 14-second vertical video", summary);
        Assert.Contains("revision discarded", summary);
        Assert.Contains("drafted its LinkedIn caption", summary);

        // Rendered once, from the storyboard as written, with the owner's site in the corner and no narration yet.
        var (board, clips, mark) = Assert.Single(renders);
        Assert.Equal((4, "vertical", 0, "hirezero.app"), (board.Scenes.Length, board.Format, clips, mark));
        Assert.Equal("accent", board.Scenes[^1].Look);
        // Only the owner's own site is screenshotted; a scene pointing elsewhere is rendered without one.
        Assert.Equal(["https://hirezero.app/"], shot);
        Assert.Equal(["shot:1"], shots[0]);

        // The clip is in Library → Campaigns → Videos; the storyboard document beside it; the caption waits as a draft.
        var store = factory.Services.GetRequiredService<Store>();
        var video = Assert.Single(store.Uploads(), file => file.MediaType == "video/mp4");
        Assert.Equal("hirezero-in-14-seconds.mp4", video.Name);
        var library = factory.Services.GetRequiredService<WorkspaceLibrary>().View("");
        Assert.Contains(library.Entries, entry => entry.Key == "media:" + video.Id && entry.Folder == "Campaigns/Videos");
        var storyboard = Assert.Single(factory.Services.GetRequiredService<CompanyWiki>().List(), page => page.Title == "HireZero in 14 seconds");
        Assert.Contains("| 1 | Marketing every week. No marketer.<br>Sound off? You're fine. | 3 |", storyboard.Body);
        Assert.Contains("\"renderer\": \"cards\"", storyboard.Body);
        Assert.Contains(library.Entries, entry => entry.Key == "wiki:" + storyboard.Id && entry.Folder == "Campaigns/Videos");
        Assert.Equal(board.Scenes, VideoRenderer.Parse(storyboard.Body, "x").Scenes);
        var decision = Assert.Single(shift.GetProperty("decisions").EnumerateArray()).GetString()!;
        Assert.StartsWith("draft:", decision);
        var state = await Send(HttpMethod.Get, "/api/marketing/state");
        var draft = state.GetProperty("drafts").EnumerateArray().Single();
        Assert.Equal(("LinkedIn", "pending"), (draft.GetProperty("channel").GetString(), draft.GetProperty("status").GetString()));
        Assert.StartsWith("Every startup needs marketing every week.", draft.GetProperty("content").GetString());
        Assert.Contains("Post with the video “HireZero in 14 seconds” attached", draft.GetProperty("rationale").GetString());
        var task = state.GetProperty("tasks").EnumerateArray().Single(item => item.GetProperty("title").GetString() == "30-second vertical video for launch week");
        Assert.Equal("needs_you", task.GetProperty("status").GetString());
        Assert.StartsWith("Watch “HireZero in 14 seconds”", task.GetProperty("next_action").GetString());

        // The owner records narration for two scenes, then renders it again: the clips go to the renderer, and a new video is filed.
        var wav = Wav(1.5);
        var clip = store.AddUpload("narration-scene-1.wav", wav);
        var withVoice = storyboard.Body.Replace("\"narration\": \"Every startup needs marketing every week.\",", $"\"narration\": \"Every startup needs marketing every week.\",\n      \"audio\": \"{clip.Id}\",");
        Assert.NotEqual(storyboard.Body, withVoice);
        factory.Services.GetRequiredService<CompanyWiki>().Save(new WikiChange(Guid.NewGuid().ToString("N"), storyboard.Id, storyboard.Version, storyboard.Scope, storyboard.ScopeId, storyboard.Title, withVoice, storyboard.Kind, storyboard.Status), "Owner");
        var again = await Send(HttpMethod.Post, "/api/videos/render", new { page = storyboard.Id });
        Assert.Equal(1, again.GetProperty("narrated").GetInt32());
        Assert.Equal(2, renders.Count);
        Assert.Equal(1, renders[1].Clips);
        Assert.Equal(2, store.Uploads().Count(file => file.MediaType == "video/mp4"));
    }

    [Fact] public void StoryboardsAreCheckedNotTrimmed()
    {
        string Board(object scenes, string format = "vertical") => JsonSerializer.Serialize(new { format, scenes });
        var scene = new { text = "One idea per scene.", seconds = 3 };
        Assert.Contains("at least three", Assert.Throws<InvalidOperationException>(() => VideoRenderer.Parse(Board(new[] { scene, scene }), "t")).Message);
        Assert.Contains("90 characters", Assert.Throws<InvalidOperationException>(() => VideoRenderer.Parse(Board(new[] { scene, scene, new { text = new string('a', 100), seconds = 3 } }), "t")).Message);
        Assert.Contains("up to 10", Assert.Throws<InvalidOperationException>(() => VideoRenderer.Parse(Board(Enumerable.Repeat(scene, 11).ToArray()), "t")).Message);
        Assert.Contains("valid JSON", Assert.Throws<InvalidOperationException>(() => VideoRenderer.Parse("```json\n{nope\n```", "t")).Message);
        // Seconds are kept in range, an unknown format or look falls back, and the document round-trips.
        var board = VideoRenderer.Parse(Board(new object[] { new { text = "A", seconds = 30, look = "neon" }, new { text = "B", seconds = 0.5 }, new { text = "C" } }, "cinema"), "Fallback title");
        Assert.Equal(("vertical", "Fallback title", "dark"), (board.Format, board.Title, board.Scenes[0].Look));
        Assert.Equal([8.0, 2.0, 3.5], board.Scenes.Select(item => item.Seconds));
        Assert.Equal(board.Scenes, VideoRenderer.Parse(VideoRenderer.Document(board), "x").Scenes);
        Assert.Equal("Hire a\nmarketing\nemployee.", VideoRenderer.Wrap("Hire a marketing employee.", 600, 100, 0.56));
        Assert.Equal(1.5, VideoRenderer.WavSeconds(Wav(1.5))!.Value, 2);
        Assert.Null(VideoRenderer.WavSeconds(Encoding.ASCII.GetBytes("not a wave file at all, not even close to one")));
    }

    /// <summary>A real render, when ffmpeg is on this machine (it isn't bundled). Without it there's nothing to check here.</summary>
    [Fact] public async Task RendersAPlayableMp4WithFfmpeg()
    {
        var renderer = new VideoRenderer(new ConfigurationBuilder().Build(), NullLogger<VideoRenderer>.Instance);
        if (!renderer.Available()) return;
        var board = VideoRenderer.Parse(JsonSerializer.Serialize(new { format = "square", scenes = new object[] {
            new { text = "Hire a marketing employee.", sub = "Keep the final say.", seconds = 2 }, new { text = "100% of posts wait for you: {braces} 'quotes' \"too\"", seconds = 2, look = "light" }, new { text = "hirezero.app", seconds = 2, look = "accent" } } }), "Test");
        var clip = Wav(2.6);
        var mp4 = await renderer.Render(board with { Scenes = [board.Scenes[0] with { Audio = new string('a', 32) }, .. board.Scenes[1..]] }, new Dictionary<string, byte[]> { [new string('a', 32)] = clip }, "hirezero.app", CancellationToken.None);
        Assert.Equal("ftyp", Encoding.ASCII.GetString(mp4, 4, 4));
        Assert.InRange(mp4.Length, 10_000, Store.MaxMediaBytes);
        // A scene showing a screenshot of the owner's site, in both layouts.
        var png = Path.Combine(root, "page.png");
        Directory.CreateDirectory(root);
        using (var make = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg", $"-hide_banner -loglevel error -y -f lavfi -i color=c=white:s=1280x800 -frames:v 1 \"{png}\"") { UseShellExecute = false, CreateNoWindow = true })!) make.WaitForExit(20000);
        foreach (var format in new[] { "landscape", "vertical" })
        {
            var shown = board with { Format = format, Scenes = [board.Scenes[0] with { Shot = "https://hirezero.app/" }, .. board.Scenes[1..]] };
            var withShot = await renderer.Render(shown, new Dictionary<string, byte[]> { ["shot:0"] = File.ReadAllBytes(Environment.GetEnvironmentVariable("THADDEUS_VIDEO_SHOT") is { Length: > 0 } real ? real : png) }, "hirezero.app", CancellationToken.None);
            Assert.Equal("ftyp", Encoding.ASCII.GetString(withShot, 4, 4));
            if (Environment.GetEnvironmentVariable("THADDEUS_VIDEO_SAMPLE") is { Length: > 0 } sampleShot) File.WriteAllBytes(sampleShot.Replace(".mp4", $"-{format}.mp4"), withShot);
        }
        if (Environment.GetEnvironmentVariable("THADDEUS_VIDEO_SAMPLE") is { Length: > 0 } sample) File.WriteAllBytes(sample, mp4);   // to look at it
    }

    static byte[] Wav(double seconds)
    {
        const int rate = 16000;
        var samples = (int)(rate * seconds);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 220 / rate) * 3000));
        writer.Flush();
        return stream.ToArray();
    }
}
