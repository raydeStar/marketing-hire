# Connected tools through MCP

The host owner can register a remote Streamable HTTP MCP server in **Settings →
Connections → Connect tools with MCP**. Registration performs one server-side
handshake, discovers up to 32 tools, and saves a bounded catalog. Remote endpoints
must use HTTPS; loopback development endpoints may use HTTP. Redirects, cookies,
system proxy settings, URL credentials, query strings and fragments are refused.

Bearer tokens are saved through the operating system credential store or retained
only in host memory. The SQLite registry contains endpoint and tool metadata plus a
credential reference; chat observations, receipts and exports do not contain the
token.

## Google Workspace OAuth

Ask chat to connect Gmail or Calendar, then choose **Continue with Google** in the
card inside Thaddeus's reply. It scrolls with the conversation and leaves the
message box available. Closing the card keeps a **Continue connection setup**
button on that reply so it can be reopened. With app setup saved, no client-ID or secret fields are shown:
the host supplies the saved registration and opens Google's consent page in the
default system browser. Choose an account, approve the selected permissions, then
return to Thaddeus. The card waits for the result and Settings shows the connected
account and actual permissions. If the browser cannot launch, the card offers an
explicit Google sign-in link. Consent is never hosted inside a Thaddeus webview.

The person configuring this installed build must first register a **Desktop app**
OAuth client in Google Cloud. Download its credentials JSON, expand **App setup ·
one time**, and choose **Import Google setup file**. This dedicated, local-owner
form sends the file directly to the host; it is not a chat attachment. Only its
client ID and secret are saved in the operating-system credential store. Imported
endpoints and redirect URLs cannot change OAuth destinations. Web-app and service
account files are refused. No individual fields need to be copied. The saved
registration is reused across Gmail read, Gmail send, Calendar, and host restarts.
**Remove saved app setup** removes that reusable registration without disconnecting
existing accounts; use each connection's Disconnect action to revoke host access.

This removes repeated setup, not Google's one-time developer registration or
verification requirements. An unconfigured installation says setup is needed and
cannot start consent. No production Thaddeus client is bundled yet. Public end
users should receive a build with an appropriately registered app identity; a
successful test-user connection alone does not make that app publicly available.
Installed-app clients are public clients: their client secret cannot be treated as
a confidential server secret. User refresh credentials still remain host-only.

An explicit Connect request starts Google's maintained PKCE authorization flow
before MCP discovery. Some Google MCP catalogue operations allow anonymous reads;
a tool list alone cannot establish a signed-in account. The callback is bound to
short-lived, single-use state and a checked Google issuer before code exchange.
The Google library keeps no file token store; reusable authorization is committed
only through the existing host credential vault. The desktop loopback callback
for the default host is:

```text
http://127.0.0.1:5179/api/settings/mcp/google/callback
```

Desktop clients do not add that loopback URI to a Web-client redirect list. In
the same Google Cloud project as the Desktop client, enable both the selected
product API and its MCP service:

| Connection | Product API | MCP service |
|---|---|---|
| Gmail | `gmail.googleapis.com` | `gmailmcp.googleapis.com` |
| Calendar | `calendar-json.googleapis.com` | `calendarmcp.googleapis.com` |

