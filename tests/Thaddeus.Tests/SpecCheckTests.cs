using Thaddeus.Host;

namespace Thaddeus.Tests;

/// <summary>What an assignment asks for that can be measured is measured on the work, so "eight questions" can't become eleven.</summary>
public sealed class SpecCheckTests
{
    static string Faq(int questions) => "# HireZero FAQ\n\n" + string.Concat(Enumerable.Range(1, questions).Select(n => $"## Question {n}?\n\nAn answer.\n\n"));

    [Fact] public void CountsAreMeasured()
    {
        const string assignment = "Customer FAQ for hirezero.app. Write an FAQ page draft: the eight questions a founder asks before trying HireZero, each answered in two or three sentences.";
        var eleven = Assert.Single(SpecCheck.Check(assignment, Faq(11)));
        Assert.Equal(("8 questions", false, "found 11"), (eleven.Requirement, eleven.Met, eleven.Detail));
        Assert.True(Assert.Single(SpecCheck.Check(assignment, Faq(8))).Met);
        // Bold questions in a list count too.
        Assert.True(Assert.Single(SpecCheck.Check("Five objections with answers.", "- **Is it safe?** Yes.\n- **Does it post?** No.\n- **Cost?** Free.\n- **Local?** Yes.\n- **Open?** Yes.")).Met);
        // A series of posts is judged by its parts; "one" and "a" ask for nothing to count.
        Assert.False(Assert.Single(SpecCheck.Check("Three LinkedIn posts for this week.", "Post one\n---\nPost two", parts: 2)).Met);
        Assert.Empty(SpecCheck.Check("Plan one idea per weekday.", "A plan."));
    }

    [Fact] public void TheOwnersNotesBecomeOneAskEachAndQuotesMustBeInTheWork()
    {
        Assert.Equal(["Say who it's for in the headline.", "Exactly eight questions.", "End on the beta link."],
            SpecCheck.OwnerAsks("Three fixes. 1) Say who it's for in the headline. 2) Exactly eight questions. 3) End on the beta link."));
        Assert.Equal(["Shorter, please.", "Lead with the customer story."], SpecCheck.OwnerAsks("Shorter, please. Lead with the customer story."));
        Assert.Empty(SpecCheck.OwnerAsks("ok"));
        const string body = "## Can I use it today?\n\n**Yes.** HireZero runs on your own computer today.";
        Assert.True(SpecCheck.Quotes(body, "Yes. HireZero runs on your own computer today"));
        Assert.False(SpecCheck.Quotes(body, "HireZero is hosted for you"));
        Assert.False(SpecCheck.Quotes(body, "Yes."));   // too short to prove anything
    }

    [Fact] public void ListNumbersCountInOrderAndMeasurableAsksAreKnown()
    {
        // The live note that was cut: "23." is part of the first note, not note 23.
        Assert.Equal(["Stay under 280 characters; a link counts as 23.", "Open with the receipt.", "One link only: the blog post."],
            SpecCheck.OwnerAsks("1) Stay under 280 characters; a link counts as 23. 2) Open with the receipt. 3) One link only: the blog post."));
        Assert.Equal("characters", SpecCheck.Dimension("Stay under 280 characters; a link counts as 23."));
        Assert.Equal("link", SpecCheck.Dimension("One link only: the blog post."));
        Assert.Null(SpecCheck.Dimension("Open with the receipt."));
        // A "60+ second" video is measured by its scenes' running time.
        Assert.Equal(("at least 60 seconds", false, "33 seconds"), SpecCheck.Duration("Prepare the 60+ second hackathon demo video.", 33) is [var short_] ? (short_.Requirement, short_.Met, short_.Detail) : default);
        Assert.True(Assert.Single(SpecCheck.Duration("A demo of at least 45 seconds.", 62)).Met);
        Assert.Empty(SpecCheck.Duration("A short clip.", 20));
    }

    [Fact] public void EveryPostMeetsItsNetworkLimitHasOneLinkAndASeriesOpensDifferently()
    {
        // The build-log series from the live check: the same opening three times, over X's and Bluesky's limits, two links each.
        const string opening = "We gave HireZero five real marketing assignments in its first four-hour shift:";
        const string links = "\n\nRead the build log: https://hirezero.app/blog/our-ai-marketing-employees-first-four-hour-shift\n\nSign up for the beta: https://hirezero.app/#launch";
        var series = SpecCheck.Posts([("LinkedIn", opening + " A customer FAQ." + links), ("X", opening + new string('x', 300) + links), ("Bluesky", opening + new string('b', 320) + links)]);
        Assert.Contains(series, result => result.Requirement == "the X post within 280 characters" && !result.Met);
        Assert.Contains(series, result => result.Requirement == "the Bluesky post within 300 characters" && !result.Met);
        Assert.DoesNotContain(series, result => result.Requirement.Contains("LinkedIn post within"));   // 3,000 is plenty
        Assert.Equal(3, series.Count(result => result.Requirement.StartsWith("one link in the")));
        Assert.Contains(series, result => result.Requirement == "each post opens its own way");
        // X counts a link as 23 characters, so a short post with a long link is fine; different openings pass.
        Assert.Empty(SpecCheck.Posts([("X", "Open source, and it runs on your own computer today. https://hirezero.app/" + new string('/', 200)),
            ("Bluesky", "Four hours, eight cycles, nothing posted without us. https://hirezero.app/blog/")]));
        Assert.Empty(SpecCheck.Posts([("Email", "Subject: Hi\n\n" + new string('e', 5000) + " https://a.example https://b.example")]));   // not a social post
    }

    [Fact] public void LengthSubjectAndCitationsAreMeasured()
    {
        const string email = "Draft the welcome email. Plain text, under 150 words, with a Subject: line.";
        var body = "Subject: Welcome to HireZero\n\n" + string.Join(' ', Enumerable.Repeat("word", 120));
        Assert.All(SpecCheck.Check(email, body), result => Assert.True(result.Met, result.Requirement));
        var long_ = SpecCheck.Check(email, string.Join(' ', Enumerable.Repeat("word", 170)));
        Assert.Equal("under 150 words ✗ (170 words), a Subject: line ✗ (missing)", SpecCheck.Line(long_));
        Assert.False(Assert.Single(SpecCheck.Check("A practical blog post (800–1,100 words).", string.Join(' ', Enumerable.Repeat("word", 400)))).Met);
        Assert.True(Assert.Single(SpecCheck.Check("A practical blog post (800–1,100 words).", string.Join(' ', Enumerable.Repeat("word", 900)))).Met);
        Assert.False(Assert.Single(SpecCheck.Check("Cite every price.", "Jasper costs $69.", sources: 3)).Met);
        Assert.True(Assert.Single(SpecCheck.Check("Cite every price.", "Jasper costs $69 [1].", sources: 3)).Met);
    }
}
