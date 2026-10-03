using System.Text.Json;
using Thaddeus.Host;

namespace Thaddeus.Tests;

/// <summary>Practice mode's stand-in model: its plan reads every assigned task, and its posts go where the assignment says.</summary>
public sealed class PracticeRuntimeTests
{
    static async Task<JsonElement> Turn(string stage, object data)
    {
        var result = await new ScriptedShiftRuntime().Turn(new ShiftTurnRequest("t", stage, "", JsonSerializer.SerializeToElement(data)), CancellationToken.None);
        return JsonDocument.Parse(result.Reply).RootElement.Clone();
    }

    [Fact] public async Task ItsPlanKeepsEveryAssignedTask()
    {
        // An assignment runs to 1,000 characters; the plan's reason is held to 500, so the whole assignment was refused as unreadable.
        var queue = new[] { "Your first week of posts", "A seasonal offer campaign" }.Select((title, index) => new { id = "t" + index, title, status = "ready", action_state = "agent_ready",
            next_action = "Deliver: " + new string('x', 980) }).ToArray();
        var reply = await Turn("prioritize", new { signals = Array.Empty<object>(), queue });
        var (priorities, _, note) = EmployeeShifts.ValidatePriorities(reply, [.. queue.Select(task => JsonSerializer.SerializeToElement(task))]);
        Assert.Equal(["t0", "t1"], priorities.Select(item => item.GetProperty("taskId").GetString()));
        Assert.DoesNotContain("couldn't read", note);
    }

    [Fact] public async Task AWeekOfPostsIsFivePostsAcrossTheNetworksItNames()
    {
        var reply = await Turn("create", new
        {
            priority = new { title = "Your first week of posts", deliverable = "draft" },
            brief = new { product_summary = "Wheel-throwing classes and handmade mugs.", audience = "Adults in Boise", channels = "LinkedIn" },
            task = new { title = "Your first week of posts", next_action = "Deliver: five posts for this week as a series, in the order to post them, across Google Business Profile, Facebook and Instagram." },
            objectives = new { callToAction = new { Label = "Book a class", Url = "" } },
        });
        var posts = reply.GetProperty("drafts").EnumerateArray().ToArray();
        Assert.Equal(["Google Business Profile", "Facebook", "Instagram", "Google Business Profile", "Facebook"], posts.Select(post => post.GetProperty("channel").GetString()));
        Assert.Equal(["https://business.google.com/", "https://www.facebook.com/", "https://www.instagram.com/"], posts.Take(3).Select(post => post.GetProperty("destination").GetString()));
        Assert.All(posts, post => Assert.EndsWith("\n\nBook a class", post.GetProperty("body").GetString()));
        Assert.StartsWith("Practice post 2 of 5 for Facebook.", posts[1].GetProperty("body").GetString());
        Assert.Equal(5, EmployeeShifts.Series(reply)!.Length);

        // One post, with no network named in the assignment: the brief's first, as a single draft.
        var single = await Turn("create", new
        {
            priority = new { title = "A launch post", deliverable = "draft" },
            brief = new { product_summary = "A planner.", audience = "Founders", channels = "Bluesky, X" },
            task = new { title = "A launch post", next_action = "Announce the launch." },
        });
        Assert.Equal(("Bluesky", "https://bsky.app/"), (single.GetProperty("channel").GetString(), single.GetProperty("destination").GetString()));
        Assert.False(single.TryGetProperty("drafts", out _));
    }
}