Configure Google Auth Platform's Branding, Audience (including the approved test
account when External/Testing), and Data Access. Join the Workspace Developer
Preview for these MCP services. Enabling only the ordinary Gmail or Calendar API
does not complete MCP setup. See Google's [Gmail setup](https://developers.google.com/workspace/gmail/api/guides/configure-mcp-server),
[Calendar setup](https://developers.google.com/workspace/calendar/api/guides/configure-mcp-server),
and [installed-app OAuth](https://developers.google.com/identity/protocols/oauth2/native-app)
guides, checked September 17, 2026. The examples for hosted clients in the MCP
guides use Web clients; this Windows deployment uses a Desktop client and its
loopback callback. Import the downloaded Desktop credentials only through the
dedicated app-setup control, never through chat or artifact uploads.

Google separates local developer/test-user success from public
OAuth availability; sensitive or restricted scopes can require verification.
The product requests only the selected workflow scopes plus `openid email` so the
connected account can be displayed. Gmail reading and sending are separate
connections: a read-only inbox watch never asks for sending permission, and a
send-only connection never asks for mailbox-reading permission.

- Gmail — Read mail: `gmail.readonly`.
- Gmail — Send approved messages: `gmail.send`.
- Calendar: read calendar lists, events, and free/busy information.

Google's Gmail MCP server is used only for its advertised read tools. Thaddeus adds
one narrow host-side `gmail.messages.send` capability backed by Gmail's documented
`users.messages.send` endpoint. It accepts exactly one recipient, subject, and
plain-text body. A successful receipt records Gmail's message ID and provider
acceptance separately from recipient delivery; creating a draft is never treated as
sending.

The OAuth client secret and refresh credential are stored through the operating
system credential store. Access tokens live in host memory. They are never written
to SQLite, exports, receipts, model prompts, or tool results. A restart uses the
stored refresh credential in the host. Denied or partial consent fails connection;
revocation or expiry before dispatch stops the action before Gmail is contacted.
Settings shows the account, granted permissions, and connection status.
The permission receipt retains all scopes actually returned by Google, including
any additional grant; it does not silently display only the requested subset.
The selected workflow's tool filter and exact action approvals remain enforced.

Disconnect removes both stored OAuth records immediately and blocks future scheduled dispatch;
reconnecting creates a new connection version, so old approval cannot silently
inherit it.

The generic form remains available for any remote Streamable HTTP MCP server that
uses no credential or a fixed bearer token. Locally executed `stdio` MCP packages
and arbitrary third-party OAuth flows are outside this preview. “Connect a
service” therefore means a compatible remote Streamable HTTP server with one of
those supported authentication modes; it is not a claim that every MCP server or
account can be connected without provider-specific setup.

Each ordinary chat turn freezes the currently available tool catalog. Luna receives
only safe aliases, descriptions and JSON input schemas. If it proposes a tool, the
run stops before network dispatch and displays the exact connector, remote tool,
effect classification and JSON arguments. Denial sends nothing. Approval records
the decision, rechecks the connector and catalog version, performs one reviewed
request, and returns the result as untrusted tool content for the final model reply.
Calls are never retried automatically.

Google's Gmail MCP server remains a Developer Preview service. Its current
`search_threads` response is paginated and returns thread summaries containing
messages. Inbox watches assess at most 20 new messages per check. Every new
message in a thread is considered, provided timestamps can distinguish mail from
before activation. Multi-message threads with only day-level or missing dates
pause visibly: the host cannot safely guess which messages are new. Direct
message searches rely on their approved activation-time filter.

Another page, a full requested batch without a continuation marker, ID-only or
partly unreadable results pause without advancing progress. Narrow the reviewed
selection before retrying an overflow; this preview does not crawl a mailbox.
Only explicit supported empty collections count as a quiet successful check.
Empty checks use no model. Assessments retain reported input/output tokens or
explicitly unknown usage in their receipt, including assessment failures. Existing
one-call/output limits remain; unknown provider usage is not certified zero cost.

Supported response fixtures cover Gmail thread envelopes and Microsoft Graph's
nested sender and original-message link. They do not prove a live connection or
qualify every advertised mail tool. In particular, Gmail MCP timestamp precision,
preview access and the correct-account message link still require live acceptance.

The server's MCP annotations inform the displayed effect (`read external data`,
`write or external action`, or `potentially destructive external action`), but do
not weaken review. Interactive calls, including reads, require approval. A recurring
brief can reuse only the bounded email/calendar read scope, time window, selection
rule, occurrence count, and expiry from its saved grant; changing those terms needs
a new review. Tool results do not become instructions and cannot expand the frozen
catalog.

Focused CPU-only verification is:

```powershell
dotnet test tests/Thaddeus.Tests/Thaddeus.Tests.csproj --filter "FullyQualifiedName~ConnectedToolConversationTests|FullyQualifiedName~McpDelegationConnectionTests|FullyQualifiedName~GoogleGmailApiTests"
npm --prefix web run build
```
