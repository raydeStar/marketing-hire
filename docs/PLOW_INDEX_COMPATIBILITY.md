# Agent Index transcript compatibility

The pinned Plow image's official Index client reads only `event_json`. OpenClaw
also stores rows in `event_zstd`, with their decoded size in `event_utf8_bytes`.
The unmodified client silently skips those compressed rows, understating usage.

During the September 27 read-only check, it counted **33,805 tokens**. Reading
both storage formats recovered **56,115**, matching the cockpit's recorded
onboarding total. No new model call, registration or report was made. These are
token counts, not a billing estimate or successful campaign-work receipt.

## Narrow packaging adaptation

`packaging/plow/index-client.mjs` accepts only the bundled official client with
SHA-256 `970caf7534cd7d3b71ffee8f1a576f9da4dc494a508e8ab1998ee2ce6f4a2ac4`.
It replaces the transcript query with `index_transcripts.read_transcript_rows`.
The package installs the helper beside the client and declares `libzstd1` as a
runtime dependency. The rest of the official client is unchanged: authentication,
response deduplication, day/model buckets, token fields, merge and report policy.
The existing boot loop still controls registration and five-minute reporting.

The helper supports legacy JSON databases and mixed JSON/zstd databases. It
bounds each compressed and decoded row at 64 MiB and refuses corrupt frames,
size mismatches or invalid compressed JSON. Failures reach the official client's
existing refusal path, preventing a smaller partial total from replacing a
complete reported day. It reads existing data; it never modifies transcripts.

A changed upstream client makes the package build fail for review. Once upstream
supports compressed rows, remove the adapter and requalify the unchanged usage
totals rather than blindly moving the source hash forward.

## Verification and release state

Eight offline checks cover mixed/legacy storage, UTF-8, cutoff, response-ID
deduplication, corruption, size/schema failures, and the official CLI refusing
to POST partial totals. Run them against a newly built package:

```powershell
docker run --rm --network none --mount "type=bind,source=$PWD/packaging/plow,target=/check,readonly" --entrypoint python3 YOUR_PACKAGE_IMAGE -B /check/test_index_transcripts.py
```

September 27 checks used the current immutable image
`sha256:f9d085fddcd9aecf298b5ce82851d775face39b9493f41ff35ec3a01324f2ff1`
with the adapted client and helper mounted read-only. A separate no-network
collector preview mounted the owner volume read-only and used temporary
agentsview state. Both checks passed. Compact local evidence is retained under
ignored `artifacts/plow-release-preflight-20260927/`.

The compressed-storage change is installed in the September 28 candidate.
The first successful campaign cycle exposed another storage difference: OpenClaw
`modelRun` calls do not create ordinary assistant transcript rows. The Plow meter
does retain digest-bound provider receipts for those actual calls.

`index_worker.py` adds those confirmed receipts to the official collector's
existing response-ID set and calendar buckets. It counts input/output once,
excludes synthetic `fixture://` work, and never counts reservations or unknown
usage. Missing or inconsistent receipts stop the report. The pinned Plow
completion route identifies the model; other terminal types are excluded.
Fourteen offline tests cover both adapters, including cross-source duplicates,
cutoffs, fresh stores and refusing a partial report. A read-only preview recovered
77,164 tokens: 56,115 from onboarding and 21,049 from the real campaign cycle and
its closing reflection. These are usage counts, not customer traction.

Reporting remains disabled during local package checks. Public listing and
reporting were authorized by the owner on September 27; publication receipts
belong in the release report, separate from these offline checks.

## Repeated reporting in a container

The first report succeeded, but subsequent five-minute passes exposed a lifecycle
problem in the bundled agentsview 0.44.0: a detached daemon held its database lock
while later commands could no longer discover it. The official client received
empty output from that failed command and correctly refused a partial report.

The boot adapter now runs the supported `AGENTSVIEW_NO_DAEMON=1 agentsview sync`
mode, waits for its exit and refuses the reporting pass if sync fails. The client
queries the resulting archive with `usage daily --json --offline --no-sync`.
This avoids a second daemon and pricing fetch; model token counts still come
from the same stores. The official registration, install identity, five-minute
schedule and upload code remain unchanged. A real installed-binary offline test
runs two collection passes and verifies no agentsview daemon remains.
