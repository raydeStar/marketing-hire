using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>Which work answered which signal: the reply drafted for a public question, the post drafted for a competitor's
/// price change. "While you were away" shows the owner that work next to what was noticed.</summary>
public sealed partial class EmployeeShifts
{
    const string AnswersKey = "signal-answers-v1";

    Dictionary<string, string[]> Answers() { lock (store) return store.Setting(AnswersKey) is { } json ? Wire.Unpack<Dictionary<string, string[]>>(json) : []; }

    public void RecordAnswer(string signalRef, IEnumerable<string> keys)
    {
        var made = keys.Where(key => key.Length > 0).Distinct().ToArray();
        if (signalRef.Length == 0 || made.Length == 0) return;
        lock (store)
        {
            var answers = Answers();
            answers[signalRef] = [.. (answers.GetValueOrDefault(signalRef) ?? []).Concat(made).Distinct().TakeLast(8)];
            // The oldest answers go first once there are many; signals are about recent days.
            foreach (var old in answers.Keys.Take(Math.Max(0, answers.Count - 300)).ToArray()) answers.Remove(old);
            store.Setting(AnswersKey, Wire.Pack(answers));
        }
    }

    /// <summary>What the shifts made for a signal (draft:…, wiki:…), oldest first.</summary>
    public string[] Answered(string signalRef) => Answers().GetValueOrDefault(signalRef) ?? [];

    /// <summary>The owner asked, with one tap, for work on something noticed while they were away: it is the next shift's task.</summary>
    public async Task<string> AssignFromAway(string title, string next) =>
        await CreateTask(title, next, "high", "ready", "agent_ready") ?? throw new InvalidOperationException("The task couldn't be created. Try again.");
}
