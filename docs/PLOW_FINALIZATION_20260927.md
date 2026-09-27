# Plow release qualification — September 27

The private installation is updated and retains the owner's business brief,
account, conversation, and credential vault. **Public release is still held:**
the first real campaign shift exposed a model-setting incompatibility.

## Verified

- Committed source `703556b` initially built as
  `hirezero-marketing:plow-package-release-20260927-2`, image
  `sha256:d1c518d9c4aadeafc950b5b8ecc3e589d86a5bc8ed5756b2e5e9f1a3a82cc0b7`.
  The packaging fix accepts the committed Windows line endings in the persona
  while still refusing an unknown or duplicate connection marker.
- Candidate 3 adds the pinned listening dependency and is now installed:
  `hirezero-marketing:plow-package-release-20260927-3`, image
  `sha256:f9d085fddcd9aecf298b5ce82851d775face39b9493f41ff35ec3a01324f2ff1`.
  Its packaged listening-store, API/browser, vault, and restart checks passed.
  The owner's brief, tasks, drafts and messages survived this update too.
- The actual packaged host/API/browser workflow passed with a scripted model.
  Eight desktop/phone layout checks reported no faults; the recommendation and
  campaign screenshots were visually reviewed. These are fictional fixtures,
  not proof of a real worker result.
- Restart, old-to-new upgrade, and new-to-old rollback preserved the fictional
  owner, saved work, and encrypted vault key. Disposable test containers,
  networks, and volumes were removed.
- The installed Gateway/SDK meter passed its offline refusal, accounting,
  completion, and replay checks. Four focused Python ledger checks cover the
  changed shift allowance and reservation behavior.
- Before the private update, the owner volume was backed up. Afterward, the
  owner account, brief, tasks, and conversation count matched the prior install.
  The vault remained healthy. No recurring work or publishing connection is on.
- A pattern scan found no credentials in tracked files or the candidate's added
  application directories. The image has no owner state files, Plow token, or
  enabled Agent Index ID. This is a bounded scan, not a full-history clearance.

## Real acceptance result

One campaign plan and one assignment were saved: prepare a truthful 80–120-word
LinkedIn introduction for HireZero, pending owner review. The assignment and
plan were prepared by the coding assistant; they are not employee output.

The 15-minute shift was limited to 12 turns and a 250,000-token allowance, with
no publishing or recurring work. Its first model turn failed:

```text
Thinking level "low" is not supported for plow/z-ai/glm-5.2
```

The shift was stopped, no draft was produced, no publication exists, and there
is no active shift. The allowance is post-response accounting, not a guaranteed
hard billing cap. Failed-turn usage records are not successful work receipts.

The real pinned Gateway regression now proves that `low` is rejected before a
provider send and that `off` permits the synthetic metered completion. The host
still requests `low` in `MarketingShiftBridge.cs` and `MarketingRunway.cs`.
Their ownership remains with Claude under the existing work split; permission
for this narrow compatibility change was requested before editing them.

**Required host change:** make the bounded worker reasoning level configurable,
retain `low` for the qualified existing worker, and select `off` for the Plow
GLM route. Cover both shift and campaign-runner requests. Do not change the
main chat, model identity, meter admission, or accounting to hide this failure.
Repackage and repeat the same saved assignment only after the offline check.

## Additional packaging defect

The listening stage also reported that its container was unreachable. Direct
inspection showed the actual cause: `pulse` exists, but importing `harken`
fails. The correction packages the existing pinned dependency versions and
Harken commit `d0710a427dbbe712594ef3a6c25112e1d14cc027` in an isolated Python
environment, verifies its source archive checksum, and preserves its MIT notice.
The package check now opens the listening store without an external search;
`pulse --help` alone would miss the dependency failure. The private installed
command now opens the store successfully. External community coverage has not
been requalified by this offline check.

## Next gates

The follow-up release preflight prepared and compiled a four-file host
compatibility patch in an isolated source snapshot (zero warnings/errors).
The main checkout's host files were left unchanged under the Claude ownership
boundary. The reviewable patch and build log are retained in ignored
`artifacts/plow-release-preflight-20260927/`; this is not a live fix receipt.

The same preflight found and fixed a separate source-level
[Index transcript compatibility defect](PLOW_INDEX_COMPATIBILITY.md): the
official bundled collector skipped compressed rows. The adapter passes eight
offline checks and recovers the full existing 56,115-token total. It awaits
the next image build; reporting remains disabled. The three reviewed listing
assets are now retained with [hashes and provenance](media/hirezero/README.md).

1. Complete the host reasoning fix and one real graded campaign piece with a
   confirmed worker usage receipt. Review or send it back in the cockpit.
2. Check the owner's real phone conversation and native multiplayer with two
   distinct authenticated people; responsive browser screenshots prove neither.
3. Finish the [demo assets and owner narration](demo/README.md), replacing the
   marked fictional segments with accepted real footage for the competition cut.
4. Review the exact public source/image and inherited Plow source terms. The
   project is MIT; the upstream base still reports no declared GitHub license.
5. After owner approval, publish source/image, register the listing and required
   assets, enable five-minute usage reporting, and request Plow admission and
   verification. Then prove a separate user's installation.

Private receipts and the owner backup remain under ignored
`artifacts/plow-finalize-20260927/`. They must not be included in a public image,
demo, or source commit. Later Claude UI commits are not silently folded into
the qualified image above.

See the [release checklist](PLOW_RELEASE_CHECKLIST.md) and
[submission kit](HACKATHON_SUBMISSION.md) for the public handoff.
