# Connected tools through the host broker and MCP

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

September 22 preview audience: **the owner's existing account only**, as requested.
Reuse its dedicated Google test project and saved Desktop app setup. No new test
users were added. Shared onboarding for additional invited users remains open;
the owner-only preview does not prove that wider setup. Do not copy owner account
tokens, client secrets or credential-vault entries into the package.

Current primary-source check: Google's [installed-app OAuth guide](https://developers.google.com/identity/protocols/oauth2/native-app)
lists the client secret as optional for code exchange and refresh, and supports
PKCE. However, a September 22 negative-control request using this actual Desktop
client ID, a fictional invalid code and no secret was rejected with HTTP 400:
`invalid_request`, `client_secret is missing.` Receipt:
`artifacts/google-preview-20260922/public-client-preflight.json`. No user
authorization or secret was sent. This disproves the proposed secret-free setup
for this client; it is not a successful sign-in check. The implementation keeps
the existing vault-backed import and PKCE flow. Google's [OAuth policy](https://developers.google.com/identity/protocols/oauth2/policies)
requires secure credential handling and prohibits public-repository credential
commits. The [Testing audience rules](https://support.google.com/cloud/answer/15549945?hl=en)
limit the configured test audience to 100 users and expire test authorizations,
including offline refresh tokens, after seven days for these workflows. Public
verification and publication remain separate from an invited tester preview.

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

An explicit Connect request starts Google's maintained PKCE authorization flow.
The host then verifies the selected Gmail or Calendar API before registering its
bounded built-in tools; a catalogue response alone cannot establish a signed-in
account. The callback is bound to short-lived, single-use state and a checked
Google issuer before code exchange.
The Google library keeps no file token store; reusable authorization is committed
only through the existing host credential vault. The desktop loopback callback
for the default host is:

```text
http://127.0.0.1:5179/api/settings/mcp/google/callback
```

Desktop clients do not add that loopback URI to a Web-client redirect list. In
the same Google Cloud project as the Desktop client, enable the selected product
API:

| Connection | Product API |
|---|---|
| Gmail | Gmail API (`gmail.googleapis.com`) |
| Calendar | Google Calendar API (`calendar-json.googleapis.com`) |

Configure Google Auth Platform's Branding, Audience (including the approved test
account when External/Testing), and Data Access. See Google's
[installed-app OAuth](https://developers.google.com/identity/protocols/oauth2/native-app),
[Gmail API](https://developers.google.com/workspace/gmail/api/reference/rest), and
[Calendar API](https://developers.google.com/workspace/calendar/api/v3/reference)
guides, checked September 17, 2026. This Windows deployment uses a Desktop client
and its loopback callback. Import the downloaded Desktop credentials only through
the dedicated app-setup control, never through chat or artifact uploads.

Google separates local developer/test-user success from public
OAuth availability; sensitive or restricted scopes can require verification.
The product requests only the selected workflow scopes plus `openid email` so the
connected account can be displayed. Gmail reading and sending are separate
connections: a read-only inbox watch never asks for sending permission, and a
send-only connection never asks for mailbox-reading permission.

- Gmail — Read mail: `gmail.readonly`.
- Gmail — Send approved messages: `gmail.send`.
- Calendar: read calendar lists, events, and free/busy information.

The built-in Google connectors are narrow host-side adapters backed by Google's
stable REST APIs. Gmail read exposes bounded message search and exact-message read;
Calendar exposes calendar listing, bounded event reads, exact-event reads and
free/busy queries. They do not mutate either service. Gmail send remains a separate
`gmail.messages.send` capability backed by Gmail's documented
`users.messages.send` endpoint. It accepts exactly one recipient, subject, and
plain-text body. A successful receipt records Gmail's message ID and provider
acceptance separately from recipient delivery; creating a draft is never treated
as sending. Existing Google connections created by an earlier build are presented
through these stable built-in tools after restart, while their stored account grant
remains in the operating-system credential store.

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

Gmail message search returns message IDs plus normalized sender, subject, precise
provider timestamp, snippet and an original-message link. Inbox watches assess at
most 20 new messages per check and bind every read to the persisted activation or
progress timestamp. Another page or a full requested batch pauses without
advancing progress; narrow the reviewed selection before retrying an overflow.
The watch never crawls a mailbox. Only explicit empty collections count as a quiet
successful check.
Empty checks use no model. Assessments retain reported input/output tokens or
explicitly unknown usage in their receipt, including assessment failures. Existing
one-call/output limits remain; unknown provider usage is not certified zero cost.

Supported response fixtures cover the stable Gmail message envelope and Microsoft
Graph's nested sender and original-message link. They do not prove a live account,
recipient delivery, correct-account message link, or qualify every generic MCP mail
tool; those require controlled live acceptance.

The server's MCP annotations inform the displayed effect (`read external data`,
`write or external action`, or `potentially destructive external action`), but do
not weaken review. Interactive calls, including reads, require approval. A recurring
brief can reuse only the bounded email/calendar read scope, time window, selection
rule, occurrence count, and expiry from its saved grant; changing those terms needs
a new review. Tool results do not become instructions and cannot expand the frozen
catalog.

Focused CPU-only verification is:

```powershell
dotnet test tests/Thaddeus.Tests/Thaddeus.Tests.csproj --filter "FullyQualifiedName~ConnectedToolConversationTests|FullyQualifiedName~McpDelegationConnectionTests|FullyQualifiedName~GoogleGmailApiTests|FullyQualifiedName~GoogleWorkspaceReadApiTests"
npm --prefix web run build
```
