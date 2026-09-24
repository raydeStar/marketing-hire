using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Thaddeus.Host;

public sealed record CustomerLoginSettings(string Authority, string ClientId, string ClientSecret,
    string Origin, string? OwnerSubject, string GoogleConnection, string MicrosoftConnection, string[] Providers,
    string GoogleSubjectPrefix, string MicrosoftSubjectPrefix)
{
    public static CustomerLoginSettings? Read(IConfiguration config, string? phoneOrigin)
    {
        if (config["CustomerLogin:Enabled"] != "true") return null;
        string Required(string key) => !string.IsNullOrWhiteSpace(config["CustomerLogin:" + key])
            ? config["CustomerLogin:" + key]!.Trim() : throw new ArgumentException("CustomerLogin:" + key + " is required.");
        var authority = Required("Authority").TrimEnd('/');
        NetworkBoundary.Origin(authority, false);
        var origin = Required("Origin");
        NetworkBoundary.Origin(origin, false);
        if (origin != phoneOrigin) throw new ArgumentException("Customer login must use the configured trusted HTTPS workspace origin.");
        var providers = (config["CustomerLogin:Providers"] ?? "google").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        if (providers.Length == 0 || providers.Any(p => p is not ("google" or "microsoft"))) throw new ArgumentException("Configure the available Google/Microsoft sign-in providers.");
        return new(authority + "/", Required("ClientId"), Required("ClientSecret"), origin,
            config["CustomerLogin:OwnerSubject"], config["CustomerLogin:GoogleConnection"] ?? "google-oauth2",
            config["CustomerLogin:MicrosoftConnection"] ?? "microsoft", providers,
            config["CustomerLogin:GoogleSubjectPrefix"] ?? "google-oauth2|", config["CustomerLogin:MicrosoftSubjectPrefix"] ?? "waad|");
    }
}

public static class CustomerLogin
{
    public const string Scheme = "customer-oidc";
    public const string CallbackPath = "/signin-oidc";

    public static void LoadPrivateSettings(ConfigurationManager config, string root)
    {
        var path = Path.Combine(Path.GetFullPath(root), "customer-login.json");
        if (!File.Exists(path)) return;
        // Only this section can be imported; a login file cannot quietly change network exposure.
        var file = new ConfigurationBuilder().AddJsonFile(path, optional: false, reloadOnChange: false).Build();
        foreach (var field in file.GetSection("CustomerLogin").GetChildren())
            if (config[field.Path] == null && field.Value != null) config[field.Path] = field.Value;
    }

    public static CustomerLoginSettings? Register(WebApplicationBuilder builder, string? phoneOrigin)
    {
        var settings = CustomerLoginSettings.Read(builder.Configuration, phoneOrigin);
        if (settings == null) return null;
        builder.Services.AddSingleton(settings);
        builder.Services.AddAuthentication().AddCookie("customer-external", options =>
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        }).AddOpenIdConnect(Scheme, options =>
        {
            options.Authority = settings.Authority;
            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.SignInScheme = "customer-external";
            options.CallbackPath = CallbackPath;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            options.RequireHttpsMetadata = true;
            options.SaveTokens = false;
            options.MapInboundClaims = false;
            options.Scope.Clear();
            foreach (var scope in new[] { "openid", "profile", "email" }) options.Scope.Add(scope);
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidIssuer = settings.Authority;
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidAudience = settings.ClientId;
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = context =>
                {
                    if (context.Properties.Items.TryGetValue("connection", out var connection))
                        context.ProtocolMessage.SetParameter("connection", connection);
                    context.ProtocolMessage.Prompt = "login";
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var subject = context.Principal?.FindFirst("sub")?.Value;
                    string? provider = null;
                    context.Properties?.Items.TryGetValue("provider", out provider);
                    var prefix = provider == "google" ? settings.GoogleSubjectPrefix : settings.MicrosoftSubjectPrefix;
                    if (context.SecurityToken.Issuer != settings.Authority || string.IsNullOrWhiteSpace(subject) ||
                        !settings.Providers.Contains(provider) || string.IsNullOrEmpty(prefix) || !subject.StartsWith(prefix, StringComparison.Ordinal))
                        context.Fail("A validated workspace identity is required.");
                    return Task.CompletedTask;
                },
                OnTicketReceived = context =>
                {
                    try
                    {
                        var principal = context.Principal!;
                        var subject = principal.FindFirst("sub")!.Value;
                        var owner = !string.IsNullOrWhiteSpace(settings.OwnerSubject) && subject == settings.OwnerSubject;
                        context.HttpContext.RequestServices.GetRequiredService<Security>().IssueCustomer(context.HttpContext,
                            settings.Authority, subject, principal.FindFirst("name")?.Value ?? "Workspace member",
                            principal.FindFirst("email")?.Value, bool.TryParse(principal.FindFirst("email_verified")?.Value, out var verified) && verified, owner);
                        context.HandleResponse();
                        context.Response.Redirect("/");
                    }
                    catch (Exception error) when (error is InvalidOperationException or ArgumentException)
                    {
                        context.HandleResponse();
                        context.Response.Redirect("/#sign-in-error=account-unavailable");
                    }
                    return Task.CompletedTask;
                },
                OnRemoteFailure = context =>
                {
                    // No provider exception or credential is sent back to the browser.
                    context.HandleResponse();
                    context.Response.Redirect("/#sign-in-error=not-completed");
                    return Task.CompletedTask;
                }
            };
        });
        return settings;
    }

    public static void Map(WebApplication app, CustomerLoginSettings? settings)
    {
        app.MapGet("/api/auth/customer", () => Results.Ok(new
        {
            enabled = settings != null,
            origin = settings?.Origin,
            providers = settings?.Providers ?? Array.Empty<string>()
        }));
        app.MapGet("/api/auth/customer/login", (HttpContext context, string provider) =>
        {
            if (settings == null) return Results.NotFound();
            if (!context.Request.IsHttps || $"{context.Request.Scheme}://{context.Request.Host}" != settings.Origin)
                return Results.BadRequest(new { error = "Open the trusted HTTPS workspace address to sign in." });
            var connection = provider switch { "google" => settings.GoogleConnection, "microsoft" => settings.MicrosoftConnection, _ => null };
            if (connection == null || !settings.Providers.Contains(provider)) return Results.BadRequest(new { error = "Choose an available sign-in provider." });
            var properties = new AuthenticationProperties { RedirectUri = "/" };
            properties.Items["connection"] = connection;
            properties.Items["provider"] = provider;
            return Results.Challenge(properties, [Scheme]);
        });
        app.MapPost("/api/auth/logout", (HttpContext context, Security security) =>
        {
            security.SignOut(context, (DeviceSession)context.Items["session"]!);
            return Results.Ok();
        });
    }
}
