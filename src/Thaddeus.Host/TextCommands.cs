using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record TextProposal(string Id, string Code, string Type, JsonElement Action, string Summary, DateTimeOffset CreatedAt, string Status, string? Result = null);

/// <summary>The cockpit by text, for an owner who only texts their employee. The employee reads the workspace here and proposes
/// changes; the host words each change itself and runs it only once the owner's own reply confirms it, which the host reads
/// from the owner's chat on Plow. The employee can neither word the question nor answer it. The same services the cockpit's
/// buttons use carry out the change, so every check they make still applies. Reached only from inside the employee's container.</summary>
public sealed class TextCommands(Store store, MarketingBackend marketing, EmployeeShifts shifts, WorkSchedule schedule, WeeklyRhythm weekly,
    Publishing publishing, OwnerTexts texts, Playbooks playbooks, CompanyObjectives objectives, ILogger<TextCommands> logger)
{
    const string Key = "text-proposals-v1";
    public const string By = "Owner (by text)";
    static readonly DeviceSession OwnerByText = new("owner-by-text", "", "", By, true, DateTimeOffset.MaxValue);
    static readonly string[] BriefFields = ["product_summary", "audience", "voice", "goals", "guardrails", "channels", "claims", "examples"];
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
    string? secret;

    /// <summary>The key the employee's hire command presents; a file only processes in the container can read.</summary>
    public string Secret()
    {
        if (secret != null) return secret;
        var file = Path.Combine(store.Root, "agent-key.txt");
        if (!File.Exists(file)) File.WriteAllText(file, Security.Random());
        return secret = File.ReadAllText(file).Trim();
    }

    public static bool IsRequest(HttpContext context) => context.Request.Path.StartsWithSegments("/_agent");

    public async Task Handle(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        // Only the employee's own container: loopback, never through the Plow entrance (which adds its owner header), with the key.
        if (!texts.Enabled || context.Connection.RemoteIpAddress is not { } peer || !System.Net.IPAddress.IsLoopback(peer) || context.Request.Headers.ContainsKey("X-Plow-User")
            || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(context.Request.Headers["X-HireZero-Agent"].ToString()), System.Text.Encoding.UTF8.GetBytes(Secret())))
        { context.Response.StatusCode = 403; return; }
        var path = context.Request.Path.Value!["/_agent".Length..].Trim('/');
        try
        {
            object result = (context.Request.Method, path) switch
            {
                ("GET", "status") => await Status(context.RequestAborted),
                ("GET", var draft) when draft.StartsWith("drafts/", StringComparison.Ordinal) && int.TryParse(draft[7..], out var id) => await DraftText(id, context.RequestAborted),
                ("POST", "propose") => await Propose(await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body, cancellationToken: context.RequestAborted), context.RequestAborted),
                ("POST", "confirm") => await Confirm(Id(await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body, cancellationToken: context.RequestAborted)), context.RequestAborted),
                ("POST", "cancel") => Cancel(Id(await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body, cancellationToken: context.RequestAborted))),
                _ => throw new KeyNotFoundException("Use status, drafts/<id>, propose, confirm or cancel."),
            };
            await context.Response.WriteAsJsonAsync(result);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or JsonException or FormatException or HttpRequestException or TaskCanceledException)
        {
            context.Response.StatusCode = error is KeyNotFoundException ? 404 : error is ArgumentException or JsonException or FormatException ? 400 : 409;
            await context.Response.WriteAsJsonAsync(new { error = error.Message });
        }
    }

    static string Id(JsonElement body) => body.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : throw new ArgumentException("Give the change's id.");
    static string Str(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    static string Cut(string text, int length) { var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)); return flat.Length > length ? flat[..length] + "…" : flat; }

    // ---------- Reading ----------
    public async Task<object> Status(CancellationToken cancellation)
    {
        var work = (await marketing.ShiftHire(null, "snapshot")).Value ?? throw new InvalidOperationException("The work ledger is unavailable.");
        var drafts = work.GetProperty("drafts").EnumerateArray().ToArray();
        object Draft(JsonElement item) => new { id = item.GetProperty("id").GetInt32(), channel = Str(item, "channel"), excerpt = Cut(EmployeeShifts.WithoutImageLine(Str(item, "content")), 160) };
        var ledger = publishing.Ledger();
        var on = shifts.History().LastOrDefault(item => item.Status is "running" or "paused" or "finishing");
        var hours = schedule.Current();
        return new
        {
            cockpit = texts.Link,
            working = on == null ? null : new { kind = on.Requests ? "what you asked for" : "a shift", on.Status, until = on.EndsAt },
            workingHours = hours is { Enabled: true } ? $"{Days(hours.Days)} {hours.Start}–{hours.End} ({hours.TimeZone})" : "off",
            weeklyPlanAndUpdate = weekly.Settings().Enabled,
            tasks = work.GetProperty("tasks").EnumerateArray().Where(task => Str(task, "status") is "ready" or "working" or "needs_you" or "paused").Take(12)
                .Select(task => new { id = Str(task, "id"), title = Str(task, "title"), status = Str(task, "status"), version = task.GetProperty("version").GetInt32(), asks = Str(task, "status") == "needs_you" ? Str(task, "blocker") : "" }),
            draftsWaitingForApproval = drafts.Where(item => Str(item, "status") == "pending").Take(10).Select(Draft),
            approvedNotPosted = drafts.Where(item => Str(item, "status") == "approved" && !ledger.Publications.Any(post => post.DraftId == item.GetProperty("id").GetInt32())).Take(10).Select(Draft),
            scheduled = ledger.Publications.Where(post => post.Status is "scheduled" or "due").TakeLast(10).Select(post => new { draft = post.DraftId, post.Channel, post.Status, at = post.ScheduledFor }),
            connectedChannels = ledger.Connections.Where(item => item.Status == "ready").Select(item => new { item.Kind, item.Account }),
            connectHow = "Connecting an account needs its sign-in, so it's done in the cockpit: send the cockpit link.",
            // A change asked about and not yet answered: when the owner says yes, confirm this one.
            waitingForYes = All().Where(item => item.Status == "waiting" && Clock() - item.CreatedAt < TimeSpan.FromMinutes(30)).Select(item => new { change = item.Id, item.Summary }),
        };
    }

    public async Task<object> DraftText(int id, CancellationToken cancellation)
    {
        var draft = await Draft(id);
        return new { id, channel = Str(draft, "channel"), destination = Str(draft, "destination"), status = Str(draft, "status"), content = EmployeeShifts.WithoutImageLine(Str(draft, "content")) };
    }

    async Task<JsonElement> Draft(int id)
    {
        var found = await marketing.ShiftHire(null, "draft", "get", "--id", id.ToString(CultureInfo.InvariantCulture));
        return found.Value ?? throw new KeyNotFoundException($"There's no draft #{id}.");
    }

    static string Days(int[] days) => days.SequenceEqual([1, 2, 3, 4, 5]) ? "Mon–Fri" : days.SequenceEqual([0, 1, 2, 3, 4, 5, 6]) ? "every day"
        : string.Join(", ", days.Select(day => CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames[day]));

    // ---------- Proposing ----------
    /// <summary>Checks the change and words it; nothing changes yet. The employee sends the returned text to the owner as it is.</summary>
    public async Task<object> Propose(JsonElement action, CancellationToken cancellation)
    {
        var type = Str(action, "type");
        var summary = await Describe(type, action, cancellation);
        var id = Guid.NewGuid().ToString("N")[..12];
        var code = string.Concat(Enumerable.Range(0, 4).Select(_ => "ABCDEFGHJKMNPQRSTUVWXYZ23456789"[RandomNumberGenerator.GetInt32(31)]));
        var proposal = new TextProposal(id, code, type, action.Clone(), summary, Clock(), "waiting");
        lock (store) store.Setting(Key, JsonSerializer.Serialize(All().Where(item => Clock() - item.CreatedAt < TimeSpan.FromDays(2)).TakeLast(49).Append(proposal).ToArray()));
        return new { id, confirmText = $"{summary} Reply YES to confirm. (change {code})", note = "Send confirmText to the owner exactly as it is, then end your turn. When they reply yes, run hire cockpit confirm with this id." };
    }

    TextProposal[] All() => store.Setting(Key) is { } json ? JsonSerializer.Deserialize<TextProposal[]>(json) ?? [] : [];

    static int DraftId(JsonElement action) => action.TryGetProperty("draft", out var value) && value.TryGetInt32(out var id) && id > 0 ? id : throw new ArgumentException("Give the draft's number.");

    async Task<string> Describe(string type, JsonElement action, CancellationToken cancellation)
    {
        switch (type)
        {
            case "approve" or "reject" or "post" or "schedule":
            {
                var id = DraftId(action);
                var draft = await Draft(id);
                var status = Str(draft, "status");
                var what = $"{Str(draft, "channel")} draft #{id} (“{Cut(EmployeeShifts.WithoutImageLine(Str(draft, "content")), 70)}”)";
                if (type is "approve" or "reject" && status != "pending") throw new InvalidOperationException($"Draft #{id} is already {status}.");
                if (type is "post" or "schedule" && status is not ("pending" or "approved")) throw new InvalidOperationException($"Draft #{id} is {status}; it can't be posted.");
                if (type == "approve") return $"Approve {what}. Approving doesn't post it.";
                if (type == "reject") return $"Reject {what}" + (Str(action, "note") is { Length: > 0 } note ? $", telling Chip why: “{Cut(note, 200)}”." : ".");
                var route = publishing.Route(Str(draft, "channel"), Str(draft, "destination"));
                if (type == "post")
                    return (status == "pending" ? "Approve and " : "") + route.Action switch
                    {
                        "schedule" => $"schedule {what} on {Account(route.ConnectionId)} for {When(route.At!.Value)}.",
                        "draft" => $"save {what} as a draft in {Account(route.ConnectionId)}; you send it from there.",
                        _ => $"mark {what} ready for you to post yourself: {route.Why}",
                    };
                var at = At(action);
                var connected = publishing.Ledger().Connections.FirstOrDefault(item => item.Status == "ready" && Publishing.Serves(item.Kind, Str(draft, "channel")));
                return (status == "pending" ? "Approve and " : "") + (connected != null
                    ? $"schedule {what} on {connected.Account} for {When(at)}."
                    : $"text you {what} at {When(at)} to post yourself: no {Str(draft, "channel")} account is connected.");
            }
            case "first_shift":
            {
                var brief = (await marketing.ShiftHire(null, "profile", "get")).Value ?? throw new InvalidOperationException("The brief is unavailable.");
                if (Str(brief, "product_summary").Trim().Length == 0 || Str(brief, "audience").Trim().Length == 0)
                    throw new ArgumentException("First find out what they sell and who buys it, and save it to the brief.");
                return $"Start your first shift: {FirstShiftPlan()}. Chip starts now and texts you what it made.";
            }
            case "shift":
            {
                var minutes = action.TryGetProperty("minutes", out var value) && value.TryGetInt32(out var count) ? count : 0;
                if (minutes is < 15 or > 1440) throw new ArgumentException("A shift is 15 minutes to 24 hours.");
                return $"Start a {(minutes % 60 == 0 ? $"{minutes / 60}-hour" : $"{minutes}-minute")} shift now: Chip finds and does work on its own until it ends.";
            }
            case "stop":
                return shifts.History().LastOrDefault(item => item.Status is "running" or "paused") is { } on
                    ? on.Requests ? "Stop what Chip is working on now; the rest waits." : "Stop the shift that's on now."
                    : throw new InvalidOperationException("Nothing is running.");
            case "hours":
            {
                var (days, start, end, zone) = Hours(action);
                return $"Set working hours to {Days(days)}, {start} to {end} ({zone}): a shift starts on its own each working day.";
            }
            case "hours_off": return "Turn working hours off: no shift starts on its own.";
            case "weekly":
                return action.TryGetProperty("enabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? enabled.GetBoolean() ? "Turn on the Monday plan and the Friday update." : "Turn off the Monday plan and the Friday update."
                    : throw new ArgumentException("Say whether to turn it on or off.");
            case "brief":
            {
                var field = Str(action, "field");
                if (!BriefFields.Contains(field)) throw new ArgumentException("The brief's fields are " + string.Join(", ", BriefFields) + ".");
                if (Str(action, "value").Trim() is not { Length: > 0 and <= 1600 } value) throw new ArgumentException("Give the new wording (up to 1,600 characters).");
                return $"Change your brief's {field.Replace('_', ' ')} to “{Cut(value, 300)}”.";
            }
            default: throw new ArgumentException("Changes by text: approve, reject, post, schedule, first_shift, shift, stop, hours, hours_off, weekly, brief.");
        }
    }

    /// <summary>What the first shift makes, as the cockpit's first-shift card says it: the first win, then the business's usual pieces.</summary>
    string FirstShiftPlan()
    {
        var playbook = playbooks.Current() ?? Playbooks.Find("product")!;
        // The pieces by their names ("your first week of posts"), which read as a list where their longer summaries don't.
        var pieces = shifts.FirstShiftPieces(playbook).Select(piece => piece.Title.Length > 0 ? char.ToLowerInvariant(piece.Title[0]) + piece.Title[1..] : piece.Title).ToArray();
        var first = Playbooks.FirstWinLabel(playbooks.Current()?.Id, !string.IsNullOrWhiteSpace(objectives.Current().Content.OwnSite));
        var list = pieces.Length <= 1 ? string.Join("", pieces) : string.Join(", ", pieces[..^1]) + " and " + pieces[^1];
        return (first.Length > 0 ? char.ToLowerInvariant(first[0]) + first[1..] : first) + (list.Length > 0 ? ", plus " + list : "");
    }

    string Account(string? connection) => publishing.Ledger().Connections.FirstOrDefault(item => item.Id == connection)?.Account ?? "the connected account";
    static string When(DateTimeOffset at) => at.ToString("ddd MMM d, h:mm tt", CultureInfo.InvariantCulture) + (at.Offset == TimeSpan.Zero ? " UTC" : "");
    DateTimeOffset At(JsonElement action)
    {
        if (!DateTimeOffset.TryParse(Str(action, "at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) || !Str(action, "at").Contains('T'))
            throw new ArgumentException("Give the time as ISO 8601 with the owner's offset, like 2026-10-07T09:00:00-06:00.");
        if (at < Clock().AddMinutes(2) || at > Clock().AddDays(60)) throw new ArgumentException("Schedule within the next 60 days.");
        return at;
    }

    (int[] Days, string Start, string End, string Zone) Hours(JsonElement action)
    {
        var days = action.TryGetProperty("days", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(day => day.GetInt32()).Distinct().Order().ToArray() : [1, 2, 3, 4, 5];
        var zone = Str(action, "timeZone") is { Length: > 0 } given ? given : schedule.Current()?.TimeZone is { Length: > 0 } saved && saved != "UTC" ? saved
            : weekly.Settings().TimeZone is { Length: > 0 } weeklyZone && weeklyZone != "UTC" ? weeklyZone : throw new ArgumentException("Ask the owner their time zone and give it (IANA, like America/Denver).");
        var start = Str(action, "start"); var end = Str(action, "end");
        // The schedule's own checks, before anything is saved.
        _ = TimeSpan.ParseExact(start, @"hh\:mm", CultureInfo.InvariantCulture); _ = TimeSpan.ParseExact(end, @"hh\:mm", CultureInfo.InvariantCulture);
        _ = TimeZoneInfo.FindSystemTimeZoneById(zone);
        return (days, start, end, zone);
    }

    // ---------- Confirming ----------
    public async Task<object> Confirm(string id, CancellationToken cancellation)
    {
        var proposal = All().FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException("There's no such change.");
        if (proposal.Status == "done") return new { done = proposal.Result };
        if (proposal.Status != "waiting") throw new InvalidOperationException($"That change was {proposal.Status}.");
        if (Clock() - proposal.CreatedAt > TimeSpan.FromMinutes(30)) { Mark(id, "expired"); throw new InvalidOperationException("That change waited over 30 minutes; propose it again."); }
        if (await texts.Unconfirmed(proposal.Code, cancellation) is { } missing) throw new InvalidOperationException(missing);
        Mark(id, "running");
        try
        {
            var result = await Run(proposal, cancellation);
            Mark(id, "done", result);
            return new { done = result };
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or JsonException or HttpRequestException)
        {
            Mark(id, "failed", error.Message);
            logger.LogWarning("A change by text failed: {Error}", error.Message);
            throw new InvalidOperationException("It didn't go through: " + error.Message);
        }
    }

    object Cancel(string id) { Mark(id, "cancelled"); return new { cancelled = id }; }

    void Mark(string id, string status, string? result = null)
    {
        lock (store) store.Setting(Key, JsonSerializer.Serialize(All().Select(item => item.Id == id ? item with { Status = status, Result = result ?? item.Result } : item).ToArray()));
    }

    async Task<string> Run(TextProposal proposal, CancellationToken cancellation)
    {
        var action = proposal.Action;
        var request = "text:" + proposal.Id;
        switch (proposal.Type)
        {
            case "approve": await Decide(DraftId(action), "approved", request, cancellation); return $"Approved draft #{DraftId(action)}. Nothing was posted.";
            case "reject": await Decide(DraftId(action), "rejected", request, cancellation); return $"Rejected draft #{DraftId(action)}.";
            case "post":
            {
                var id = DraftId(action);
                if (Str(await Draft(id), "status") == "pending") await Decide(id, "approved", request, cancellation);
                var post = await publishing.OneTap(id, new OneTapRequest(request + ":post", Str(await Draft(id), "digest")), By, cancellation);
                return Posted(post);
            }
            case "schedule":
            {
                var id = DraftId(action);
                if (Str(await Draft(id), "status") == "pending") await Decide(id, "approved", request, cancellation);
                var draft = await Draft(id);
                var at = DateTimeOffset.Parse(Str(action, "at"), CultureInfo.InvariantCulture);
                var connected = publishing.Ledger().Connections.FirstOrDefault(item => item.Status == "ready" && Publishing.Serves(item.Kind, Str(draft, "channel")));
                var post = connected != null
                    ? await publishing.Publish(id, new DraftPublishRequest(request + ":schedule", connected.Id, Str(draft, "digest"), at), By, cancellation)
                    : await publishing.Assist(id, new AssistRequest(request + ":remind", Str(draft, "digest"), at), By, cancellation);
                return Posted(post);
            }
            case "first_shift":
            {
                // The cockpit's own first shift: the first win and the usual pieces, queued for the worker, which starts on them now.
                await shifts.PrepareFirstWin(By);
                return $"Your first shift has started: {FirstShiftPlan()}. I'll text you what it made.";
            }
            case "shift":
            {
                var minutes = action.GetProperty("minutes").GetInt32();
                var shift = shifts.Start(new ShiftStartRequest(request, Math.Max(1, (minutes + 59) / 60), null, null, minutes), By);
                return $"The shift is on until {When(shift.EndsAt)}.";
            }
            case "stop":
            {
                var on = shifts.History().LastOrDefault(item => item.Status is "running" or "paused") ?? throw new InvalidOperationException("Nothing is running.");
                await shifts.Control(on.Id, "stop", cancellation);
                return "Stopped.";
            }
            case "hours":
            {
                var (days, start, end, zone) = Hours(action);
                var current = schedule.Current();
                schedule.Save(new ShiftScheduleChange(true, days, start, end, zone, current?.CycleMinutes, current?.TurnBudget, current?.TokenBudget, current?.MonthlyTokens), By);
                return $"Working hours are {Days(days)}, {start} to {end} ({zone}).";
            }
            case "hours_off":
            {
                var current = schedule.Current();
                schedule.Save(new ShiftScheduleChange(false, current?.Days ?? [1, 2, 3, 4, 5], current?.Start ?? "09:00", current?.End ?? "17:00", current?.TimeZone ?? "UTC",
                    current?.CycleMinutes, current?.TurnBudget, current?.TokenBudget, current?.MonthlyTokens), By);
                return "Working hours are off.";
            }
            case "weekly":
            {
                var current = weekly.Settings(); var on = action.GetProperty("enabled").GetBoolean();
                weekly.Save(new WeeklySettingsChange(on, current.TimeZone, current.PlanDay, current.PlanTime, current.UpdateDay, current.UpdateTime, current.EmailDraft));
                return on ? "The Monday plan and Friday update are on." : "The Monday plan and Friday update are off.";
            }
            case "brief":
            {
                var profile = (await marketing.ShiftHire(null, "profile", "get")).Value ?? throw new InvalidOperationException("The brief is unavailable.");
                var change = new Dictionary<string, object> { ["request_id"] = request, ["version"] = profile.GetProperty("version").GetInt32(), [Str(action, "field")] = Str(action, "value").Trim() };
                var saved = await marketing.ShiftHire(JsonSerializer.Serialize(change), "profile", "update", "--input-json", "-");
                if (saved.Error != null) throw new InvalidOperationException(saved.Error);
                return $"Saved to your brief ({Str(action, "field").Replace('_', ' ')}).";
            }
            default: throw new ArgumentException("That kind of change can't be made by text.");
        }
    }

    /// <summary>The owner's decision, recorded through the same path as the cockpit's Approve and Reject buttons.</summary>
    async Task Decide(int id, string decision, string request, CancellationToken cancellation)
    {
        var draft = await Draft(id);
        await marketing.DecideDraft(id, JsonSerializer.SerializeToElement(new { requestId = request + ":" + decision, decision, digest = Str(draft, "digest"), revision = draft.GetProperty("revision").GetInt32() }), OwnerByText, cancellation);
        if (Str(await Draft(id), "status") != decision) throw new InvalidOperationException($"Draft #{id} couldn't be {decision}; it may have changed. Check it in the cockpit.");
    }

    static string Posted(Publication post) => post.Status switch
    {
        "published" => "Posted" + (post.Url is { Length: > 0 } url ? ": " + url : "."),
        "scheduled" when post.ConnectionId.Length == 0 => $"I'll text you the post at {When(post.ScheduledFor!.Value)} so you can put it up yourself.",
        "scheduled" => $"Scheduled for {When(post.ScheduledFor!.Value)}.",
        "due" or "awaiting_link" => post.ScheduledFor is { } at ? $"I'll remind you at {When(at)} to post it." : "Ready for you to post; the text is in the cockpit.",
        _ => $"It's {post.Status.Replace('_', ' ')}" + (post.Error is { Length: > 0 } error ? ": " + error : "."),
    };
}
