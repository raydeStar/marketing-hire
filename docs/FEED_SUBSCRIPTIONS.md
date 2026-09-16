# Subscriptions and saved reading

Feed combines updates from explicitly chosen sources with the existing saved-link
collection. **Add a source** accepts a public HTTPS RSS 2.0 or Atom 1.0 address,
or a UTF-8 website that advertises one. Preview reads the entered address; choosing
a discovered feed makes a separate request. Subscribe authorizes ongoing checks
of that exact source host. No default subscriptions are installed.

An empty Feed offers one-click sources for AI/open models (Hugging Face),
software development (GitHub), international reporting (BBC World, The Guardian,
Al Jazeera), US reporting (PBS News), and science (NASA, ScienceDaily). These are
publisher choices, not endorsements of individual stories or inferred beliefs.
Following starts the ordinary RSS refresh workflow; Find sources filters these
choices by topic and keeps them available later. The UI
distinguishes an initial check, a failed source, paused sources and read updates.
It displays 20 articles at a time, with Show more for the remaining entries.

The September 15 empty-feed repair was verified against the live study: it had
zero subscriptions, then loaded 110 real entries after connecting the GitHub and
Hugging Face feeds. No model or Brave request was needed. That setup was specific
to the owner's request; new studies still choose whether to follow either source.
The source-based reader now ranks articles locally; it does not generate an AI briefing.

## Personal reading order

Feed offers **For you**, **Latest**, and broad article topic filters. Both orders
group entries by the reader's local calendar day, newest day first. Latest sorts
strictly by publication time (receipt time when absent); future timestamps are
clamped to now for ranking. The original source date remains visible.

For you uses an intentionally small content-based ranker in `web/src/feed-ranking.ts`:

- Broad topic hints come from bounded headline/excerpt keyword matches, then a
  known source topic or General. They are approximate, not semantic understanding.
- One article contributes its strongest implicit action: open 0.25, Discuss 1,
  save 1.5. Explicit more/less overrides that article with +3/-3. Repeated clicks
  do not accumulate points. Marking read and merely viewing a card add nothing.
- Feedback weight halves every 30 days and stops contributing after 90. It is
  stored on the existing bounded feed entries (100/source, 20 sources); rotation
  or source removal also forgets that entry's learning. No unlimited click log.
- Within each day, score is recency (up to 2), topic affinity (up to +/-1.5) and
  publisher affinity (up to +/-0.5). Each affinity saturates at six weighted
  points. A story marked less gets an additional -6, and remains accessible.
- Reranking favors no more than two articles from one source host in a rolling
  five when same-day alternatives exist. Every fifth slot favors an as-yet-unseen
  topic if available. This encourages variety, but is not an ideological balance
  guarantee. New users start with recency and publisher variety.

**Why this story?** explains ordering and provides reversible More/Less feedback.
**Your feed preferences** shows broad learned topics, disables learning, and
resets it. Disable clears stored feedback and uses Latest. Reset preserves read
state, subscriptions, and saved links. Preference versions reject delayed requests
from before reset/disable. Feedback is synchronized across paired clients in the
local study and included in export/backup; personal-data deletion clears it.
There is no third-party interest profile, cross-site reading timer, model call,
GPU computation, or Brave search in ranking or feedback. Opening a publisher
still makes the browser's ordinary visit to that publisher.

