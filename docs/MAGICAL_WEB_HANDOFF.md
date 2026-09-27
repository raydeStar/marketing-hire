# Prepared opportunities and campaign review

The cockpit leads with one prepared opportunity from `GET /api/today`, then at
most three Today items and a collapsed Later list. Prepared items and evidence
open through existing workspace navigation. Review opens the common campaign
package when all pieces belong to one campaign; otherwise it opens the first
prepared item. Change direction collects the required note; park accepts an
optional note. Both submit the exact decision contract and display failures.
Older hosts fall back to `inboxItems()` without losing the owner’s queue.

Campaign windows show the plan, its explicitly recorded angle, goal, then pieces
by recorded week and channel. Drafts, documents, page copy, media and tasks appear
together; draft attachments join through their saved attachment association.
Campaign experiments now show their hypothesis, decision rule and measured
result, and open the scorecard for its existing decision controls. Page-copy and
experiment keys can be assigned through the campaign API; page-copy windows also
offer the campaign picker. Package counts cover every piece; the progress bar is
explicitly labelled as assignments and drafts.
Each card shows status, editorial grade, recorded claims and blockers. A review
desk keeps draft/page/document decisions inside the campaign. Draft approval
still binds the existing revision and digest. Document approval shares the
document with the employee; it does not publish externally.

Document history compares a chosen earlier version with the current one, defaults
to the last text change, and distinguishes approval-only versions. A polished draft’s
recorded `polished version of draft #N` rationale links its original comparison.
Changed lines have both symbols and subtle color, with a Changes only toggle.
Large documents show complete versions without expensive line highlighting.
New styles are confined to `magical-web.css` and `experience.css`; the shared
shell rules were not reordered. Arrival motion respects reduced-motion settings.

`EmployeeContinuity` reads `/api/continuity`: finished work, changes of mind,
owner decisions, next steps, and hypotheses beside their recorded results and
uncertainty. Details stay collapsed until opened. A failed refresh retains the
last response with a visible notice; older hosts retain the original shift summary.

First-win preparation only saves the assignment. Its receipt survives a reload
through the saved task and offers an explicit “Start a 30-minute shift now”.
That action submits `{requestId, hours:1, durationMinutes:30, cycleMinutes:30,
turnBudget:12}` and shows Claude’s `ShiftFeed` for the returned shift ID. Retries
reuse the request ID; an existing shift is shown rather than starting another.
The card links to the saved assignment and existing shift controls.

## Host integration

- Claude supplied `GET /api/campaigns/{id}/pieces` in `c9c038e`: the campaign
  angle and `{key, week, channel, grade, claims:[{text, sourceKey?, url?}],
  blockers:[]}`. The UI consumes it, renders source links, and uses explicitly
  recorded text fields as an older-host fallback. Missing metadata remains
  visible as Week not set / Not recorded. Page-copy associations are also now
  accepted by the host; the package renders them through the existing Items map.
- The established attachment endpoint is `/api/drafts/media`, as used by the
  existing DraftMedia component. The brief called it `/api/draft-media`; no alias
  was added and no host files were edited after the work split.
- Separate media approval remains out of scope. Media inspection opens the
  saved file; its attached draft keeps its existing approval controls.
- No additional host endpoints are needed for this scope. Continuity’s finished
  and needsYouTop entries are title strings; individual navigation uses prepared
  keys, feed events, and the saved shift report.

## Verification and evidence

Build passed using `npm run build -- --outDir ../artifacts/magical-web/website`
(through `cmd.exe` on Windows, preserving the live fixture’s webroot).
The existing bundle-size advisory remains.

`magical-cockpit.spec.ts`: four passing presentation tests cover the lead card, prepared links,
all three decision shapes, decision failure, old-host fallback, week/channel
groups, attached media, revision/digest approval, document send-back, document
history, polished-draft comparison, continuity bets and failed-refresh recovery.
Today, campaign work, continuity and decisions are mocked in those tests.
Authentication and the surrounding host are real and disposable.

`magical-host.spec.ts` passes against the actual endpoints with no API mocks:
brief and campaign saves, first-win queue/reload without a shift or model turn,
the explicit shift budget, scripted cycle and live feed, Today review/change,
campaign week/grade metadata, experiment filing/navigation, and continuity’s
saved work and changes of mind. The runtime is explicitly scripted and the pump
disabled; these checks do not establish live model quality or campaign lift.

Final evidence: `artifacts/magical-web-check-1790472877766/` (five passing checks).
Desktop and phone screenshots of the lead card, campaign package, pieces,
continuity and comparisons were reviewed; all sixteen captures pass the shared
`ux-tour` layout checker. Real endpoint responses and the shift request are in
`screenshots/real-contracts.json`. The eleven existing `first-employee-shell.spec.ts`
cases passed at `artifacts/magical-web-check-1790472576999/`; the subsequent fixes
were confined to experiment navigation, package labels and new test timing.
The host build passed with locked restore and zero warnings. Build hashes and
cleanup receipts accompany the final evidence; disposable binaries are removed.

`web/tools/check-magical-web.mjs HOST_DLL [SPEC...]` starts only an owned host at
5183, checks storage, runs the bounded browser suite, waits for host exit, and
removes its study and temp ledger. It retains results, screenshots and cleanup
receipts. No GitHub Actions, live model calls or GPU work were used. The shared
5190 fixture was not restarted, stopped, or approved.

Before the work split, Codex added the host recommendation/first-win changes;
Claude adopted those in commit `78375b5` and implemented Today in `4e278a8`.
Claude committed the pre-split Onboarding and ChatActions edits in `e8ff196`;
those files remain outside this work’s edits. Pre-split temp fixture deletion
was rejected by automatic approval review with “blocked by policy”; those old
stopped fixtures remain under their recorded temp paths, and deletion was not
retried. The new 5183 checks have successful cleanup receipts.
