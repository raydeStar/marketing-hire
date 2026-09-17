using System.Security.Cryptography;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Http;
using Microsoft.AspNetCore.WebUtilities;
using ModelContextProtocol.Authentication;

namespace Thaddeus.Host;

public sealed partial class McpConnections
{
    private async Task<TokenContainer> AuthorizeGoogle(OAuthAttempt attempt, CancellationToken cancellation)
    {
        using var flow = new PkceGoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = attempt.ClientId, ClientSecret = attempt.ClientSecret },
            Scopes = attempt.Product.Scopes,
            Prompt = "consent",
            IncludeGrantedScopes = false,
            DataStore = null, // Only CommitOAuth may put reusable authorization in the host vault.
            HttpClientFactory = new GoogleSignInHttpFactory(handlerFactory)
        });
        var request = flow.CreateAuthorizationCodeRequest(oauthRedirect.AbsoluteUri, out var verifier);
        attempt.State = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        request.State = attempt.State;
        var authorization = request.Build();
        attempt.Phase = "awaiting-google";
        var browserOpened = OpenGoogleBrowser(authorization, openGoogleBrowser);
        attempt.Ready.TrySetResult(new { attemptId = attempt.Id, authorizationUrl = authorization.AbsoluteUri, redirectUri = oauthRedirect.AbsoluteUri, browserOpened });
        using var registration = cancellation.Register(() => attempt.Callback.TrySetCanceled(cancellation));
        var callback = await attempt.Callback.Task;
        if (!Fixed(attempt.State, callback.State))
            throw new InvalidOperationException("Google sign-in did not match this request. Start it again.");
        var token = await flow.ExchangeCodeForTokenAsync(attempt.Id, callback.Code, verifier, oauthRedirect.AbsoluteUri, cancellation);
        if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken) ||
            !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) || token.ExpiresInSeconds is not (>= 60 and <= 86400))
            throw new InvalidOperationException("Google did not provide reusable authorization. Reconnect and approve access on Google's consent screen.");
        return new TokenContainer
        {
            AccessToken = token.AccessToken, RefreshToken = token.RefreshToken, TokenType = token.TokenType,
            Scope = token.Scope, ExpiresIn = (int)token.ExpiresInSeconds.Value, ObtainedAt = DateTimeOffset.UtcNow,
            ClientId = attempt.ClientId, ClientSecret = attempt.ClientSecret,
            AuthorizationServer = "https://accounts.google.com", TokenEndpointAuthMethod = "client_secret_post"
        };
    }

    private sealed class GoogleSignInHttpFactory(Func<HttpMessageHandler>? factory) : Google.Apis.Http.HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) =>
            factory?.Invoke() ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
    }
}
