using System.Text.Json;

namespace Thaddeus.Host;

internal static class MarketingModelStatus
{
    internal static (string Status, string? Detail) Read(JsonElement root, string model)
    {
        if (!root.TryGetProperty("resolvedDefault", out var selected) || selected.GetString() != model)
            return ("failed", "The configured model differs from the marketing model route.");

        var auth = root.GetProperty("auth");
        var provider = model.Split('/')[0];
        var routes = auth.GetProperty("runtimeAuthRoutes").EnumerateArray()
            .Where(item => item.GetProperty("provider").GetString() == provider).ToArray();
        var usable = routes.Any(item => item.GetProperty("status").GetString() == "usable");

        // Plow 2026.9.6 reports environment credentials separately from OAuth runtime routes.
        // An explicit unusable route still wins; a livery is not a letter of recommendation.
        if (!usable && provider == "plow" && routes.Length == 0)
            usable = auth.TryGetProperty("providers", out var providers) &&
                providers.EnumerateArray().Any(item =>
                    item.GetProperty("provider").GetString() == provider &&
                    item.TryGetProperty("effective", out var effective) &&
                    effective.TryGetProperty("kind", out var kind) && kind.GetString() == "env") &&
                auth.TryGetProperty("missingProvidersInUse", out var missing) && missing.GetArrayLength() == 0 &&
                auth.TryGetProperty("modelRouteIssues", out var issues) && issues.GetArrayLength() == 0;

        return usable && auth.GetProperty("unusableProfiles").GetArrayLength() == 0
            ? ("connected", null)
            : ("auth_required", "The OpenClaw model credential needs attention.");
    }
}
