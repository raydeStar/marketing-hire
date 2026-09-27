using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record PlaybookTask(string Title, string Next)
{
    /// <summary>What the owner sees on the task: the assignment itself is written for the employee.</summary>
    public string Summary => Playbooks.Summaries.GetValueOrDefault(Title) ?? Next;
}
/// <summary>How to market one kind of business: what the number is, where the work goes, what never to do, what to start with,
/// and what the first shift makes besides the site's biggest fix.</summary>
public record Playbook(string Id, string Name, string Hint, string NorthStar, string[] Channels, string Guidance, string[] Guardrails,
    PlaybookTask[] Starters, PlaybookTask[] FirstShift);
public record PlaybookChoice(string Id);

/// <summary>What kind of business this is: a product, a practice, a community or a local business. The owner picks one at
/// onboarding; every shift and chat turn reads its guidance and guardrails, the Work board offers its starter tasks, the first
/// shift makes its pieces, and a north star is suggested when none is set.</summary>
public sealed class Playbooks(Store store, CompanyObjectives objectives)
{
    const string Key = "playbook-v1";

    static PlaybookTask WeekOfPosts(string channels, string mix) => new("Your first week of posts",
        $"Deliver: five posts for this week as a series, in the order to post them, across {channels}. {mix} " +
        "Guidance: the owner's voice (their Voice page posts); each post opens its own way and ends on one ask; one idea each; only the owner's true stories and proof points; an event's date as the date (Thursday, October 16), not \"Thursday night\", while a standing weekly rule stays as the owner puts it; no placeholder for a fact the brief has; nothing posts until the owner approves it.");
    public const string SnapshotTitle = "One competitor snapshot";
    public static PlaybookTask Competitor(string who) => new(SnapshotTitle,
        $"Deliver: a one-page snapshot of {who}: what they offer, to whom, at what price (from their own pages, cited), how they present themselves, and the one thing we should do about it. " +
        "Guidance: read their site; if the brief names none, choose the closest alternative and say why; say plainly what couldn't be found.");

