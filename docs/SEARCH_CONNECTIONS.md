# Public search

Research can optionally discover public sources through Brave Search. Ordinary
chat and the sidebar Search destination do not use this connection; the sidebar
searches this study's saved content. Public search still requires an enabled
isolated research worker and a separately configured model.

## Connect on the host

1. Open Settings and find **Connect public search**.
2. Enter a Brave Search API key and choose the operating system's credential
   store for persistence, or memory only until this host stops.
3. Confirm that your provider plan permits keeping API results in task history.
   Thaddeus retains queries and result receipts for replay. Brave requires a plan
   explicitly granting storage rights for retained API results; check the current
   [provider plans and FAQ](https://brave.com/search/api/).
4. Save the connection. **Check saved search key** confirms that the host can
   retrieve it. Neither operation sends a query or verifies provider acceptance,
   quota, billing or result quality.

Keys are stored separately from model credentials, outside the worker and study
data. They are absent from exports and backups. Native storage is Windows
Credential Manager, macOS Keychain or Linux Secret Service. Linux needs a working
desktop secret service. Session keys disappear when the host process stops;
choose native storage for tasks that need to continue across host restarts.
An existing task keeps its original credential reference. Saving another key
does not silently replace the key used by that task. Restoring an older backup
does not restore a removed operating-system credential.

## Allow search for a task

In the conversation composer, choose Research and enable **Search the public web
with Brave**. Select a one-to-four request allowance. **Allow opening the returned
result pages** is a separate permission. Without it, the worker can read search
snippets but can fetch only explicitly listed source websites.

Queries may include details from your question and selected context. The model
is instructed to keep private or unrelated details out of queries, but this is
not a guarantee that query text is free of sensitive information. Enable public
search only for context you are willing to include in a provider query.

Search is off by default. A saved connection does not enable it for other tasks.
At most five result links are returned per query. Opening results permits the
exact recorded URLs, with redirects restricted to the same hostname; it does
not grant arbitrary paths, subdomains or general network access. The usual
public-address, HTTPS, redirect, response-size and page-fetch limits still apply.

## Usage and results

Settings and the research composer show a **monthly search allowance** for this
study. It defaults to 100 requests and can be changed from 0 to 100,000 in Settings
on the host. Zero pauses new searches. The host checks this limit across tasks
and commits the reservation before provider dispatch; concurrent tasks cannot
spend the same last slot. Failures and unknown outcomes remain counted. A denied
dispatch does not consume a monthly slot. Changing the limit or saved key does
not clear usage, and replaying a recorded operation sends no new request.

The count includes historical search intents in the full ledger and survives
restart. It renews on the first of each calendar month at 00:00 UTC. This is a
study allowance, not a Brave account/billing counter. Other apps, separate studies
and restoring older study backups can make actual account usage higher. Set
provider-side spending controls as well; the local cap cannot guarantee free use.
There is no billing integration or cross-task result cache in this MVP.

The token summary also shows search attempts. Task details retain each query,
result link, plain-text snippet and broker receipt. Attempt counts consume the
task allowance, including failures and unknown outcomes; they are not a provider
invoice or a measurement of billable requests. Search charges are separate from
model tokens. Dollar spend and account-wide quota are not currently measured.

The host records intent before dispatch. Reusing an operation ID returns its
recorded result; it does not search again. An interrupted request with an unknown
outcome is not retried automatically. Search snippets are discovery hints, not
captured quotation evidence. A separately recorded public-page fetch is required
before a quotation from that page can pass source checks. Matching a quotation
does not independently establish that a source's claims or the conclusion are true.

The key is sent only by the host to the fixed
[Brave Web Search endpoint](https://api-dashboard.search.brave.com/app/documentation/web-search/get-started).
The worker receives the task-scoped MCP tool `thaddeus_search_public_web`;
OpenClaw's built-in web provider remains disabled. Worker packages without this
tool stop search-enabled tasks before Gateway startup. Existing non-search work
retains its earlier behavior.

## Verification boundary

The implementation has automated broker, credential and browser checks. Native
integration checks substitute the search provider's HTTP response and model
responses while exercising the actual OpenClaw worker and public-page fetch.
These checks do not establish real Brave account acceptance or search quality.
A live provider check requires a configured key and an explicitly enabled task.
Routine checks use fictional credentials and substituted provider HTTP/model
responses; they consume no Brave search quota. Selected-source research can use
notes and supplied public links with search disabled. Model costs are separate.
