using Thaddeus.Host;

namespace Thaddeus.Tests;

/// <summary>Listening brings the owner what's about them: a topic too broad to mean anything isn't listened for, and a Hacker News
/// question counts only when it names the topic itself (the search also matches words deep in the comments).</summary>
public sealed class ListeningRelevanceTests
{
    [Theory]
    [InlineData("General", true)]
    [InlineData("Marketing", true)]
    [InlineData("AI", true)]
    [InlineData("specific competitor not identified", true)]
    [InlineData("Open-source agents — various providers", true)]
    [InlineData("Jasper", false)]
    [InlineData("AI marketing employee", false)]
    [InlineData("Social scheduling and autonomous marketing tools", false)]
    public void ATopicTooBroadIsntListenedFor(string topic, bool generic) => Assert.Equal(generic, MarketListening.Generic(topic));

    [Fact] public void AHackerNewsQuestionCountsOnlyWhenItNamesTheTopic()
    {
        Mention On(string title) => new("m", "AI marketing employee", "Hacker News", title, "", "https://news.ycombinator.com/item?id=1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "neutral");
        Assert.False(MarketListening.Relevant(On("Ask HN: Jeeves. Reasoning improves Jev-like decisions?")));
        Assert.True(MarketListening.Relevant(On("Ask HN: Has anyone tried an AI marketing employee?")));
        // News and followed feeds come as they are.
        Assert.True(MarketListening.Relevant(new("m", "AI marketing employee", "Google News", "Startups hire software", "", "https://news.example/1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "neutral")));
    }
}