    public static readonly Playbook[] All =
    [
        new("product", "A product or SaaS", "Software, an app, a physical product", "Trials or sign-ups", ["LinkedIn", "X", "Bluesky", "Blog", "Email"],
            "Market a product: lead with the problem it solves and the proof that it works; teach the buyer's job in posts and articles; be fair in comparisons with alternatives; make launches moments. Trials or sign-ups are the number.",
            ["Claim only what the product does today, and say plainly what isn't ready.", "A number needs a source, or is marked as a goal."],
            [new("Positioning one-pager from our website", "Deliver: a one-page positioning document for the owner, read from our site and the brief: 1) Who it is for. 2) Their problem. 3) What they use instead. 4) Why us. " +
                "5) The proof: each point cited to the site [n] or marked (from the brief). 6) Each assumption marked (assumption) where it is stated. 7) How it serves the north star, then the owner's decision and the date to decide by."),
             new("Our three closest competitors, compared", "Find our three closest alternatives and write a battlecard: what each costs, who it is for, and where we win or lose. Cite their pages."),
             new("Site check: the five fixes that matter", "Check our own site for clarity and SEO problems, and list the five fixes that would matter most, in order."),
             new("A two-week launch campaign", "Plan a two-week campaign: a goal, the channels, and a day-by-day list of the posts and emails to make, starting with what we can ship first."),
             new("A pricing and packaging review", "Deliver: 1) What to charge and how to package it, first. 2) The options in a small table. 3) The prices of the alternatives we compete with, each cited to its page, or marked unknown. 4) A Test section: one change to test first, its measure and how long it runs. Guidance: advice for the owner's decision; nothing is changed or published.")],
            [WeekOfPosts("LinkedIn, X and Bluesky", "Post 1: the buyer's problem. Post 2: a proof point. Post 3: how it works. Post 4: a lesson learned, from the owner's stories. Post 5: the ask for the sign-up."), Competitor("our closest competitor (the first one in Objectives, if named)")]),
        new("practice", "A practice or service", "Coach, therapist, consultant, trainer", "Consults or seminar sign-ups", ["Email", "Facebook", "LinkedIn", "Instagram", "Google Business Profile", "Events"],
            "Market a practitioner: trust comes first. Teach from expertise in short, useful posts; show the person and their credentials; promote seminars and workshops (the date, who it is for, what people leave with); follow up by email; keep the local listing current. Consults and seminar sign-ups are the number.",
            ["No client stories, quotes or details unless the owner confirms written consent; describe patterns, never identifiable people.",
             "No promised outcomes or cures: say what the work involves and who it is for.",
             "Stay within the owner's credentials and professional rules; no diagnosing or individual advice in public.",
             "Testimonials only as the owner provides them, and only where the profession allows them."],
            [new("Seminar promotion kit for the next event", "Deliver: six pieces for the owner's next seminar or workshop, each under its own heading, and nothing else: 1) Registration page copy. " +
                "2) Post 1, three weeks before: opens with what the owner believes (their story \"Something we believe\", in their words), as one useful idea the seminar goes deeper on. 3) Post 2, a week before: the owner's own reason for running it (their story \"How we started\", as they told it). " +
                "4) Post 3, two days before: the last call, who it is for and what they leave with. 5) Reminder email, the day before. 6) Follow-up email for attendees: thanks, and one next step (a free consult or a reply), not the sign-up for the seminar they attended. " +
                "Guidance: each post opens its own way; end with one line, the owner's decision; no plan sections; mark any fact only the owner has (takeaways, a consult link, a missing date or price) as [Owner: …]."),
             new("A welcome and nurture email sequence", "Write a three-email sequence for people who join the list: a welcome with one useful idea, a short piece of expertise, and an invitation to book a consult or the next seminar."),
             new("Google Business Profile: description, services and three posts", "Write the Google Business Profile description (750 characters at most), the list of services, and three short posts the owner can publish there."),
             new("Five short expertise posts", "Five short posts that each teach one useful idea from the owner's field, for the people they help. No client stories."),
             new("Who you help: a one-page positioning", "Who the owner helps, the problem they bring, how the owner works, and why them: one page, from the brief and site, marking assumptions.")],
            [WeekOfPosts("Facebook, LinkedIn and Instagram", "Posts 1 to 3 each teach one useful idea from the owner's field, for the people they help, from the owner's own point of view (their stories, such as \"Something we believe\", as they told them), with no event details and no link, and end on a reply or a save; posts 4 and 5 promote the next seminar or a consult and end on the call to action. No client stories, no promised outcomes."), Competitor("a comparable practitioner or program nearby or in the same niche")]),
        new("community", "A community or group", "Facebook group, Discord, club, meetup", "Active members", ["Facebook group", "Instagram", "Threads", "Email", "Events"],
            "Grow a community: people join for belonging and value. Discussion prompts members want to answer, welcome posts that give newcomers a first step, member spotlights (with permission), events and meetups, clear rules, and invitations members choose to share. Active members (posting or replying each week) is the number, not joins. Facebook group posts are posted by the owner: Meta no longer lets apps post in groups.",
            ["Never share members' posts, names or photos outside the group without their permission.", "No spam: no mass DMs or unwanted invites; invitations are ones members choose to share.", "Follow the group's own rules and the platform's."],
            [new("The group's welcome post and rules", "Write the pinned welcome post (what the group is for, how to introduce yourself, the first thing to do) and five clear, friendly group rules. It is pinned inside the group, so it ends on the first thing to do (say hi), not a link to join."),
             new("A month of weekly discussion prompts", "Four weeks of discussion prompts, three a week, that members in this group would want to answer; each short, specific and easy to reply to."),
             new("A member-invite kit", "What members can share to invite a friend: a short invite message, a one-line description of the group, and a post for their own feed."),
             new("The group's about page", "Write the group's description and about section: who it's for, what happens there, and what members get in their first week."),
             new("A first community event plan", "Plan one online or local event for members: the idea, the date options, the announcement post, two reminders and a follow-up thread.")],
            [WeekOfPosts("the Facebook group (posted by the owner), Instagram and Threads", "Post 1: a welcome for new members, with one first thing to do. Post 2: a discussion prompt. Post 3: a question members can answer in a line. Post 4: a second discussion prompt. Post 5: a weekend thread, ending on an invitation members choose to share."), Competitor("one comparable community: what it does well and what members there respond to")]),
        new("local", "A local business", "Shop, studio, restaurant, trade", "Calls or bookings", ["Google Business Profile", "Facebook", "Instagram", "Email", "Nextdoor"],
            "Market a local business: local search and reviews come first; then offers and the seasons, photos of the place and the people, and being part of the neighbourhood. Calls and bookings are the number.",
            ["Hours, prices and offers exactly as the owner gives them; when things are made isn't when they're sold.", "Never write or ask for fake reviews; reply to real reviews politely, without the customer's details.", "No claims about competitors."],
            [new("Google Business Profile: description, services and a month of posts", "Deliver: 1) The description, under its own heading: 750 characters at most, with no link, phone number or promotion. 2) The services list. 3) Four weekly posts, headed Week 1 to Week 4, each with one ask."),
             new("Replies to your latest reviews", "Draft short, polite replies to the business's latest reviews (the owner pastes them in); thank the good ones, answer the unhappy ones calmly and offer to talk."),
             new("A seasonal offer campaign", "Plan a two-week campaign around the season or a local moment: the offer as the owner sets it, three posts, an email, and a sign for the counter or door."),
             new("Five Instagram posts with photo ideas", "Five Instagram posts, each with the photo to take and a short caption in the owner's voice."),
             new("Local search fixes for the site", "Check the site for what local search needs (name, address and phone the same everywhere, hours, services, a map) and list the fixes that matter most.")],
            [WeekOfPosts("Google Business Profile, Facebook and Instagram", "Post 1: the place and who runs it, from their story \"How we started\", as they told it. Post 2: what comes out when, the morning as the owner describes it. Post 3: what the owner believes (their story \"Something we believe\", in their words), as a line people would pass on. Post 4: the standing offer or pre-order, exactly as the owner set it, as its one ask. Post 5: one proof point from the brief (how or where it is made), told as a small scene."), Competitor("one nearby competitor")]),
    ];

