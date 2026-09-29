using Microsoft.Extensions.Configuration;
using Thaddeus.Host;

namespace Thaddeus.Tests;

/// <summary>A command line on Windows holds about 32,000 characters: a live turn with a 30 KB prompt couldn't start, and its
/// reserved turn held the meter. A model turn's parameters go in through standard input to a shell in the container instead.</summary>
public sealed class ProcessTransportTests
{
    static MarketingProcessTransport Transport(string mode) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Marketing:Transport"] = mode }).Build(), "employee");

    [Fact] public void BigParametersGoThroughStandardInput()
    {
        var prompt = "{\"message\":\"" + new string('x', 30_000) + " it's here\"}";
        string? input = null;
        var arguments = Transport("docker").ThroughInput(["openclaw", "gateway", "call", "agent", "--params", prompt, "--expect-final"], ref input);
        Assert.Equal(prompt, input);
        Assert.Equal(["sh", "-c"], arguments[..2]);
        Assert.Equal("exec 'openclaw' 'gateway' 'call' 'agent' '--params' \"$(cat)\" '--expect-final'", arguments[2]);
        Assert.True(arguments.Sum(argument => argument.Length) < 200);
    }

    [Fact] public void SmallParametersAndDirectRunsAreLeftAsTheyAre()
    {
        string? input = null;
        string[] small = ["openclaw", "gateway", "call", "health", "--params", "{}"];
        Assert.Equal(small, Transport("docker").ThroughInput(small, ref input));
        Assert.Null(input);
        // Run directly on Linux (hosted), there's no such limit: the command stays as it is.
        string[] big = ["openclaw", "gateway", "call", "agent", "--params", new string('x', 30_000)];
        Assert.Equal(big, Transport("direct").ThroughInput(big, ref input));
        Assert.Null(input);
    }
}
