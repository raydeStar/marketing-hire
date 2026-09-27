using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record PlaybookTask(string Title, string Next);
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
        "Guidance: the owner's voice (the Voice page's own posts show how they sound); each post opens its own way and ends on one ask; one idea per post; the owner's true stories and proof points only; write a date as the date (Thursday, October 16), never \"Thursday night\" or \"tomorrow\", since the owner chooses when each goes out; nothing posts until the owner approves it.");
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
             new("A pricing and packaging review", "Review our offer and pricing against the alternatives we compete with (cited): what to charge, how to package it, and one change to test first, as a recommendation for the owner.")],
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
                "Each post opens its own way; end with one line, the owner's decision. Guidance: no plan sections; mark any fact only the owner has (takeaways, a consult link, a missing date or price) as [Owner: …]."),
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
            ["Hours, prices and offers exactly as the owner gives them.", "Never write or ask for fake reviews; reply to real reviews politely, without the customer's details.", "No claims about competitors."],
            [new("Google Business Profile: description, services and a month of posts", "Write the Google Business Profile description (750 characters at most), the services list, and four weekly posts for the month."),
             new("Replies to your latest reviews", "Draft short, polite replies to the business's latest reviews (the owner pastes them in); thank the good ones, answer the unhappy ones calmly and offer to talk."),
             new("A seasonal offer campaign", "Plan a two-week campaign around the season or a local moment: the offer as the owner sets it, three posts, an email, and a sign for the counter or door."),
             new("Five Instagram posts with photo ideas", "Five Instagram posts, each with the photo to take and a short caption in the owner's voice."),
             new("Local search fixes for the site", "Check the site for what local search needs (name, address and phone the same everywhere, hours, services, a map) and list the fixes that matter most.")],
            [WeekOfPosts("Google Business Profile, Facebook and Instagram", "Post 1: the place and the people. Post 2: an offer or the season, exactly as the owner sets it. Post 3: a useful tip. Post 4: what's on in the week ahead, with its dates. Post 5: a thank-you to customers, without anyone's details."), Competitor("one nearby competitor")]),
    ];

    public static Playbook? Find(string? id) => All.FirstOrDefault(item => item.Id == id);
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
