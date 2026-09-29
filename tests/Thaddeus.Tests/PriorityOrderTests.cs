using System.Text.Json;
using Thaddeus.Host;

namespace Thaddeus.Tests;

/// <summary>Handed something important mid-shift, it finishes the step it's on and does that next: a high-priority request goes
/// ahead of the work it chose itself, behind only the owner's send-backs.</summary>
public sealed class PriorityOrderTests
{
    static JsonElement Task(string id, string title, string priority) => JsonSerializer.SerializeToElement(new { id, title, next_action = title, status = "ready", action_state = "agent_ready", priority });

    [Fact] public void AHighPriorityRequestGoesAheadOfItsOwnPicks()
    {
        // The plan filled every slot with work of its own choosing (a reply to a public question, two ideas of its own).
        var plan = JsonSerializer.SerializeToElement(new { priorities = new object[] {
            new { title = "Reply to a Hacker News question", reason = "A public question", deliverable = "draft", taskId = (string?)null, signalRef = "listen:question:1" },
            new { title = "A comparison page", reason = "Its own idea", deliverable = "document", taskId = (string?)null },
            new { title = "A launch email", reason = "Its own idea", deliverable = "draft", taskId = (string?)null } }, newTasks = Array.Empty<object>(), note = "Its plan." });
        var queue = new List<JsonElement> { Task("t-low", "Tidy the About page", "normal"), Task("t-seo", "Check my site for SEO", "high") };
        var (chosen, _, note) = EmployeeShifts.ValidatePriorities(plan, queue);
        Assert.Equal("t-seo", chosen[0].GetProperty("taskId").GetString());
        Assert.Equal(3, chosen.Length);
        Assert.DoesNotContain(chosen, item => item.GetProperty("title").GetString() == "A launch email");   // its last own pick made room
        Assert.Contains("high-priority request", note);
    }
}
