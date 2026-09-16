# Website reading in Chat

Paste a public HTTPS article URL into Chat and ask Thaddeus to read or discuss it.
Feed's **Discuss** prepares that request in the composer; sending it starts the read.
The reply includes a **Read N sources** disclosure with the source link, retrieval
time and an excerpt label when truncated. The log records the request, tool result,
read failures and model usage. A refused or failed read is not page evidence.

## Runtime boundary

Ordinary Chat still uses the selected compatible model provider. It now advertises
`thaddeus_fetch_public_page` for up to three exact HTTPS URLs supplied in the current
message. The model requests a read, the host capability broker performs it, and a
following model call receives the recorded result through the function/tool message
protocol. Artifact tools remain available. A linked page does not grant permission
to read other pages, local files, private addresses or URLs found in source text.

Isolated OpenClaw research accesses the same capability broker over the existing
authenticated MCP endpoint, `/worker/{runId}/mcp`. Chat invokes the broker directly
under its existing run lock; it does not start a VM or impersonate a worker.
The MCP SDK contract tests negotiate a real session, list tools, read a scoped page,
refuse an ungranted host and replay a recorded operation without fetching twice.
This does not install arbitrary third-party MCP connectors or enable browser control.

The reader checks public DNS addresses and dials the checked address. It uses HTTPS,
no cookies, no provider credentials, a 15-second retrieval deadline, a 1 MB decoded
response limit and a 12,000-character text limit. Redirects stay on the original
granted hostname. JavaScript-only, signed-in, paywalled and non-text pages can fail;
the app reports that rather than claiming to have read them. Extracted page text is
untrusted material and its claims are not independently verified facts.

## Budget

The default two-model-call reply permits one page read followed by an answer.
The tool must fit the task's tool budget and leave a model call for the answer.
The usual aggregate token allowance, usage counter, cancellation and background
handoff still apply. A known-URL read spends **no Brave search request**. This is
separate from web search and does not change the saved monthly search limit.

## Verification

Routine checks use fictional providers and pages, without external model calls:

```powershell
dotnet test tests/Thaddeus.Tests/Thaddeus.Tests.csproj --filter 'FullyQualifiedName~ConversationWebTests|FullyQualifiedName~WorkerMcpTests|FullyQualifiedName~PublicWebTests|FullyQualifiedName~ProviderTests|FullyQualifiedName~ConversationTests'
```

`web/tests/chat-web-live.spec.ts` is skipped unless explicitly enabled for a user
authorized acceptance check. It uses a disposable packaged study and a loopback
Luna High provider bridge. It records source hashes, answer, model/tool counts and
token usage, then checks the source disclosure on desktop and mobile. It does not
use the real study or search API. Set `THADDEUS_WEB_ACCEPTANCE_PROVIDER` only for
that invocation of `scripts/browser-check.mjs`, and unset it afterward. Remove the
fixture study after its owned host exits; retain the small acceptance receipt.

## September 15 acceptance

The Windows development package `portable-chat-web-20260915-a` read the originally
reported Hugging Face article with Luna High: two model calls, one page fetch,
36,507 input tokens and 295 output tokens reported; no Brave search or GPU use.
The answer cited a specific consistency result from the retrieved excerpt and
linked to the article. Evidence is in
`artifacts/chat-web-live-check-20260915-a/screenshots/live-acceptance.json`.
The live harness subsequently waited for a background-only task button although
the reply had completed in the foreground. That selector was corrected; the live
model calls were not repeated. A separate packaged UI check passed source links,
excerpt/error disclosures, log details, mobile layout and Feed's reading draft.

The focused backend set passed 124 checks, including MCP SDK transport and page
reading. All 29 artifact regression cases also passed after replacing an obsolete
export-schema assertion (7) with the current store schema (8). TypeScript passed.
The live study upgrade preserved every existing table, the owner key and session;
schema remains 8. This verifies the broker and Chat change, not a new VM image or
arbitrary external MCP integrations. Disposable studies and build intermediates
were removed; compact receipts remain under `artifacts/chat-web-20260915`.
