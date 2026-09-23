using Thaddeus.Host;

namespace Thaddeus.Tests;

public sealed class MeetingExecutionTests
{
    private static readonly string[] Sources = ["https://news.ycombinator.com/item?id=47667504", "https://news.ycombinator.com/item?id=49703771"];

    [Theory]
    [InlineData("http://news.ycombinator.com/item?id=47667504")]
    [InlineData("https://localhost/item?id=47667504")]
    [InlineData("https://news.ycombinator.com/redirect?id=47667504")]
    [InlineData("https://news.ycombinator.com/item?id=47667504#fragment")]
    [InlineData("https://news.ycombinator.com/item?id=47667504&next=https://example.com")]
    public void PublicReadRejectsUrlsOutsideExactPilotHostAndShape(string url) => Assert.False(MeetingSourceReader.Allowed(url));

    [Fact] public void WorkerEvidenceNeedsThreeAnglesBothSourcesAndExplicitGaps()
    {
        const string one = "https://news.ycombinator.com/item?id=47667504";
        const string two = "https://news.ycombinator.com/item?id=49703771";
        var valid = $$"""{"angles":[{"title":"A","sourceUrl":"{{one}}","evidence":"Founder asks for help","whyRelevant":"Pilot concern","assumption":"One post"},{"title":"B","sourceUrl":"{{two}}","evidence":"Comments question automation","whyRelevant":"Quality constraint","assumption":"One thread"},{"title":"C","sourceUrl":"{{one}}","evidence":"Founder describes stalled traction","whyRelevant":"Workflow question","assumption":"Anecdote"}],"gaps":["No buyer validation"]}""";
        var content = MeetingWorkerResult.Parse("evidence_brief", valid, Sources);
        Assert.Contains(one, content); Assert.Contains(two, content); Assert.Contains("No buyer validation", content);
        Assert.Throws<InvalidOperationException>(() => MeetingWorkerResult.Parse("evidence_brief", valid.Replace(two, "https://example.com"), Sources));
        var retrieved = new Dictionary<string, string> { [one] = "Founder asks for help and Founder describes stalled traction", [two] = "Comments question automation" };
        Assert.Contains("Source excerpt", MeetingWorkerResult.Parse("evidence_brief", valid, Sources, retrieved));
        retrieved[one] = "Unrelated page content";
        Assert.Throws<InvalidOperationException>(() => MeetingWorkerResult.Parse("evidence_brief", valid, Sources, retrieved));
    }
}
