using System.Text.Json;
using Thaddeus.Host;

namespace Thaddeus.Tests;

/// <summary>The model only finds a draft's checkable claims; code decides which stand, from the words the owner actually gave.</summary>
public sealed class FactAuditTests
{
    const string Facts = "{\"product_summary\":\"Handmade soy candles.\"} Lavender candle launch. Owner by text: it's $24, burns 40 hours, ships Oct 3.";
    const string Body = "Meet our lavender candle: $24, 40 hours of burn time, poured from soy. Hand-poured in Vermont with cotton wicks. Ships Oct 3.";

    static JsonElement Audit(params string[] claims) => JsonSerializer.SerializeToElement(new { claims = claims.Select(text => new { text }) });

    [Fact] public void AClaimStandsOnlyWhenItsWordsAndNumbersAreInWhatTheOwnerGave()
    {
        var unsupported = EmployeeShifts.Unsupported(Audit(
            "$24",                     // the owner's price
            "40 hours of burn time",   // their words, rearranged
            "Ships Oct 3",
            "Hand-poured in Vermont",  // a place nobody named
            "with cotton wicks",       // a material nobody named
            "poured from soy",         // soy, yes; poured, no
            "Ships Oct 5",             // a date that isn't theirs
            "a 60-hour burn"), Body.Replace("Ships Oct 3.", "Ships Oct 3. Ships Oct 5."), Facts);   // not in the body at all: ignored
        Assert.Equal(["Hand-poured in Vermont", "with cotton wicks", "poured from soy", "Ships Oct 5"], unsupported);
        Assert.Empty(EmployeeShifts.Unsupported(JsonSerializer.SerializeToElement(new { other = 1 }), Body, Facts));
        // What the judge's test turned on: "our new walnut desk" gives neither "solid" nor anything about veneer or the frame.
        const string maple = "I run Maple Desk, a small studio that makes standing desks for home offices. A launch campaign for our new walnut desk.";
        Assert.False(EmployeeShifts.Given("a standing desk out of solid walnut", maple));
        Assert.False(EmployeeShifts.Given("Not veneer. Solid walnut top", maple));
        Assert.False(EmployeeShifts.Given("Your standing frame, now in walnut.", maple));
        // What they did say stands, however it's put.
        Assert.True(EmployeeShifts.Given("We're a small studio", maple));
        Assert.True(EmployeeShifts.Given("Today we're launching the walnut standing desk.", maple));
        Assert.True(EmployeeShifts.Given("standing desks for your home office", maple));
        Assert.True(EmployeeShifts.Given("— Maple Desk", maple));
    }

    [Fact] public void WhatTheRepairLeftInIsMarkedSoItCantBePublished()
    {
        var marked = EmployeeShifts.MarkUnconfirmed(Body, ["with cotton wicks", "not in the body"]);
        Assert.Contains(EmployeeShifts.Unconfirmed + " with cotton wicks", marked);
        Assert.Equal(marked, EmployeeShifts.MarkUnconfirmed(marked, ["with cotton wicks"]));   // marked once
        Assert.Equal("blocked", CampaignQa.Check("Instagram", "https://www.instagram.com/", marked + " Shop now: https://example.com").Status);
    }

    [Fact] public void TheFactsAreTheBriefTheOwnersWordsAndTheSourcesRead()
    {
        var created = JsonSerializer.SerializeToElement(new
        {
            brief = new { product_summary = "Handmade soy candles.", claims = "Burn time 40 hours. Assume it's sold on their own site unless told otherwise." },
            task = new { title = "Lavender launch", next_action = "Owner by text: it's $24." },
            sources = new[] { new { title = "Our shop", text = "Burns 40 hours.", evidenceText = "" } },
            objectives = new { northStar = "Sell 100 candles" },   // a goal, not a fact about the product
        });
        var facts = EmployeeShifts.FactsGiven(created);
        Assert.Contains("Handmade soy candles", facts);
        Assert.Contains("it's $24", facts);
        Assert.Contains("Burns 40 hours.", facts);
        Assert.DoesNotContain("Sell 100 candles", facts);
        // A saved line that is an assumption isn't something the owner gave, so a draft can't lean on it.
        Assert.Contains("Burn time 40 hours", facts);
        Assert.DoesNotContain("own site", facts);
        Assert.False(EmployeeShifts.Given("Order it on our site", facts));
    }
}
