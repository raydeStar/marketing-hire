using Thaddeus.Host;

namespace Thaddeus.Tests;

/// <summary>Each kind of work gets its own A standard, found from what the priority is and says.</summary>
public sealed class QualityStandardsTests
{
    [Theory]
    [InlineData("page", "", "Customer FAQ for hirezero.app. Write an FAQ page draft", "faq")]
    [InlineData("draft", "Blog", "Marketing in 30 minutes a day", "blog")]
    [InlineData("draft", "Email", "Welcome email for new waitlist signups", "email")]
    [InlineData("draft", "LinkedIn", "Three LinkedIn posts for this week", "social")]
    [InlineData("document", "", "Two-week social content calendar", "calendar")]
    [InlineData("document", "", "Competitor snapshot: what changed this month", "competitor")]
    [InlineData("video", "", "60+ second hackathon demo video", "video")]
    [InlineData("page", "", "New copy for the pricing page", "page")]
    [InlineData("document", "", "Market sizing for AI marketing tools", "document")]
    [InlineData("experiment", "", "Propose a test to lift signups", "experiment")]
    [InlineData("document", "", "Beta sign-ups campaign plan: Sep 28–Oct 23", "campaign")]
    // The functions beyond content, and the playbooks' own first steps.
    [InlineData("draft", "Email", "Pitch the founder story to three podcasts that interview solo founders", "pitch")]
    [InlineData("document", "", "A partnership pitch to a complementary tool", "pitch")]
    [InlineData("document", "", "Draft replies to this week's comments in the group", "community")]
    [InlineData("document", "", "The group's welcome post and rules", "community")]
    [InlineData("draft", "Email", "Seminar promotion kit for the next event", "event")]
    [InlineData("document", "", "Google Business Profile: description, services and three posts", "local")]
    [InlineData("document", "", "Replies to your latest reviews", "local")]
    [InlineData("draft", "Email", "A welcome and nurture email sequence", "nurture")]
    [InlineData("draft", "Email", "Segment the list by trial stage and write each segment's next email", "nurture")]
    [InlineData("document", "", "Compare buyer segments", "document")]
    [InlineData("document", "", "A paid social plan for the launch, $500 budget", "paid")]
    [InlineData("document", "", "A pricing and packaging review", "pricing")]
    [InlineData("document", "", "Prepare one concrete offer improvement and review it with the owner", "document")]
    [InlineData("document", "", "Positioning one-pager: our elevator pitch", "document")]
    public void KindComesFromWhatTheWorkIs(string deliverable, string channel, string text, string kind)
    {
        Assert.Equal(kind, QualityStandards.Kind(deliverable, channel, text));
        Assert.Equal(kind == "experiment", QualityStandards.For(kind) is null);
    }

    [Fact] public void TheTasksOwnTitleNamesTheKind()
    {
        // The live week of posts was reviewed as event work: its title and text mention the seminar the posts are for.
        Assert.Equal("social", QualityStandards.Kind("draft", "", "Your first week of posts", "First week of posts: October 16 burnout seminar. Five posts; at most two promote the next seminar."));
        Assert.Equal("event", QualityStandards.Kind("document", "", "Seminar promotion kit for the next event", "Registration page copy, three posts, a reminder email."));
        Assert.Equal("event", QualityStandards.Kind("document", "", "Prepare my first useful win", "Fill the seminar"));   // a title that names nothing
        Assert.Equal("video", QualityStandards.Kind("video", "", "Five posts about it", "A clip"));
    }

    [Fact] public void EachFunctionsMeasurableRulesAreCheckedInCode()
    {
        string[] Unmet(string kind, string assignment, string? cta, params string[] parts) => [.. SpecCheck.ForKind(kind, assignment, parts, cta).Select(result => result.Requirement)];
        var longPitch = "Subject: A founder who marketed a launch in spare hours, on your show\n\n" + string.Join(" ", Enumerable.Repeat("word", 160)) + " https://a.example https://b.example";
        Assert.Equal(["a pitch under 150 words", "a subject of eight words or fewer", "one link at most"], Unmet("pitch", "Pitch a podcast", null, longPitch));
        Assert.Empty(Unmet("pitch", "Pitch a podcast", null, "Subject: A guest for your solo-founder episodes\n\nHi Dana, your episode on pricing was the one I send people. Would a 15-minute call work? https://hirezero.app"));

        Assert.Equal(["a reply under 80 words", "a first message without a link"], Unmet("community", "Draft DMs and replies to new members", null, string.Join(" ", Enumerable.Repeat("thanks", 90)) + "\n\nWelcome! https://x.example"));
        Assert.Empty(Unmet("community", "The group's welcome post and rules", null, string.Join(" ", Enumerable.Repeat("welcome", 200)) + " https://x.example"));

        const string seat = "https://example.com/seminar";
        Assert.Equal(["the event's date (piece 2)", "the event's time", "the sign-up link"], Unmet("event", "Seminar kit", seat, "Join us Thursday, October 16.", "Bring a friend."));
        Assert.Empty(Unmet("event", "Seminar kit", seat, "Thursday, Oct 16 at 6:30 pm, Park Hill library. " + seat, "A reminder: tomorrow, Oct 16, 6:30 pm."));

        Assert.Equal(["a profile description within 750 characters", "a profile description with no link or phone number"],
            Unmet("local", "Google Business Profile", null, "## Description\n" + new string('a', 760) + " call 303-555-0100\n\n## Services\n- Counseling"));
        Assert.Empty(Unmet("local", "Google Business Profile", null, "## Description\nA counseling practice in Park Hill, Denver.\n\n## Services\n- Counseling"));

        Assert.Equal(["a subject under 50 characters (piece 1)", "a Subject line (piece 2)", "when it's sent (piece 2)"],
            Unmet("nurture", "Welcome sequence", null, "Send: right after sign-up\nSubject: Welcome to the list, and here is the first useful idea for you\n\nHi.", "Hello again."));

        Assert.Equal(["Google headlines within 30 characters", "a budget with a figure", "a rule for when to stop or shift money"],
            Unmet("paid", "A Google Ads plan", null, "Google Search.\nHeadline 1: An AI marketing employee that asks you first"));
        Assert.Empty(Unmet("paid", "A Google Ads plan", null, "Google Search. Budget: $600 over four weeks.\nHeadline 1: Marketing that asks first\nPause any ad group if cost per sign-up is above $12 after $100 spent."));

        Assert.Equal(["one change to test first"], Unmet("pricing", "Pricing review", null, "Recommendation: charge $29."));
        Assert.Equal(["how long the test runs"], Unmet("pricing", "Pricing review", null, "Recommendation: charge $29.\n\n## Test first\nOffer annual at $290."));
        Assert.Empty(Unmet("pricing", "Pricing review", null, "Recommendation: charge $29.\n\n## Test first\nOffer annual at $290 for four weeks."));
    }
}
