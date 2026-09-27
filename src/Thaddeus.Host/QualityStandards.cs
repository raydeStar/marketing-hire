using System.Text.RegularExpressions;

namespace Thaddeus.Host;

/// <summary>What an A looks like, for each kind of work and for each rubric category. The writer drafts to it and the reviewer
/// grades against it, so both aim at the same target instead of a general sense of "good".</summary>
public static class QualityStandards
{
    /// <summary>The kind of work a priority asks for, from what it is (deliverable, channel) and what it says (title, assignment).</summary>
    public static string Kind(string deliverable, string channel, string text)
    {
        bool Has(string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
        if (deliverable == "video" || Has(@"\b(video|clip|reel)\b") && deliverable != "document") return "video";
        if (deliverable == "experiment") return "experiment";
        if (Has(@"\bfaq\b|frequently asked")) return "faq";
        if (deliverable == "page" || Has(@"\b(landing page|home ?page|page copy)\b")) return "page";
        if (Has(@"\bcampaign plan\b|\bplan (?:a|the|our) [\w -]*campaign\b")) return "campaign";
        if (Has(@"\b(calendar|editorial plan|content plan|posting plan)\b")) return "calendar";
        if (Has(@"\b(competitor|competitive|battlecard|rivals?)\b")) return "competitor";
        if (Regex.IsMatch(channel, @"email|newsletter", RegexOptions.IgnoreCase) || Has(@"\b(email|newsletter)\b")) return "email";
        if (Regex.IsMatch(channel, @"blog|article", RegexOptions.IgnoreCase) || Has(@"\b(blog|article|guide)\b")) return "blog";
        if (deliverable == "draft" || Has(@"\b(posts?|thread|tweet)\b")) return "social";
        return "document";
    }

    static readonly Dictionary<string, string[]> Standards = new()
    {
        ["faq"] =
        [
            "The questions a buyer asks before trying, in the order they worry; the objections on the facts page come first, in the buyer's words.",
            "Each answer opens with a direct answer (yes, no, or one line), then one or two sentences of specifics.",
            "What works today and what isn't ready are stated exactly as the facts page states them: never overstated, never undersold.",
            "Proof that only this company has (from the facts page) sits inside the answers, not as slogans.",
            "It ends with the call to action and its link."
        ],
        ["blog"] =
        [
            "The title and first paragraph promise one specific outcome to one named reader, with a point of view a generic tool wouldn't take.",
            "It is built around a concrete worked example the reader can copy (a day, a before and after, a template); each section is usable on its own.",
            "It holds one idea worth quoting or passing on: a rule of thumb, a sharp distinction, a checklist, grounded in the sources or the facts page.",
            "The company appears where it naturally solves the problem described, once, honestly, within the facts page.",
            "It ends on the call to action and its link; headings are skimmable and no sentence is filler."
        ],
        ["email"] =
        [
            "The subject line is specific and under 50 characters; the first line earns the second.",
            "One job: the reader knows what this is, what happens next, and does one thing now.",
            "That one thing is the call to action with its link written out; a reply ask is a question answerable in one line.",
            "It reads like a person at the company wrote it, within the length asked, with no marketing filler.",
            "Nothing contradicts the facts page, especially what works today versus what is waitlisted."
        ],
        ["social"] =
        [
            "The first line works alone in a feed: a specific claim, a number from the sources, a question or a contrast, never an announcement.",
            "One idea per post, native to the channel's length and conventions; no hashtag stuffing.",
            "A concrete detail only this company could say (a proof point from the facts page), not a category claim.",
            "It ends with one ask that suits the channel: reply, comment, or the call to action's link.",
            "In a series, every post has its own angle, hook and ask; none repeats another."
        ],
        ["calendar"] =
        [
            "Every item has its own angle tied to an objective or the campaign's goal, and the plan builds across the weeks (attention, then proof, then the ask).",
            "Each hook is written as the actual first line, specific enough to post as is, drawn from proof points, sources or customer problems.",
            "Calls to action fit their purpose (a reply to learn, the call to action's link to convert) and say exactly what the reader does; at most a third go to the home page.",
            "Each item says what it tests or teaches (which audience, which message), so the plan produces evidence.",
            "It fits each channel's norms and the campaign's dates, and one person could publish it."
        ],
        ["campaign"] =
        [
            "One measurable goal on a \"Goal:\" line that serves the north star, with a number and a date, and a \"Channels:\" line; the title carries the dates.",
            "One audience, one core message and the two or three proof points from the facts page that carry it, stated before the schedule.",
            "A dated schedule (a heading per day or week) of specific pieces: what, which channel, the hook as its first line, and its call to action; what can ship first comes first.",
            "How it's measured: which signal is checked when, and the rule for pushing or changing course at each checkpoint.",
            "Honest about constraints: what isn't ready yet and what the owner must do, sized for one person's week.",
            "It makes choices: two or three channels with the reason for each (the evidence there is), and what it deliberately leaves out this time and why."
        ],
        ["competitor"] =
        [
            "It leads with the answer: what changed since the last reading (or that this is the first baseline, and what to watch next time), and the one implication.",
            "Every price, plan and claim is cited to the page it came from with the date read; gaps are named as gaps.",
            "A small table compares what buyers choose on: pricing model, who it's for, what it does on its own, how much control the buyer keeps.",
            "One message to test, written as the line to use, with why it should win against these rivals.",
            "One next step for the owner, with who and when."
        ],
        ["page"] =
        [
            "The headline says who it's for and the outcome in plain words; the subhead says how.",
            "Each section answers a buyer question in the order they'd ask it, with proof from the facts page.",
            "One call to action, repeated at natural points with the same words and link.",
            "What's ready and what isn't are stated as the facts page states them.",
            "Short sections, specific headings, no filler."
        ],
        ["video"] =
        [
            "The first scene is a hook that works with the sound off: a problem or a claim in under eight words.",
            "It shows the product: scenes use shots of pages on the owner's site where they help, and say what the viewer sees.",
            "One idea per scene, in an arc: the problem, how it works, the proof, the action.",
            "The caption stands on its own as a post and carries the call to action.",
            "The last scene is the call to action and says exactly what to do."
        ],
        ["document"] =
        [
            "It leads with the answer or recommendation in the first three lines.",
            "Every number and claim is cited, or marked as a hypothesis.",
            "It is organised for a decision: the options, the evidence, the recommendation, and what would change it.",
            "It is specific to this company's situation, not a general primer.",
            "It ends with next steps: who does what, by when."
        ]
    };

    /// <summary>The A standard for a kind of work, or null when there is none (an experiment is checked by its own rules).</summary>
    public static string[]? For(string kind) => Standards.TryGetValue(kind, out var points) ? points : null;

    /// <summary>What a 5 and a 3 are in each rubric category, so a grade means the same thing in every pass.</summary>
    public const string Levels =
        "strategy 5: the reader's outcome plainly serves the north star or an objective and the assignment is done exactly; 3: on topic, the link to the goal left implied. " +
        "customer 5: names a specific reader situation in the reader's words from the brief, facts or sources; 3: a generic audience (\"founders\", \"small teams\") with no situation. " +
        "distinctive 5: uses at least one proof point or true story only this company has, in context, so a rival couldn't publish it by swapping the name; 3: true but generic to the category. " +
        "channel 5: native length, format and conventions; 3: the right channel in the wrong shape. " +
        "brand 5: the brief's voice throughout, and when voice examples are given it could sit beside them unnoticed; 3: neutral corporate. Public work speaks as the business, in the voice page's person (\"we\" when it says so): a mistake the product or the AI employee made is said to be its own (\"our FAQ said…\", \"HireZero's first draft…\"), never \"I got this wrong\", which puts it on the owner; that misattribution scores brand and claims 3 or lower. " +
        "action 5: one specific ask that ends the piece (for public work, the call to action and its link when one is set; for a document to the owner, the one decision they make); 3: \"visit our site\" or several asks. " +
        "claims 5: every claim is on the facts page or cited, nothing overstated or undersold; 3: mostly supported, some vague or hedged. " +
        "shareable 5: holds one idea someone would forward or quote; 3: correct but forgettable.";
}
