using System.Text.Json;
using System.Text.Json.Nodes;
using Thaddeus.Host;

namespace Thaddeus.Tests;

public sealed class MarketingModelStatusTests
{
    // Sanitized shape observed from the installed Plow base. No token value is needed or retained.
    private static JsonObject Plow() => JsonNode.Parse("""
        {"resolvedDefault":"plow/z-ai/glm-5.2","auth":{
          "runtimeAuthRoutes":[],"unusableProfiles":[],"missingProvidersInUse":[],"modelRouteIssues":[],
          "providers":[{"provider":"plow","effective":{"kind":"env"}}]}}
        """)!.AsObject();

    private static string Status(JsonObject value, string model = "plow/z-ai/glm-5.2")
        => MarketingModelStatus.Read(JsonSerializer.SerializeToElement(value), model).Status;

    [Fact]
    public void PlowEnvironmentCredentialDoesNotRequireAnOauthProfile()
        => Assert.Equal("connected", Status(Plow()));

    [Theory]
    [InlineData("missingProvidersInUse")]
    [InlineData("modelRouteIssues")]
    [InlineData("unusableProfiles")]
    public void ReportedCredentialOrRouteProblemsRemainBlocked(string field)
    {
        var report = Plow(); report["auth"]![field] = new JsonArray("unavailable");
        Assert.Equal("auth_required", Status(report));
    }

    [Fact]
    public void EnvironmentCredentialCannotOverrideAnExplicitUnusableRoute()
    {
        var report = Plow();
        report["auth"]!["runtimeAuthRoutes"] = JsonNode.Parse("""[{"provider":"plow","status":"unusable"}]""");
        Assert.Equal("auth_required", Status(report));
    }

    [Fact]
    public void OtherProvidersCredentialAndAbsentEvidenceDoNotAuthenticatePlow()
    {
        var report = Plow(); report["auth"]!["providers"]![0]!["provider"] = "openai";
        Assert.Equal("auth_required", Status(report));
        report = Plow(); report["auth"]!.AsObject().Remove("modelRouteIssues");
        Assert.Equal("auth_required", Status(report));
    }

    [Fact]
    public void AChangedModelStillFails()
        => Assert.Equal("failed", Status(Plow(), "plow/different-model"));

    [Fact]
    public void ExistingOauthRouteStillWorksWithoutEnvironmentMetadata()
    {
        var report = JsonNode.Parse("""
            {"resolvedDefault":"openai/gpt-5.6-luna","auth":{
              "runtimeAuthRoutes":[{"provider":"openai","status":"usable"}],"unusableProfiles":[]}}
            """)!.AsObject();
        Assert.Equal("connected", Status(report, "openai/gpt-5.6-luna"));
        report["auth"]!["runtimeAuthRoutes"]![0]!["status"] = "unusable";
        Assert.Equal("auth_required", Status(report, "openai/gpt-5.6-luna"));
    }
}
