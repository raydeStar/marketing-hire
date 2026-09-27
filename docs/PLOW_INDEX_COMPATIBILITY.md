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

The compatibility change is in source and awaits the next packaged release.
The installed private image is unchanged, `AGENT_ID` remains empty, and report
authentication has not been exercised. After the host compatibility fix lands,
build one combined candidate, rerun these focused tests and perform the approved
real campaign acceptance. Public listing and reporting need the release decision.
