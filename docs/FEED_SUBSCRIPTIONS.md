# Subscriptions and saved reading

Feed combines updates from explicitly chosen sources with the existing saved-link
collection. **Add a source** accepts a public HTTPS RSS 2.0 or Atom 1.0 address,
or a UTF-8 website that advertises one. Preview reads the entered address; choosing
a discovered feed makes a separate request. Subscribe authorizes ongoing checks
of that exact source host. No default subscriptions are installed.

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