The implementation follows the general [content-based filtering approach](https://developers.google.com/machine-learning/recommendation/content-based/basics).
Large collaborative recommenders such as [implicit](https://github.com/benfred/implicit)
or [Gorse](https://github.com/gorse-io/gorse) are options for a future multi-user
service; this personal study has no cross-user dataset to justify that machinery.

Publisher availability was checked using the actual bounded Feed preview API;
all six new catalog feeds returned entries. East Idaho News returned 403 and is
excluded from the catalog. No subscriptions were created by that preview.
Receipts: `artifacts/feed-personal-20260915/source-preview.json`. Publisher feed
availability can change. Catalog links are for personal reading; broader hosted
redistribution needs publisher-specific terms reviewed before launch.

September 15 delivery: 36 focused backend tests and four packaged browser/ranking
checks passed. The browser proof covers desktop/mobile, actual link-click feedback
against a fictional publisher, opt-out/reset, source/topic filters, ranking decay
and variety, and existing subscribe/save/read/remove flows. Fixtures make no live
search or model calls. Evidence is in `artifacts/feed-personal-check-20260915-b`
and the final focused click check in `artifacts/feed-personal-check-20260915-c`.
The live package is `artifacts/portable-feed-personal-20260915-a/thaddeus-win-x64`;
activation preserved schema 8 and all existing study tables. BBC World, PBS News,
and NASA were then connected for the owner alongside the existing two sources.
This does not install default subscriptions for new users. Activation, live
counts and bounded test cleanup receipts live in `artifacts/feed-personal-20260915`.

Focused browser proof is in `artifacts/feed-start-check-20260915-a/verified.json`
(two tests: starter/empty-state behavior and the existing feed workflow). Those
UI fixtures substitute feed responses and check that the exported study is
unchanged. Live source fetches separately prove the actual reader brought in
articles. Activation, source preservation and cleanup receipts are in
`artifacts/feed-start-20260915/`; the current package is
`artifacts/portable-feed-start-20260915-a/thaddeus-win-x64`.

The Updates view supports source and unread filters, read/unread state, pause,
resume, manual refresh and source removal. Saving an entry copies its title,
plain-text excerpt and link into Saved links. That copy survives entry rotation
and subscription removal; subsequent feed changes do not overwrite edited notes.
Discuss opens an unsent conversation draft. Viewing, fetching, marking and saving
never dispatch a model, create an agent task or consume model tokens.

## Refresh and retention

- Up to 20 subscriptions, with at most 100 retained entries per source.
- Downloads are capped at 1,000,000 decoded bytes and 15 seconds, with at most
  two redirects inside the originally selected host. One request runs at a time
  across previews and background refreshes; the pump checks for due work every
  five seconds while the host is awake. Closing the host cancels and awaits it.
- Checks normally occur hourly. RSS TTL, HTTP cache lifetime and Retry-After can
  extend the delay, bounded to 24 hours. Failures back off and preserve existing
  updates. Manual checks have a five-minute minimum and cannot bypass failure
  backoff. The browser shows the next check and latest failure.
- ETag and Last-Modified validators apply only to the representation URL that
  supplied them. A valid 304 preserves entries. A persisted attempt lease avoids
  immediate retries after a crash; an interrupted attempt waits until its next
  recorded check, normally one hour later.
- Feed IDs/GUIDs determine entry identity, with URL and content-hash fallbacks.
  Read state survives a refresh while its entry is retained. Old unsaved entries
  can rotate out; mark or save actions use versions to reject stale changes.
- Pause/removal invalidate an outstanding refresh result. An already-started
  request may finish, but cannot commit after pause, removal or data deletion.
  Source removal deletes its rotating entries and leaves Saved links intact.

## Boundaries and implementation

`PublicFeedReader` reuses the public-web transport's resolve-once/public-address
checks, exact destination dialing and HTTPS-only scope validation. It carries no
ambient cookies, proxy credentials or provider tokens. Private addresses, embedded
credentials, custom ports and redirects to other hosts are rejected. A website
may advertise a feed on another public host, but that candidate is not fetched
until the user selects it. HTTP-only and authenticated subscriptions are currently
unsupported. Article links are also limited to public HTTPS addresses.

The parser prohibits DTDs and external entities, checks XML nesting before
building a tree, and accepts RSS 2.0 or Atom 1.0. It does not fetch images,
enclosures, stylesheets or scripts. Titles are limited to 160 characters and
plain-text excerpts to 1,200. Source text remains untrusted; it cannot silently
become memory, task instructions, an approval or an external action.

The implementation follows the [RSS 2.0 specification](https://www.rssboard.org/rss-specification)
and [Atom format](https://www.rfc-editor.org/rfc/rfc4287). XML entity rejection uses
[DTD processing prohibition](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings.dtdprocessing?view=net-10.0).

The deterministic reader/pump lives in the application infrastructure, independently
of OpenClaw agent execution. Subscriptions and entries are versioned SQLite rows.
A refresh commits entries, metadata and the browser revision notification in one
transaction. The single request slot prevents unbounded queued network work.
Authenticated paired devices can manage this personal collection with the same
CSRF checks as the host browser; full export and personal-data deletion remain
owner operations.

Database and JSON export versions are now 5. The migration adds feed tables without
rewriting existing runs, events, memories or saved reading. Export includes current
subscriptions and entries. Closed-study backup/restore retains them. Rolling back
to a schema-4 application requires restoring a pre-upgrade backup; an older
application must not be pointed at the upgraded database.

## Evidence

`artifacts/local-check-feeds-20260914-final` passes 758 backend tests plus protocol,
secret-scan and web checks. Focused tests cover parsing, network/format limits,
conditional requests, cancellation, retention, stale responses, transactional
failure, migration, backup/restore, authenticated APIs and zero model work.
The unknown-length response test proves the streaming limit independently of
Content-Length.

`artifacts/browser-feeds-20260914-c` passes 22 packaged browser checks; the opt-in
native research case is skipped. Desktop and 390-pixel layouts were inspected.
The Feed browser test uses explicitly simulated source/API data; actual storage,
parser and API behavior are independently exercised by the backend integration
tests. It does not represent a live third-party feed or physical-phone test.
The initial browser fixture had an incorrect request matcher, corrected without
changing the application. Failed receipts and package cleanup results are retained.
All 17 native Windows package checks pass under `artifacts/native-feeds-20260914-b`;
its backup schema assertion now compares against the actual exported database
version. The first native attempt retained the obsolete schema-4 assertion. Both
attempts removed extracted scratch after confirmed process and credential cleanup.
