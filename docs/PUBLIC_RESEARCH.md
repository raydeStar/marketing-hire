# Public research broker checkpoint

The shared runtime now offers `thaddeus_fetch_public_page` to isolated research
tasks with an explicit `PublicWebScope`. Existing conversations and plans receive
no web grant. The MCP tool list is task-specific; calling an unadvertised web tool
also fails at the broker. The production research-admission UI is still open work.

A grant contains one to eight exact DNS hostnames and an allowance of one to eight
fetches. Hostnames do not imply their subdomains. The worker cannot expand the
grant through tool arguments or experimental policy. Scope and allowance are part
of the durable execution-command fingerprint and prepared task context.

## Retrieval boundary

- HTTPS on the default port, with no URL credentials. Every redirect must pass
  the same task-host check. At most two redirects follow an initial request.
- DNS is resolved at connection time. Every returned address must be eligible
  public IPv4 or IPv6; mixed public/private answers fail. The socket receives the
  validated IP directly, avoiding a second DNS resolution. TLS remains responsible
  for validating the original hostname. IPv4 special-purpose/multicast ranges and
  IPv6 local, mapped, translation and selected special-purpose ranges are refused.
- No ambient proxy, cookies, HTTP authentication, provider key, referrer, browser
  profile, scripts or subresource loading. HTTP/1.1 is explicit; redirects are
  handled individually by the broker.
- A retrieval has 15 seconds within the task's remaining active time, at most
  1,000,000 decoded response bytes, and at most 12,000 returned characters. UTF-8
  HTML, Markdown and plain text are supported. Truncation is explicit.
- AngleSharp 1.8.1 parses HTML locally without a loader or script engine. Active
  elements and common navigation/hidden sections are excluded. The returned text
  is an extraction, not a rendered-page or factual-accuracy guarantee.

These are application-level outbound controls. Qualification of the VM and its
broker-only network path is still required. This capability does not make an
unrestricted container or host agent an approved production backend.

## Receipts and interruption

The host charges the capability and saves retrieval intent before dispatch. A
successful receipt contains the final URL, retrieval time, content type, response
byte count, decoded-body SHA-256, returned text SHA-256, truncation flag, and each
attempted HTTP hop with any observed status. Page assertions remain
`untrusted-source-data`; the receipt's authority is `broker-observed`.

Reusing the operation ID returns the saved result without another request. Changed
arguments under the same ID are refused. If the host stops after intent but before
the result commits, the saved operation remains an error with an unknown outcome;
it is not automatically fetched again. Failed attempts retain their tool charge
and consume the task's bounded fetch allowance.

The reader currently follows only URLs on granted hosts. Search-provider
integration, user-facing grant selection, additional-host approval, richer source
navigation, independent factual checks and benchmark comparisons remain open.

## Current evidence

The backend suite has 186 passing tests. New cases cover address classifications,
mixed DNS answers, direct-IP dialing without a second lookup, exact host scope,
redirects, unknown-length oversized streams, MIME restrictions, explicit
truncation, cancellation, tool exposure, durable replay, and interrupted intent.
The official MCP and existing history/migration tests also pass. NuGet's current
advisory check reported no vulnerable direct or transitive packages.
The self-contained Windows package at `artifacts/dev-host-20260912-public`, built
from `eb8b34c`, passed a separate fictional plan/approval/import/export smoke and
included the parser's license notice. The main development host remains on the
previous control checkpoint; production research admission is still disabled.

The native check has a separate mode with real public HTTP and scripted inference:

```powershell
dotnet restore --locked-mode
dotnet build tools/Thaddeus.NativeCheck --no-restore
docker build --pull=false -t thaddeus-openclaw:2026.9.4-context-dev workers/openclaw
node scripts/native-integration-check.mjs scripted-web
```

`artifacts/native-integration-scripted-web-1789250839574` passed the selected-note
read, public fetch, durable question, confirmed Gateway process restart,
continuation, artifact proposal and exact approved import. The worker retained
`--network none`; the real HTTP request ran in the external broker. The test-only
stdio relay is not a qualified production network path.

The granted Docker FAQ returned HTTP 200 in one request with 9,529 decoded bytes
and 9,519 characters, served as Markdown without truncation. Its decoded-body hash
was `cc3436f6f12af56e8aae34b77d4e1c36ad731ec618df0dcbf42bd9a4bf877024`.
The complete returned text and source URL matched the next native model request,
not just its checksum. Five model responses used explicitly synthetic accounting;
no live inference, GPU use, factual-validation gain or performance gain is claimed.

References: [OWASP SSRF prevention](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html),
[.NET connection callback](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.connectcallback?view=net-10.0),
[IANA IPv4 registry](https://www.iana.org/assignments/iana-ipv4-special-registry),
[IANA IPv6 registry](https://www.iana.org/assignments/iana-ipv6-special-registry),
[AngleSharp](https://github.com/AngleSharp/AngleSharp).
