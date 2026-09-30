using Microsoft.AspNetCore.Http;
using Thaddeus.Host;

namespace Thaddeus.Tests;

// A hosted entrance can give up before a slow model reply. Answer "still working" promptly
// and keep going, rather than be cancelled mid-turn and recorded as an answer nobody can confirm.
public sealed class ChatWaitTests
{
    [Fact]
    public async Task SlowReplySaysItIsStillWorkingAndKeepsGoing()
    {
        var reply = new TaskCompletionSource<IResult>();
        var answer = await MarketingBackend.WithinWait(reply.Task, TimeSpan.FromMilliseconds(50),
            () => Results.Json(new { status = "pending" }, statusCode: 202));
        Assert.Equal(202, Assert.IsAssignableFrom<IStatusCodeHttpResult>(answer).StatusCode);
        Assert.False(reply.Task.IsCompleted);
        reply.SetResult(Results.Ok(new { status = "succeeded" }));
        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await reply.Task).StatusCode);
    }

    [Fact]
    public async Task QuickReplyIsReturnedAsItIs()
    {
        var answer = await MarketingBackend.WithinWait(Task.FromResult(Results.Ok(new { status = "succeeded" })), TimeSpan.FromSeconds(5),
            () => Results.Json(new { status = "pending" }, statusCode: 202));
        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(answer).StatusCode);
    }

    [Fact]
    public void WaitEndsBeforeTheRemoteEntranceGivesUp() => Assert.InRange(MarketingBackend.ChatReplyWait, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(20));
}
