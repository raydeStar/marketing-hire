using System.Runtime.CompilerServices;

namespace Thaddeus.Tests;

/// <summary>Test hosts never reach a real employee container. Without this, a test host that isn't given a fixture ledger
/// runs startup recovery against the default container and marks a live shift's running turn unknown.</summary>
static class TestIsolation
{
    [ModuleInitializer]
    internal static void KeepTestsOffRealContainers()
    {
        foreach (var name in new[] { "Marketing__Container", "Marketing__SharedContainer", "Marketing__ShiftContainer" })
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))) Environment.SetEnvironmentVariable(name, "thaddeus-tests-no-container");
    }
}