    public static readonly Dictionary<string, string> Summaries = new()
    {
        ["Your first week of posts"] = "Five posts for this week, in your voice, ready for you to approve.",
        ["One competitor snapshot"] = "What a competitor offers, to whom and for how much, from their own pages, and what to do about it.",
        ["Positioning one-pager from our website"] = "Who it's for, their problem, what they use instead, why you, and the proof, on one page.",
        ["Our three closest competitors, compared"] = "A battlecard: what each costs, who it's for, and where you win or lose.",
        ["Site check: the five fixes that matter"] = "The five fixes on your site that would matter most, in order.",
        ["A two-week launch campaign"] = "A goal, the channels, and a day-by-day list of posts and emails.",
        ["A pricing and packaging review"] = "What to charge and how to package it, with one change to test first.",
        ["Seminar promotion kit for the next event"] = "Registration page copy, three posts, a reminder email and a follow-up for attendees.",
        ["A welcome and nurture email sequence"] = "Three emails for new subscribers: a welcome, something useful, and an invitation.",
        ["Google Business Profile: description, services and three posts"] = "Your profile description, services list, and three short posts.",
        ["Five short expertise posts"] = "Five posts that each teach one useful idea from your field.",
        ["Who you help: a one-page positioning"] = "Who you help, the problem they bring, how you work, and why you.",
        ["The group's welcome post and rules"] = "A pinned welcome post and five friendly group rules.",
        ["A month of weekly discussion prompts"] = "Twelve prompts members will want to answer, three a week.",
        ["A member-invite kit"] = "What members can share to invite a friend.",
        ["The group's about page"] = "Your group's description: who it's for, what happens there, what members get.",
        ["A first community event plan"] = "One event: the idea, the announcement, two reminders and a follow-up thread.",
        ["Google Business Profile: description, services and a month of posts"] = "Your profile description, services list, and four weekly posts.",
        ["Replies to your latest reviews"] = "Short, polite replies to your latest reviews (paste them in).",
        ["A seasonal offer campaign"] = "Two weeks around the season: posts, an email, and a sign for the counter.",
        ["Five Instagram posts with photo ideas"] = "Five posts, each with the photo to take.",
        ["Local search fixes for the site"] = "What local search needs on your site, and the fixes that matter most.",
    };

    /// <summary>The first win's piece, as the owner reads it, for each kind of business.</summary>
    public static string FirstWinLabel(string? id, bool hasSite) => id switch
    {
        "community" => "A sharper About section for your group",
        "local" => "A better Google Business Profile description",
        _ when hasSite => "The single biggest fix on your website, with the copy written",
        _ => "The single biggest fix on the page people find you by, with the copy written",
    };

    public static Playbook? Find(string? id) => All.FirstOrDefault(item => item.Id == id);

    /// <summary>The page the first win fixes, for each kind of business: the community run fixed a Google Business Profile a
    /// Facebook group doesn't have, because the one example given was a Google Business Profile.</summary>
    public static string FirstWinPage(string? id) => id switch
    {
        "practice" => "the owner's site, or with none the page people find them by (a directory profile), as the brief or the company facts quote it",
        "community" => "the group's description (its about section), as the brief or the company facts quote it",
        "local" => "the Google Business Profile description, as the brief or the company facts quote it; it holds no link, phone number or promotion (an invitation to visit is fine)",
        _ => "the owner's site, or with none the page people find them by, as the brief or the company facts quote it",
    };
    public Playbook? Current() { lock (store) return store.Setting(Key) is { } id ? Find(Wire.Unpack<string>(id)) : null; }

    /// <summary>Picks the playbook; when no north star is set yet, it suggests the playbook's (named, not yet linked to a metric).</summary>
    public Playbook Choose(PlaybookChoice choice, string author)
    {
        var playbook = Find((choice.Id ?? "").Trim().ToLowerInvariant()) ?? throw new ArgumentException("Choose a product, a practice, a community or a local business.");
        lock (store) store.Setting(Key, Wire.Pack(playbook.Id));
        var current = objectives.Current();
        if (current.Content.NorthStar == null)
            try { objectives.Save(new ObjectivesChange(current.Version, current.Content with { NorthStar = new NorthStar(playbook.NorthStar, null, null, null, null, $"The usual number for {playbook.Name.ToLowerInvariant()}; set a target and link it to a scorecard metric.") }), author); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException) { }
        return playbook;
    }

    /// <summary>The line every shift and chat turn reads: what kind of business, how to market it, and what never to do.</summary>
    public string Guidance() => Current() is not { } playbook ? "" :
        $" Kind of business: {playbook.Name.ToLowerInvariant()} ({playbook.Hint.ToLowerInvariant()}). {playbook.Guidance} Channels that usually matter: {string.Join(", ", playbook.Channels)}. Never: {string.Join(" ", playbook.Guardrails)}";
}
