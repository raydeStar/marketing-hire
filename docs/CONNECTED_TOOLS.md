# Connected tools through MCP

The host owner can register a remote Streamable HTTP MCP server in **Settings →
Connections → Connect tools with MCP**. Registration performs one server-side
handshake, discovers up to 32 tools, and saves a bounded catalog. Remote endpoints
must use HTTPS; loopback development endpoints may use HTTP. Redirects, cookies,
system proxy settings, URL credentials, query strings and fragments are refused.

Bearer tokens are saved through the operating system credential store or retained
only in host memory. The SQLite registry contains endpoint and tool metadata plus a
credential reference; chat observations, receipts and exports do not contain the
token. OAuth and locally executed `stdio` MCP packages are not in this first slice.

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
