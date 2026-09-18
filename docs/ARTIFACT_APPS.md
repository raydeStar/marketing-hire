# Apps designed and maintained through chat

Choose **Build an app**, describe what you need in Chat, then send the request.
It goes to the configured model. Thaddeus
asks concise questions when missing details matter; no app is saved while that
question is pending. Once the request is clear, the model writes the app's own
HTML, CSS and JavaScript, declares its persistent fields, and creates its page.
There are no instant starter templates. A fully specified request can proceed
directly to creation without an unnecessary approval exchange.

Open an app from the shelf or a chat card, then use **Show chat** in its header
to describe an update.
The small chip above the composer identifies which app can be changed. Click
the chip to open it, or its X to stop sharing its data with subsequent messages.
Chat receipts link back to the app. Asking only to open an app selects it for
the next message. A pending edit, redesign or deletion can instead read that
app and finish in the same request. The follow-up receives its current records
and the original conversation, including answers to clarification questions.
Reloading an app's URL restores its selection for Chat.

The Artifacts shelf has **Edit** for the app's name and description, preserving
its page and records. Open it for record/field editing, or use Chat for larger
layout and behavior changes. **Delete** on the shelf or a deletion request in
Chat moves the app to **Trash**. **Restore** brings it back with its data;
this is reversible deletion, not a permanent purge.

App changes take effect directly when requested. They do not require the
separate Markdown-write approval flow. Review the resulting entries; **History**
can undo the last change or restore a retained version. Manual forms and
checkboxes use the same records as chat. They make no model request. Existing
Markdown pages and remembered context remain in **Notes & memory**.

## A page for each app

Each app opens at `/apps/<id>` with a minimal header: the chat-panel toggle,
app name and close button. Its own content scrolls below that header. The
Artifacts heading, shelf tabs, model controls and activity log are outside the
app page. Closing returns to the previous workspace; refresh and browser
Back/Forward preserve the app route. These remain authenticated study pages.

On desktop, opening from chat shows the conversation beside the app. **Full
screen** expands the app across the workspace; **Show chat** brings the conversation
back without discarding its draft. Opening from the shelf starts with the full
page. At widths up to 1,000 px, only chat or the app is visible. The selected-app
chip in chat returns to the app. Model usage remains available in the chat header.

## Current scope

The model designs the layout and client-side interactions: cards, charts,
forms, tabs, calculations and other small browser apps. New designs include
`definition.page` with `html`, `css` and `javaScript`. Older apps without that
property retain their original table/summary view; ask Chat to redesign one to
add a custom page without discarding its records.

Persistent records still use declared text, number, date, checkbox and select
fields. The contained page receives its records through `thaddeus.onChange` and
saves through `thaddeus.save`. Both those controls and chat update the same
versioned store. Data updates leave the page mounted, preserving drafts and
selected tabs; changing page code reloads its design. **Data & history** retains
manual record editing, export, deletion and undo even when the page has an error.
A displayed error can be described in Chat for repair; there are no automatic
model retries or background repair charges.

This supports self-contained browser apps. External APIs, backend servers,
package installation and background schedules are outside this app capability.
No container or VM is required. It does not replace OpenClaw's task runtime.

## Generated page boundary

The authenticated `/api/artifacts/<id>/page` document uses a CSP sandbox and an
iframe with `allow-scripts allow-forms`, without `allow-same-origin`, popups or top
navigation privileges. Native forms need `allow-forms` for validation and local
JavaScript submit handlers; `form-action 'none'` blocks actual form submissions,
including those aimed at the host. The host retains its strict script policy. The page's
policy blocks fetch, external assets, nested frames and workers; the parent's
frame policy also blocks external frame destinations. Navigating away revokes
the data port and exposes recovery UI. These use the browser's [iframe sandbox
rules](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/iframe).

The parent transfers a dedicated MessagePort to that one frame. It supplies only
that app's title, fields, records, version, local date, theme and read-only state.
No credential, session, CSRF value, other app or study history is included.
Save requests accept only record upserts and deletions; the parent fixes the app
id and API route and adds authentication outside the frame. Existing server
validation, CAS and operation IDs remain authoritative. The bridge caps each
change at 100 entries/60,000 characters and admits at most 30 saves per minute.

