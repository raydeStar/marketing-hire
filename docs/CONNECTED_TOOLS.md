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
MCP servers. The owner creates one Web application OAuth client in a Google Cloud
project, enters its client ID and secret on the host, and chooses **Continue with
Google**. Thaddeus opens the Google consent page in a separate window and waits in
the background. The callback is bound to a short-lived, single-use OAuth state;
the MCP SDK also applies PKCE and authorization-server checks before exchanging the
code.

The Google Cloud OAuth client must list the exact redirect URI shown in Settings.
For the default host it is:

```text
http://localhost:5179/api/settings/mcp/google/callback
```

Enable the selected product's API and MCP service and configure its consent screen
before connecting. Google's Workspace MCP services are currently a Developer
Preview, so availability and Cloud-console labels may change. The current product
presets deliberately request only Google's documented scopes:

- Gmail: read mail and compose drafts.
- Calendar: read calendar lists, events, and free/busy information.

The OAuth client secret and refresh credential are stored through the operating
system credential store. Access tokens live in host memory. They are never written
to SQLite, exports, receipts, model prompts, or tool results. A restart uses the
stored refresh credential; if Google requires fresh consent, Settings asks the
owner to reconnect rather than opening an authorization page from an agent task.
Removing the connector removes both stored OAuth records.

The generic form remains available for any remote Streamable HTTP MCP server that
uses no credential or a fixed bearer token. Locally executed `stdio` MCP packages
are outside this preview.

Each ordinary chat turn freezes the currently available tool catalog. Luna receives
only safe aliases, descriptions and JSON input schemas. If it proposes a tool, the
run stops before network dispatch and displays the exact connector, remote tool,
effect classification and JSON arguments. Denial sends nothing. Approval records
the decision, rechecks the connector and catalog version, performs one MCP request,
and returns the result as untrusted tool content for the final model reply. Calls
are never retried automatically.

The server's MCP annotations inform the displayed effect (`read external data`,
`write or external action`, or `potentially destructive external action`), but do
not weaken review. Every call, including reads, requires approval. Tool results do
not become instructions and cannot expand the turn's frozen catalog.

Focused CPU-only verification is:

```powershell
dotnet test tests/Thaddeus.Tests/Thaddeus.Tests.csproj --filter "FullyQualifiedName~ConnectedToolConversationTests|FullyQualifiedName~ProviderTests.Conversation_AdvertisesOnlyFrozenConnectedTools"
npm --prefix web run build
```
