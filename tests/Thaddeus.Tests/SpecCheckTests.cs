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
        // Review sees the storyboard document (a script table and its fenced JSON), not bare JSON: its running time is still read (the live check read 0).
        var board = VideoRenderer.Parse("{\"scenes\":[" + string.Join(',', Enumerable.Range(1, 8).Select(n => $"{{\"text\":\"Scene {n}\",\"seconds\":8}}")) + "]}", "Demo");
        Assert.Equal(64, VideoRenderer.Parse(VideoRenderer.Document(board), "check").Seconds);
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

    [Fact] public void AListIsCountedAgainstTheNumberThatIntroducesIt()
    {
        // The live LinkedIn post: "five" assignments, four listed.
        const string four = "We gave it five real marketing assignments:\n\nA customer FAQ.\nA two-week social calendar.\nA welcome email.\nA competitor review.\n\nIt worked through the queue.";
        Assert.Equal(("the list after “five real marketing assignments” has 5", false, "4 listed"), SpecCheck.Tallies(four) is [var result] ? (result.Requirement, result.Met, result.Detail) : default);
        // Five short sentences in one paragraph are five; a hyphenated "four-hour" isn't a count; prose after a colon isn't a list.
        Assert.Empty(SpecCheck.Tallies("We gave HireZero five real marketing assignments in its first four-hour shift:\n\nA customer FAQ. A two-week social calendar. A welcome email. A competitor review. And one more task in the build log."));
        Assert.Empty(SpecCheck.Tallies("Three things changed:\n\nCounts are now checked by code rather than by the model, which means an ask for eight questions is counted before anyone sees the work."));
        Assert.Empty(SpecCheck.Tallies("18 of 40 model turns:\n\nOne.\nTwo."));   // two numbers: which one is the count isn't clear
    }

    [Fact] public void ARedraftKeepsTheSpecificPageItLinked()
    {
        // The live LinkedIn redraft: the original shared the post; the rewrite linked the blog index.
        const string original = "Read the build log: https://hirezero.app/blog/our-ai-marketing-employees-first-four-hour-shift\n\nSign up for the beta: https://hirezero.app/#launch";
        var result = Assert.Single(SpecCheck.Narrowed(original, "I wrote up the shift here:\nhttps://hirezero.app/blog"));
        Assert.Equal(("the link to https://hirezero.app/blog/our-ai-marketing-employees-first-four-hour-shift", false), (result.Requirement, result.Met));
        Assert.Empty(SpecCheck.Narrowed(original, "The full shift: https://hirezero.app/blog/our-ai-marketing-employees-first-four-hour-shift/"));   // a trailing slash is the same page
        Assert.Empty(SpecCheck.Narrowed(original, "Sign up for the beta: https://hirezero.app/#launch"));
        Assert.Empty(SpecCheck.Narrowed(original, "Only the post now."));                                                                        // dropping a link is the owner's call
    }

    [Fact] public void APostThatSaysTheSameThingOverIsCaught()
    {
        // The live LinkedIn redraft: "runs on your own computer today" three times in 600 characters.
        const string post = "Another marketing service, or an open-source marketing employee that runs on your own computer today.\n\nMarketing help for solo founders that runs on your own computer today, not another service.\n\nHireZero is open source. It runs on your own computer today.\n\nSign up for the beta: https://hirezero.app/#launch";
        Assert.Equal(("no phrase said 3 times", false, "“runs on your own computer” 3 times"), SpecCheck.Repeats(post) is [var result] ? (result.Requirement, result.Met, result.Detail) : default);
        Assert.Empty(SpecCheck.Repeats("Open source. Runs on your own computer today.\n\nYou keep the final say: every public draft needs your approval before it's posted.\n\nSign up for the beta: https://hirezero.app/#launch https://hirezero.app/#launch"));
    }

    [Fact] public void GuidanceIsAdviceNotAChecklist()
    {
        // The first-win assignment: one deliverable, then how to go about it; only the deliverable is checked against the work.
        var asks = SpecCheck.OwnerAsks("Deliver: one concise saved document with the actual copy for ONE small improvement. Guidance: explore three angles internally and select one; ask at most one essential question if genuinely blocked.");
        Assert.Equal(["one concise saved document with the actual copy for ONE small improvement."], asks);
        Assert.Equal(2, SpecCheck.OwnerAsks("Write three posts for LinkedIn. End on the beta link.").Length);   // without the markers, every sentence is an ask
    }

    [Fact] public void NewWorkDoesntCopyTheOwnersOwnPosts()
    {
        // The live first win: its "sharper opening" was the site's headline, already one of the owner's voice examples.
        string[] voice = ["Hire a marketing employee. Keep the final say.", "Receipts make mistakes cheap. Every problem took minutes to find, because every action was on the record."];
        var copied = Assert.Single(SpecCheck.Copied("# Hire a marketing employee. Keep the final say.\n\nHireZero works shifts on your marketing from your own computer.", voice));
        Assert.Contains("hire a marketing employee", copied.Detail);
        // Quoting the current line as Before is the point of a before/after; new words after it are fine.
        Assert.Empty(SpecCheck.Copied("**Before:** Hire a marketing employee. Keep the final say.\n\n**After:** Your marketing gets done every week, and nothing goes out until you say so.", voice));
        Assert.Empty(SpecCheck.Copied("Receipts made it quick.", voice));   // too short to be a copy
    }

    [Fact] public void ADocumentForTheOwnerEndsOnTheirDecision()
    {
        // The live first win: its owner decision told the owner to sign up for their own beta.
        const string memo = "## After\n\nNew copy.\n\n## Next owner decision\n\nUse the After opening for the beta offer and decide whether to sign up for the beta: https://hirezero.app/#launch\n\n---\n\n_Marketing rubric A._";
        Assert.Single(SpecCheck.OwnerDocumentCta(memo, "https://hirezero.app/#launch"));
        Assert.Empty(SpecCheck.OwnerDocumentCta("## After\n\nSign up for the beta: https://hirezero.app/#launch\n\n## Next owner decision\n\nApprove the After opening for the launch page by Monday.", "https://hirezero.app/#launch"));   // the CTA inside the proposed copy is fine
        Assert.Empty(SpecCheck.OwnerDocumentCta(memo, null));
        // The live false alarm: the proposed After ends on the call to action, and the document ends on the owner's decision.
        Assert.Empty(SpecCheck.OwnerDocumentCta("After\n\nHireZero works shifts on your marketing.\n\n**Sign up for the beta:** https://hirezero.app/#launch\n\nWhy it matters: this leads with the founder's need.\n\nNext owner decision: approve this opening for the home page.", "https://hirezero.app/#launch"));
    }

    [Fact] public void AnAfterHasToChangeTheBefore()
    {
        // The live first win: its After opened with the Before, word for word apart from a dash.
        const string same = "# Proposed opening copy\n\n## Before\n\n“Marketing gets done in spare hours, or not at all.”\n\n## After\n\nMarketing gets done in spare hours—or not at all.\n\nHireZero is open source.\n\n## Why it matters\n\nIt speaks to founders.";
        Assert.Equal("an After that changes the Before", Assert.Single(SpecCheck.BeforeAfter(same)).Requirement);
        Assert.Empty(SpecCheck.BeforeAfter("## Before\n\nMarketing gets done in spare hours, or not at all.\n\n## After\n\nYou do marketing in the hours left over, or it waits.\n\n## Why\n\nSharper."));
        Assert.Empty(SpecCheck.BeforeAfter("A plan with no before and after."));
        // A headline of two short sentences, repeated as the After's headline, is still a repeat.
        Assert.Single(SpecCheck.BeforeAfter("Before\n\n> Hire a marketing employee. Keep the final say.\n\nAfter\n\n## Hire a marketing employee. Keep the final say.\n\nWhen marketing has to fit into spare hours, you need help.\n\nWhy it matters: it leads with the need."));
    }

    [Fact] public void AKitsPostsOpenTheirOwnWayAndItsFollowUpHasItsOwnStep()
    {
        const string seat = "https://example.com/burnout-seminar";
        // Run 8's kit, shortened: two posts opened alike, and the follow-up sent attendees to the sign-up again.
        var kit = "## Post 1 — Facebook\n\nWhen work keeps feeling heavier, you do not have to wait until you are ready for therapy.\n\nSave a seat: " + seat +
            "\n\n## Post 2 — Facebook\n\nYou do not have to wait until you are ready for therapy to start paying attention to burnout.\n\nThursday, October 16, 6:30 pm. " + seat +
            "\n\n## Follow-up email — October 17\n\nThank you for joining. You can save a seat for the next seminar here: " + seat;
        var unmet = SpecCheck.ForKind("event", "Seminar kit", [kit], seat).Select(result => result.Requirement).ToArray();
        Assert.Contains("each post opens its own way", unmet);
        // Kit run 1 had no follow-up at all; the assignment names one, so its absence is measured.
        Assert.Contains("the follow-up email, under its own heading", SpecCheck.ForKind("event", "the registration page copy; a reminder email; and a follow-up email", ["## Registration page copy\nA\n\n## Reminder email\nB"], seat).Select(result => result.Requirement));
        Assert.DoesNotContain("the reminder email, under its own heading", SpecCheck.ForKind("event", "the registration page copy; a reminder email; and a follow-up email", ["## Registration page copy\nA\n\n## Reminder email\nB"], seat).Select(result => result.Requirement));
        // Kit run 3: Post 3 and the reminder email became one "Reminder post".
        var merged = SpecCheck.ForKind("event", "Post 1 (three weeks before); Post 2 (a week before); Post 3 (the last call); a reminder email the day before", ["## Post 1\nA\n\n## Post 2\nB\n\n## Reminder post\nC"], seat).Select(result => result.Requirement).ToArray();
        Assert.Contains("Post 3, under its own heading", merged);
        Assert.Contains("the reminder email, under its own heading", merged);
        Assert.DoesNotContain("Post 1, under its own heading", merged);
        Assert.Contains("a follow-up with its own next step", unmet);
        var fixedKit = "## Post 1\n\nWork can get heavier long before it looks like burnout.\n\n" + seat + "\n\n## Post 2\n\nI started these seminars because people wanted help first.\n\nThursday, October 16, 6:30 pm. " + seat +
            "\n\n## Follow-up email\n\nThank you for coming. If you'd like to talk it through, book a free consult: [Owner: consult link].";
        unmet = SpecCheck.ForKind("event", "Seminar kit", [fixedKit], seat).Select(result => result.Requirement).ToArray();
        Assert.DoesNotContain("each post opens its own way", unmet);
        Assert.DoesNotContain("a follow-up with its own next step", unmet);
    }

    [Fact] public void TheProposedCopyEndsOnItsOneCallToAction()
    {
        const string seat = "https://example.com/burnout-seminar";
        // Run 10's site fix: the After asked twice, the consult last.
        var twoAsks = "Before:\n\n“I am a counselor.”\n\nAfter:\n\nBurned out by work? To save a seat, register here: " + seat + ". [Owner: confirm registration.] If a private conversation feels better, ask about a free 15-minute consult.\n\nWhy this is the biggest fix: clarity.";
        Assert.Single(SpecCheck.AfterEndsOnCta(twoAsks, seat));
        var oneAsk = "Before:\n\n“I am a counselor.”\n\nAfter:\n\nBurned out by work? Start with a two-hour seminar. [Owner: confirm registration.]\n\nSave a seat: " + seat + "\n\nWhy this is the biggest fix: clarity.";
        Assert.Empty(SpecCheck.AfterEndsOnCta(oneAsk, seat));
        Assert.Empty(SpecCheck.AfterEndsOnCta("Before:\n\nOld.\n\nAfter:\n\nNew words, no link.\n\nWhy: x", seat));   // copy without the link isn't judged here
    }

    [Fact] public void AKitWithRulesBetweenItsPiecesEndsWhereItEnds()
    {
        const string seat = "https://example.com/burnout-seminar";
        // Kit run 8 separated its pieces with --- and ended on the owner's decision; the check read only the first piece.
        var kit = "## 1) Registration page copy\n\nSave a seat: " + seat + "\n\n---\n\n## 6) Follow-up email\n\nThank you.\n\nOwner decision: approve this kit.";
        Assert.Empty(SpecCheck.OwnerDocumentCta(kit, seat));
        Assert.Empty(SpecCheck.OwnerDocumentCta(kit + "\n\n---\n\n_Marketing rubric A_", seat));
        Assert.Single(SpecCheck.OwnerDocumentCta("Memo.\n\nDecide: sign up at " + seat + "\n\n---\n\n_Marketing rubric B_", seat));
    }

    [Fact] public void AStoryTheWriterWasGivenIsToldNotAskedFor()
    {
        const string stories = "## How we started\n\nPeople wanted help but weren't ready to sit on a couch.\n\n## Something we believe\n\nRest is not a reward for finishing. It is part of doing the work.";
        Assert.Single(SpecCheck.AskedForGiven("## Post 1\n\n[Owner: insert the exact belief from “Something we believe,” followed by one useful idea.]", stories));
        Assert.Empty(SpecCheck.AskedForGiven("## Post 1\n\nRest is not a reward for finishing.\n\n[Owner: add the consult link.]", stories));
        Assert.Empty(SpecCheck.AskedForGiven("[Owner: add your story here.]", null));   // no stories given: asking is right
    }

    [Fact] public void ACopyIsACopyWhateverItsQuotes()
    {
        // Run 8's first post reprinted the owner's tip with curly quotes; the check compared quote characters and missed it.
        string[] own = ["A small thing that helps: end the workday on purpose. Close the laptop, say \"done,\" and walk around the block."];
        Assert.Single(SpecCheck.Copied("One small thing that helps: end the workday on purpose. Close the laptop, say “done,” and walk around the block.", own));
        Assert.Empty(SpecCheck.Copied("Mark the end of the day with one small ritual of your own.", own));
    }

    [Fact] public void AWeeksMixIsMeasuredByWhereTheLinksAre()
    {
        const string week = "Posts 1 to 3 each teach one useful idea from the owner's field, with no event details and no link, and end on a reply or a save; posts 4 and 5 promote the next seminar or a consult and end on the call to action.";
        const string seat = "https://example.com/burnout-seminar";
        // The live week that went wrong: all five promoted the seminar.
        string[] allPromo = [.. Enumerable.Range(1, 5).Select(n => $"Post {n}. Save a seat: {seat}")];
        Assert.Equal(["posts 1 to 3 teach, without a link"], SpecCheck.Mix(week, allPromo, seat).Select(result => result.Requirement));
        string[] right = ["Notice what work costs you.", "Rest is part of the work.", "Name the hardest hour.", $"Thursday, October 16. {seat}", $"Six runs so far. {seat}"];
        Assert.Empty(SpecCheck.Mix(week, right, seat));
        Assert.Equal(["posts 4 and 5 carry the call to action"], SpecCheck.Mix(week, [.. right[..4], "No link here."], seat).Select(result => result.Requirement));
        Assert.Equal((1, 3), SpecCheck.PostRange("Posts 1 to 3 each teach one useful idea"));
        Assert.Equal((4, 5), SpecCheck.PostRange("posts 4 and 5 promote the next seminar"));
        Assert.Equal((3, 3), SpecCheck.PostRange("Post 3: how it works."));
        Assert.Null(SpecCheck.PostRange("Each post opens its own way."));
        // Run product-1's site fix kept the headline and added lines under it; a kept line, marked, is no repeat.
        Assert.Empty(SpecCheck.BeforeAfter("## Before\n\n“Hire a marketing employee. Keep the final say.”\n\n## After\n\n**Headline (unchanged):** Hire a marketing employee. Keep the final say.\n\n**Supporting line:** It runs on your own computer today.\n\n## Why\n\nClarity."));
        Assert.Single(SpecCheck.BeforeAfter("## Before\n\n“Hire a marketing employee. Keep the final say.”\n\n## After\n\n**Headline:** Hire a marketing employee. Keep the final say.\n\n## Why\n\nClarity."));
        Assert.Equal(2, EmployeeShifts.SeriesParts("One.\n\n---\n\nTwo."));
        // "## Posts" heads a section; only a singular "Post" heading is one.
        Assert.Equal("3", Assert.Single(SpecCheck.Check("Three posts leading up to it.", "## Posts\n\n### Post 1\nA\n\n### Post 2\nB\n\n### Post 3\nC")).Detail);
    }

    [Fact] public void TheLocalChecksReadTheProfileCopyAsWritten()
    {
        // Local run 3: plain "Services:" and "Week 1 —" labels; the description is one short paragraph.
        var copy = "Description:\nRise & Crumb is a neighbourhood bakery on Tennyson Street in Denver.\n\nServices:\n- Sourdough bread\n- Morning buns\n- Coffee\n- Pre-orders\n- Visits\n\n" +
            string.Join("\n\n", Enumerable.Range(1, 4).Select(n => $"Week {n} — [Owner: add date]\nA loaf.\nGet directions: https://maps.google.com/?q=x"));
        Assert.Empty(SpecCheck.ForKind("local", "Google Business Profile", [copy], null));
        Assert.Equal("4", Assert.Single(SpecCheck.Check("Four weekly posts, headed Week 1 to Week 4.", copy)).Detail);
        // A quote is the work's words, whatever the punctuation between them.
        Assert.True(SpecCheck.Quotes("## Owner's decision\nApprove this replacement for the description.", "Owner’s decision: approve this replacement for the description."));
        Assert.False(SpecCheck.Quotes("## Owner's decision\nApprove this replacement.", "The owner should approve a different replacement."));
    }

    [Fact] public void ALabelIsntACount()
    {
        // The live paid plan: "Variant 1 headlines:" then two headlines was read as a list of one.
        Assert.Empty(SpecCheck.Tallies("Variant 1 headlines:\n\n- AI Marketing Employee\n- Marketing Help for Founders"));
        Assert.Single(SpecCheck.Tallies("Three headlines:\n\n- AI Marketing Employee\n- Marketing Help for Founders"));
    }

    [Fact] public void EachMeansEachPiece()
    {
        // The live paid plan: two variants, each with three headlines, is six headlines, not "3 (found 25)".
        Assert.DoesNotContain(SpecCheck.Check("4) Two ad variants, each with three headlines within 30 characters.", "## Variant A\n- a\n- b\n- c\n\n## Variant B\n- d\n- e\n- f"), result => result.Requirement.EndsWith("headlines"));
        // The live replies: under 80 words each, measured reply by reply.
        var replies = string.Join("\n\n---\n\n", Enumerable.Range(0, 3).Select(_ => string.Join(" ", Enumerable.Repeat("word", 34))));
        var limit = Assert.Single(SpecCheck.Check("Deliver: a short reply to each of these three comments, under 80 words each.", replies), result => result.Requirement.StartsWith("under 80"));
        Assert.True(limit.Met);
        Assert.False(Assert.Single(SpecCheck.Check("A post under 80 words.", replies), result => result.Requirement.StartsWith("under 80")).Met);   // one piece: the whole
        Assert.True(SpecCheck.Numbered("Deliver: 1) Sent right after sign-up. 2) Sent three days later. 3) A week later. Guidance: x"));
        Assert.False(SpecCheck.Numbered("Deliver: five posts. Guidance: 1) one idea."));
    }

    [Fact] public void ADocumentHasWhatItWasAskedToMark()
    {
        const string asked = "Deliver: 6) Each assumption marked (assumption) where it is stated. 7) How it serves the north star, then the owner's decision and the date to decide by.";
        // The live one-pager: no marks, and it ended on a guardrail.
        Assert.Equal(["assumptions marked (assumption)", "it ends on the owner's decision"],
            SpecCheck.Document(asked, "## Why us\n\nIt asks first.\n\n## Guardrail\n\nDo not describe it as hosted.\n\n---\n\n_Marketing rubric B_").Select(result => result.Requirement));
        Assert.Empty(SpecCheck.Document(asked, "Agencies cost more. (assumption)\n\n## Decision\n\nApprove this positioning by October 3."));
        Assert.Empty(SpecCheck.Document("Write a memo.", "No marks and no decision."));   // nothing asked, nothing measured
    }

    [Fact] public void SourceFilesHoldNoControlCharacters()
    {
        // A pattern written with a stray backspace where \b was meant compiles and never matches: two checks were silently off.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Thaddeus.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var bad = Directory.EnumerateFiles(Path.Combine(root!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && File.ReadAllText(file).Any(ch => ch < ' ' && (ch > 13 || ch == 11 || ch == 12 || ch < 9)))
            .Select(Path.GetFileName).ToArray();
        Assert.Empty(bad);
    }

    [Fact] public void ASentenceIsntSaidInTwoPostsOfAWeek()
    {
        // The community week: the owner's line in posts 2 and 4; the meetup's details repeat, as they should.
        string[] week = ["Getting outside with little kids isn’t about the hike. It’s about not doing it alone. Sunday, October 19 at 9 am at the boathouse.",
                         "Would a stroller walk make Sunday morning easier? We’ll meet at the boathouse at 9 am on Sunday, October 19. Getting outside with little kids isn’t about the hike."];
        Assert.Contains("posts 1 and 2", SpecCheck.RepeatedAcross(week));
        Assert.Null(SpecCheck.RepeatedAcross(["Sunday, October 19 at 9 am, meeting at the Washington Park boathouse.", "Sunday, October 19 at 9 am, meeting at the Washington Park boathouse."]));
        Assert.Single(SpecCheck.RelativeDays("Could a Sunday morning walk be the easiest way to get outside this week?"));
    }

    [Fact] public void ASeriesCountIsTheHostsToMeasure()
    {
        Assert.Equal((5, "posts"), SpecCheck.SeriesCount("five posts for this week as a series, in the order to post them, across Facebook, LinkedIn and Instagram."));
        Assert.Null(SpecCheck.SeriesCount("five posts that each tell a customer story"));   // says more than a count
        Assert.Null(SpecCheck.SeriesCount("Posts 1 to 3 each teach one useful idea"));
    }

    [Fact] public void AnAskAboutSomeOfTheSeriesIsShownInThatMany()
    {
        Assert.Equal((3, false), SpecCheck.Counted("Three teach one useful idea each from the owner's field.", 5));
        Assert.Equal((2, true), SpecCheck.Counted("at most two promote the next seminar or a consult.", 5));
        Assert.Null(SpecCheck.Counted("five posts for this week as a series, in the order to post them.", 5));   // every post
        Assert.Null(SpecCheck.Counted("End each post on one ask.", 5));
    }

    [Fact] public void AKitsPostsAreCountedByTheirOwnSections()
    {
        // The live seminar kit: page copy, three posts, two emails, then its plan. "Three posts" was counted as nine.
        var kit = "## Registration page copy\nCome.\n\n## Post 1 — early promotion\nA.\n\n## Post 2 — story\nB.\n\n## Post 3 — final promotion\nThursday night I am running it again.\n\n" +
            "## Reminder email — day before\nSubject: Tomorrow evening\n\nSee you tomorrow evening.\n\n## Follow-up email\nThanks.\n\n## Dependencies\n- x\n\n## Timing\n- y\n\n## Owner decision\nApprove.";
        Assert.Equal("3", Assert.Single(SpecCheck.Check("Three posts leading up to it, a reminder email the day before, and a follow-up email.", kit)).Detail);
        Assert.Equal(3, SpecCheck.Sections(kit, "post").Length);
        // The posts' relative day is caught; the day-before email's "tomorrow" is not.
        Assert.Equal(["the event's time", "dates written as dates in the posts"], SpecCheck.ForKind("event", "Seminar kit", [kit], null).Select(result => result.Requirement).Where(item => item != "the event's date"));
    }

    [Fact] public void AReviewAnswerClosedEarlyIsStillRead()
    {
        // The shapes the live reviews came back in: the root closed early, the last field after it.
        using (var moved = EmployeeShifts.Lenient("{\"scores\":{\"brand\":5},\"revised\":{\"title\":\"T\",\"body\":\"B\"}},\"edits\":[{\"find\":\"a\",\"replace\":\"b\"}]}"))
        {
            Assert.Equal("B", moved.RootElement.GetProperty("revised").GetProperty("body").GetString());
            Assert.Equal(1, moved.RootElement.GetProperty("edits").GetArrayLength());
        }
        using (var first = EmployeeShifts.Lenient("{\"scores\":{\"brand\":5},\"revised\":{\"title\":\"T\",\"body\":\"B\"}}],\"edits\":[]}"))
            Assert.Equal("B", first.RootElement.GetProperty("revised").GetProperty("body").GetString());
        using (var wrapped = EmployeeShifts.Lenient("Here is the review: {\"scores\":{\"brand\":4}} Thanks."))
            Assert.Equal(4, wrapped.RootElement.GetProperty("scores").GetProperty("brand").GetInt32());
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => EmployeeShifts.Lenient("{\"scores\":{\"brand\":"));
    }

    [Fact] public void APostGivesADateAsTheDate()
    {
        // The first practice shift wrote "Thursday night, I'm running it again" three weeks before a seminar on Thursday, October 16.
        Assert.Single(SpecCheck.RelativeDays("Six times I have run this seminar, and Thursday night, I'm running it again."));
        Assert.Single(SpecCheck.RelativeDays("See you tomorrow."));
        Assert.Empty(SpecCheck.RelativeDays("It's Thursday, October 16, at 6:30 pm at the Park Hill library."));
        Assert.Empty(SpecCheck.RelativeDays("Thursday Oct 16, 6:30 pm."));
        Assert.Single(SpecCheck.RelativeDays("Join me on Thursday at the library."));
        Assert.Empty(SpecCheck.RelativeDays("Every Sunday night I plan the week; on Mondays I rest."));   // habits, not dates
        Assert.Empty(SpecCheck.RelativeDays("Join me on Thursday, October 16."));
        // The full first shift's third post: a question about the reader's week, not a date for anything.
        Assert.Empty(SpecCheck.RelativeDays("One smaller first step is to ask: “What would make this week 10 percent more manageable?”"));
        Assert.Single(SpecCheck.RelativeDays("The seminar is this week, so save a seat."));
        // Local run 4: the bakery's standing cutoff, as the owner gives it.
        Assert.Empty(SpecCheck.RelativeDays("Saturday sourdough: pre-order by Thursday, 5 pm, and we'll hold a loaf with your name on it."));
        Assert.Empty(SpecCheck.RelativeDays("Phone by Thursday at 5 pm and we’ll set one aside for you."));
        // A post that is only placeholders and a link.
        Assert.Contains(SpecCheck.Posts([("Facebook", "[Owner: add one true at-home sourdough tip from the baker.]\n\n[Owner: add the date for this post]\n\nGet directions: https://maps.google.com/?q=x")]), result => result.Requirement == "each post has something to say");
        Assert.DoesNotContain(SpecCheck.Posts([("Facebook", "It takes 36 hours to make Saturday’s sourdough. Plan for it before the weekend.\n\nGet directions: https://maps.google.com/?q=x")]), result => result.Requirement == "each post has something to say");
        Assert.Single(SpecCheck.Posts([("Facebook", "Thursday night I'm running it again.")]), result => result.Requirement == "dates written as dates");
    }

    [Fact] public void APracticePromisesNoOutcomesAndTellsNoClientStoryWithoutConsent()
    {
        Assert.Equal(["no promised outcomes", "a client's story only with their consent"],
            SpecCheck.Guardrails("This seminar will fix your anxiety. One client told me she felt stuck for years.", "practice").Select(item => item.Requirement));
        Assert.Empty(SpecCheck.Guardrails("One client, who gave written consent to share this, told me she felt stuck.", "practice"));
        Assert.Empty(SpecCheck.Guardrails("What a first session involves, and who it is for.", "practice"));
        Assert.Empty(SpecCheck.Guardrails("This release will fix the sync bug.", "product"));   // only the practice playbook carries these
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
