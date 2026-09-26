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
    public void KindComesFromWhatTheWorkIs(string deliverable, string channel, string text, string kind)
    {
        Assert.Equal(kind, QualityStandards.Kind(deliverable, channel, text));
        Assert.Equal(kind == "experiment", QualityStandards.For(kind) is null);
    }
}
