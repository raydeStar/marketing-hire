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
- Standalone media uploads have no approval or send-back state/API. Their
  attached draft keeps its own approval; media inspection opens the saved file.
  If separate media decisions are desired, expose a version-bound media review
  status and decision endpoint rather than treating a usefulness rating as approval.

## Verification and evidence

Build passed using `npm run build -- --outDir ../artifacts/magical-web/website`
(through `cmd.exe` on Windows, preserving the live fixture’s webroot).
The existing bundle-size advisory remains.

`magical-cockpit.spec.ts`: three passing tests cover the lead card, prepared links,
all three decision shapes, decision failure, old-host fallback, week/channel
groups, attached media, revision/digest approval, document send-back, document
history and polished-draft comparison. Today, campaign work and decisions are
mocked. Authentication and the surrounding host are real and disposable. These
checks do not establish live model quality or publishing results.

Final evidence: `artifacts/magical-web-check-1790466782889/`. Desktop and phone
screenshots of the lead card, campaign package and comparisons were reviewed. All eight
screenshots pass the shared `ux-tour` layout checker. The eleven existing
`first-employee-shell.spec.ts` cases passed in the earlier run at
`artifacts/magical-web-check-1790465874728/`; the new mock route was subsequently
corrected and retested separately, without repeating that unchanged suite.

`web/tools/check-magical-web.mjs HOST_DLL [SPEC...]` starts only an owned host at
5183, checks storage, runs the bounded browser suite, waits for host exit, and
removes its study and temp ledger. It retains results, screenshots and cleanup
receipts. No GitHub Actions, live model calls or GPU work were used. The shared
5190 fixture was not restarted, stopped, or approved.

Before the work split, Codex added the host recommendation/first-win changes;
Claude adopted those in commit `78375b5` and implemented Today in `4e278a8`.
The pre-split Onboarding and ChatActions edits are left for Claude’s commit;
they are excluded from the web commits here. Pre-split temp fixture deletion
was rejected by automatic approval review with “blocked by policy”; those old
stopped fixtures remain under their recorded temp paths, and deletion was not
retried. The new 5183 checks have successful cleanup receipts.
