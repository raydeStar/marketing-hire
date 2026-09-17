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

Settings contains first-class connections for Google's official Gmail and Calendar
MCP servers. For this installed Windows build, the owner creates a **Desktop app**
OAuth client in a Google Cloud project, enters its client ID and secret on the host,
and chooses **Continue with Google**. Thaddeus opens Google's consent page in the
browser and waits in the background. The callback is bound to a short-lived,
single-use state; the maintained MCP OAuth client applies PKCE and validates the
authorization response before exchanging the code. The desktop loopback callback
for the default host is:

```text
http://127.0.0.1:5179/api/settings/mcp/google/callback
```

Desktop clients do not add that loopback URI to a Web-client redirect list. Enable
the selected product API, configure the Google Auth Platform consent screen,
audience, and test users, and join the Workspace Developer Preview when the MCP
server requires it. Google separates local developer/test-user success from public
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
messages. Inbox watches assess at most 20 messages per check, use the newest
message in a matching thread, and pause visibly rather than advancing their
cursor when a provider returns another page or an unsupported response shape.

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