Page code is untrusted and is not verified for correctness. Browser containment
is not an OS sandbox or a CPU/memory/network quota. A faulty page can malfunction;
its saved data and the host's recovery controls are kept outside its document.

Numbers recorded from chat remain user/model-supplied values, not verified facts.
The app instructions require unknown calorie/caffeine amounts to be left blank
or clarified. Mood records are a personal journal, not a diagnostic assessment.

## Persistence and boundaries

SQLite schema 6 added `artifact_apps` and `artifact_revisions`. Schema 7 records
the generated-page format, preserving existing rows and preventing older hosts
from silently dropping page code during edits. Backups and study export include both definitions and records; each
app also has a JSON export. An older schema-6 or schema-5 application cannot open the upgraded
study. Rollback requires its pre-upgrade backup and the older application.

Writes compare the expected app version and have an operation ID. Repeating the
same operation cannot append a second entry while that revision is retained;
a stale edit is rejected. Chat commits its app, revision, completed run and
assistant receipt in one transaction, so a failed save cannot produce a success
receipt. A second window receives changes through the existing event stream.
An open draft is preserved and saving is disabled when its version becomes stale.

This MVP retains 20 snapshots per app, up to 32 apps including archived apps,
12 fields, 1,000 entries, and 250,000 serialized characters per app, including at most 40,000 page-code characters. A single
change can touch at most 100 entries. Archive keeps records; it does not free an
app slot. These bounds prevent an unbounded revision store.

Every ordinary chat request receives active app titles/descriptions. Only the
selected app's complete definition/page code and up to 40 recent entries (16,000 serialized
characters) are included. The model cannot edit other apps or omitted older
entry IDs in that request. Older records remain manually accessible. Metadata,
records and previous messages are treated as untrusted content.

App calls use the existing model provider and token ledger. The default ordinary
reply allows up to two model calls and two app actions within the same 64,000
total-token allowance and 4,096 output tokens per call. Most replies use one
call; the second is available for a single read-then-change continuation. An
explicitly smaller allowance is never raised. The second dispatch must pass
token admission again; exhausting the shared allowance prevents it.
Actual provider usage is counted;
unknown usage retains the existing conservative charge. A provider overrun stops
the app mutation. Clarification, creation and later design/data replies each
count as normal model calls. Manual CRUD, generated page controls and opening
an app from the UI use no model tokens. View usage and reply limits through the model name → Log → Info.

## Focused verification

`ArtifactAppTests`, `ArtifactProviderTests` and `ArtifactApiTests` cover generic
definitions, selected-app capabilities, CAS conflicts, idempotent retries,
transaction rollback, usage charging, schema migration, backup restore and
HTTP session/CSRF enforcement. These use fictional data and synthetic providers.

The packaged browser check is:

```powershell
node scripts/browser-check.mjs artifacts/portable-NAME/thaddeus-win-x64 artifacts/app-browser-NAME artifact-apps.spec.ts
```

It verifies ordinary chat creation, the user-selected side-by-side opening flow, mood logging, manual
and chat checkbox edits, undo, reload, export, desktop/mobile and both themes.
The compatible endpoint is a bounded local HTTP fixture: four synthetic replies per spec,
zero live model/search calls, zero GPU use. `artifact-generated.spec.ts` additionally
proves a question with no app write, answer propagation, generated controls,
chat updates without remounting, redesign/undo with retained records, browser
containment and broken-page recovery. Run it in a separate fresh browser fixture.
`artifact-crud.spec.ts` reproduces a clarification answer with no selected app,
requires an actual redesign in the same request, and covers URL selection,
shelf metadata edits, shelf/chat deletion, restoration and preserved records.
This proves the application wiring;
natural-language quality with the configured model remains manual QA.

After owned test processes exit, remove the disposable `study` directory through
`cleanArtifactPaths`, record its removal, and retain compact logs/screenshots.
Do not run this check against the owner's live study.
