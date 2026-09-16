# MVP finish line

Scope updated from the owner's 2026-09-14 instruction: finish the usable product;
stop broad benchmark and sandbox qualification loops.

The [September 16 acceptance checkpoint](MVP_ACCEPTANCE_20260916.md) maps the
requested MVP behavior to reviewed evidence and separates owner acceptance from
unfinished distribution work.

## Available for manual QA

September 16: [chat recovery and saved-work search](CHAT_QOL.md) are installed in
the running study, including same-tab draft recovery and duplicate Feed story
handling. The linked document records the current package and verification;
older package names below are historical evidence.
The current package also includes exact Undo for recent To-do actions and visible
Ideas progress/failure/cancellation with log details and an explicit fresh attempt.
File uploads now show progress, preserve partial successes and continue past a
rejected file. Chat remains editable while pending uploads prevent premature
sending. The latest host-only installer contains this same local QA package.

The identified [Windows QA build](MANUAL_QA.md) already includes conversation,
visible token accounting, scoped OpenClaw research, guidance/questions/restart,
exact import approval, persistent history, notes/memory, Artifacts, To-do, Ideas,
saved links and Feed subscriptions, local Search, backup/restore, the revised
responsive layout and raven. The sidebar now reads Chat, Search, Feed, Ideas,
To-do, Artifacts, with Settings at the bottom. These features have implementation and scoped
verification evidence. User acceptance and real research quality are separate.

## Remaining MVP work

1. **Manual QA and usability fixes.** Work through the five short flows in the
   QA guide and fix blocking findings. Final acceptance of the layout/raven is
   still the owner's decision. Do not add unrelated features to this pass.
2. **Distributable installation.** Finish the supported-host onboarding and
   installation path, upgrade/recovery, publisher trust, and the notices/source
   provisions for shipped worker/runtime components. The current unsigned,
   host-only installer preview is not a finished consumer distribution.
   The latest September 16 host-only installer contains the upload-recovery build
   and passes eight native cases, including the corrected ownership marker. Its
   unchanged installer sources retain the earlier nine-test contract evidence.
   See [the current installer checkpoint](WINDOWS_INSTALLER.md#september-16-installer-matches-the-current-qa-app).
3. **Platform scope and acceptance.** The current worker implementation supports
   Windows x64 and Linux x64 only. A native Mac research worker is not implemented;
   it requires backend work as well as native acceptance.
   [The Mac scope](MAC_MVP_SCOPE.md) describes both Apple silicon and Intel,
   reuse, prerequisites and estimated effort. The owner subsequently shelved Mac
   implementation; it is outside the current Windows manual QA cycle.
   Remaining Linux desktop acceptance also stays open. Phone setup is deferred;
   a phone connects to a host, and the host must remain awake.

Items 2–3 apply to launching the cross-platform product for other people. They
do not prevent the owner testing the existing local Windows preview now.

The owner took over the old installer-fixture cleanup after automatic review
rejected the authorized action. It is still present and was preserved. Native
verification of the current installer passed under its different package
identity; do not retry the old cleanup as incidental work. Mac scoping is
delivered and implementation is shelved. The running study and its data remain
available; the full launch goal is not complete.

Search cost protection was verified in `portable-hidden-rail-20260915-a` and is
retained in the current QA package: a visible study-wide monthly limit, default 100,
zero to pause, atomic admission across tasks and durable attempt accounting.
43 focused backend checks and the packaged browser setup flow pass with no live
provider requests. See [public search](SEARCH_CONNECTIONS.md) for account-wide
limits and [development status](DEVELOPMENT_STATUS.md) for evidence.

Completed installation follow-up: the native desktop entry now reopens its exact
running study. The actual Windows package and installed entry are verified; the
updated installer includes the current search allowance and reopening behavior.
This closes a usability gap, not the remaining publisher/worker/platform work.

Windows desktop startup refusals also remain visible in a dialog, with recovery
instructions for a port conflict. This change is installed in the current QA
package and included in its installer; see [Windows installer](WINDOWS_INSTALLER.md).

## After the MVP

- Floated or pinned individual artifacts in the sidebar.
- Broad Lab benchmarks, paired model optimization and performance campaigns.
- Docker Sandboxes recovery, additional sandbox backends and optional isolation
  experiments. Preserve the existing confinement/approval checks when changing
  relevant code; do not remove the boundary to make setup appear easier.
- Optional search adapters, shared retrieval caches, automatic provider switching,
  hosted accounts, scheduling and additional agent capabilities.

## Cheap research by default

Use saved notes and links first; use subscriptions for recurring updates. Request
public search only when discovery is useful. Display requests separately from
model tokens. Never describe supplied-link research as zero-cost inference.
See the [research experience recommendation](RESEARCH_EXPERIENCE.md) for the
launch defaults and the distinction between free search credits and result
storage rights. Additional search providers are optional follow-up work.
The study cap cannot measure other apps using the same Brave account, separate
studies or usage lost when restoring an older backup. Provider-side spending
controls remain the account-wide limit. No provider key is needed for routine QA.
