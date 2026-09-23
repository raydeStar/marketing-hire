using System.Net;
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

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.0.0.8")]
    [InlineData("192.0.2.1")]
    [InlineData("192.88.99.2")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.254")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("240.0.0.1")]
    [InlineData("::1")]
    public void PublicReadRejectsSpecialPurposeAddresses(string address) =>
        Assert.False(MeetingSourceReader.PublicIPv4(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("192.31.196.1")]
    public void PublicReadCanConnectToOrdinaryPublicAddresses(string address) =>
        Assert.True(MeetingSourceReader.PublicIPv4(IPAddress.Parse(address)));

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

    [Fact] public void WorkerDraftAcceptsBoundedAssumptionList()
    {
        const string draft = """{"audience":"Technical founders","angle":"Practical research","draft":"Ask one customer question before making a claim.","ownerNextAction":"Review the language","assumptions":["Audience is provisional","No customer proof yet"]}""";
        var content = MeetingWorkerResult.Parse("local_draft", draft, Sources);
        Assert.Contains("Audience is provisional; No customer proof yet", content);
        Assert.Throws<InvalidOperationException>(() => MeetingWorkerResult.Parse("local_draft",
            draft.Replace("No customer proof yet", new string('x', 501)), Sources));
    }
}
