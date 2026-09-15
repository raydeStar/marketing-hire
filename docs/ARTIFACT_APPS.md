# Apps that chat can maintain

Ask in ordinary Chat: "Build me a mood tracker with a date, mood choices and a
note." Thaddeus chooses a definition, saves the app and opens it in Artifacts.
The three starters are conveniences; the model can choose different names,
fields and choices for another tracker, journal, inventory or checklist.

Use **Chat with this app** to select an existing app, then describe the update.
The small chip above the composer identifies which app can be changed. Click
the chip to open it, or its X to stop sharing its data with subsequent messages.
Chat receipts link back to the app. Asking to open another app selects it for
the next message; this first version does not combine a switch and an edit in
the same response.

App changes take effect directly when requested. They do not require the
separate Markdown-write approval flow. Review the resulting entries; **History**
can undo the last change or restore a retained version. Manual forms and
checkboxes use the same records as chat. They make no model request. Existing
Markdown pages and remembered context remain in **Notes & memory**.

## Current scope

This is a declarative data-app runtime. The model selects a schema and data;
React renders the controls. It supports text, numbers, calendar dates, checkboxes,
select choices, optional day filtering, number totals/averages, checkbox progress
and a select distribution. Users can customize the definition or ask chat to do
so. Incompatible changes to existing records are rejected instead of losing data.

It does not generate or execute arbitrary HTML/JavaScript, formulas, custom page
layouts, external integrations or background schedules. Such capabilities need
their own design. No container or VM is required for these data apps. They are
not a replacement for OpenClaw's separate research/task runtime.

Numbers recorded from chat remain user/model-supplied values, not verified facts.
The app instructions require unknown calorie/caffeine amounts to be left blank
or clarified. Mood records are a personal journal, not a diagnostic assessment.

## Persistence and boundaries

SQLite schema 6 adds `artifact_apps` and `artifact_revisions`. Old tables are
preserved. Backups and study export include both definitions and records; each
app also has a JSON export. An older schema-5 application cannot open the upgraded
study. Rollback requires its pre-upgrade backup and the older application.

Writes compare the expected app version and have an operation ID. Repeating the
same operation cannot append a second entry while that revision is retained;
a stale edit is rejected. Chat commits its app, revision, completed run and
assistant receipt in one transaction, so a failed save cannot produce a success
receipt. A second window receives changes through the existing event stream.
An open draft is preserved and saving is disabled when its version becomes stale.

This MVP retains 20 snapshots per app, up to 32 apps including archived apps,
12 fields, 1,000 entries, and 250,000 serialized characters per app. A single
change can touch at most 100 entries. Archive keeps records; it does not free an
app slot. These bounds prevent an unbounded revision store.

Every ordinary chat request receives active app titles/descriptions. Only the
selected app's definition and up to 40 recent entries (16,000 serialized
characters) are included. The model cannot edit other apps or omitted older
entry IDs in that request. Older records remain manually accessible. Metadata,
records and previous messages are treated as untrusted content.

App calls use the existing model provider and token ledger. The default ordinary
reply allows one model call and one app action. Actual provider usage is counted;
unknown usage retains the existing conservative charge. A provider overrun stops
the app mutation. Manual CRUD, starters and opening an app from the UI use no
model tokens. View usage and reply limits through the model name → Log → Info.

## Focused verification

`ArtifactAppTests`, `ArtifactProviderTests` and `ArtifactApiTests` cover generic
definitions, selected-app capabilities, CAS conflicts, idempotent retries,
transaction rollback, usage charging, schema migration, backup restore and
HTTP session/CSRF enforcement. These use fictional data and synthetic providers.

The packaged browser check is:

```powershell
node scripts/browser-check.mjs artifacts/portable-NAME/thaddeus-win-x64 artifacts/app-browser-NAME artifact-apps.spec.ts
```

It verifies ordinary chat creation and automatic opening, mood logging, manual
and chat checkbox edits, undo, reload, export, desktop/mobile and both themes.
The compatible endpoint is a bounded local HTTP fixture: four synthetic replies,
zero live model/search calls, zero GPU use. This proves the application wiring;
natural-language quality with the configured model remains manual QA.

After owned test processes exit, remove the disposable `study` directory through
`cleanArtifactPaths`, record its removal, and retain compact logs/screenshots.
Do not run this check against the owner's live study.
