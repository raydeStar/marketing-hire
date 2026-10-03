# Video transcripts and source checks

Two additions to the employee's own research tools, both keyless and inside the
Plow image. Neither adds a browser session, cookie or account.

## `video transcript`

`video transcript --url <youtube link>` prints one public YouTube video's
captions as JSON (`business/agent/hire/bin/video.py`). It reads captions only and
never downloads the video. It uses yt-dlp 2026.8.19 with yt-dlp-ejs 0.8.0, and
Node as yt-dlp's JavaScript runtime when Node is on the PATH (it is in the Plow
image). Both are pinned in `harken-requirements.lock` and installed into the
existing pulse environment.

- **Input.** One video link: `watch?v=`, `youtu.be/`, `/shorts/`, `/live/` or
  `/embed/`. Playlists, channels and other sites are refused with what to send
  instead. The tool rebuilds the address from the 11-character id, so yt-dlp is
  only ever pointed at a YouTube watch page.
- **Captions chosen.** Captions the creator uploaded come first, then YouTube's
  speech recognition of the spoken language. Another language (`--lang`) may be
  a machine translation, labelled `translated`. `captions.kind` says which.
- **Output.** A `[mm:ss]` stamp every half minute, in pages of 2,000–40,000
  characters (`--max-chars`, default 20,000) continued with `--offset`. The first
  page also carries the description's first 600 characters and records a
  `transcript` event in the activity feed (see `business/agent/FEED.md`).
- **Failures** exit 1 with one line that starts with `blocked`, `unavailable`,
  `no_captions`, `live` or `failed`, followed by what the owner can do.

### In shifts

Shift workers never run tools; the host gathers their sources. When a task's
assignment contains a YouTube video link (up to three owner links are read, as
for pages), `EmployeeShifts.ReadAllowlisted` reads its captions through the
shift container with `video transcript --max-chars 6000`, not the page's HTML.
The research allowlist doesn't apply, because the owner gave that exact video.
The writing turn gets the first 6,000 characters as owner evidence
(`VideoEvidenceChars`; a page gets 1,400), labelled with the caption kind and
how much was read. The fact check therefore accepts what was said in that
portion and nothing past it. A failure becomes a `sourceGaps` line, so the draft
knows the video wasn't read. A local fixture (`Marketing:FixtureLedger`) has no
container, so there it runs `video.py` with the local `python`, beside its
ledger, as its `hire` commands do. That needs yt-dlp in the local Python.

### Known limits

- **Data-center addresses.** YouTube asks some servers to sign in ("confirm
  you're not a bot"). It worked from a home connection and from WSL. It has not
  been tried from a hosted Plow install, where a block is more likely. The tool
  then says `blocked` and the skill asks the owner to paste the transcript.
- **Automatic captions** mishear names, numbers and prices. The label says so,
  and the skill tells the employee to confirm them before they reach a draft.
- **Only the first 6,000 characters** (about the first seven minutes of a talk)
  reach a shift's writing turn. Chat can page through the rest.

## Pulse source checks

Harken's RSS source skips a feed that fails, so a scan used to report Reddit and
Google News as `unverified` even when one was blocked. `pulse.py` now swaps in a
subclass whose HTTP client records each feed's response through an httpx
response hook. It reads the same response Harken parses and sends no extra
request. A scan's `feeds` field gives each feed's status: `ok`, `blocked`
(401/403), `rate_limited` (429), `not_a_feed` (a web page, usually a block or
sign-in page) or `error`. Anything but `ok` is listed in `errors`, and coverage
is `partial`. A source stays in `unverified_sources` only if no response
arrived (a timeout or connection error).

`pulse doctor [--sources ...]` sends one request per source the way a scan would
and stores nothing. Each request counts toward the source's rate limit. In the
first live check, a Reddit scan run seconds after the doctor got HTTP 429. Run
the doctor to diagnose a failing source, never before a scan.

## Verification (October 3)

- Offline tests: `business/agent/hire/tests/test_video.py` (with YouTube faked) and
  `test_pulse.py` (real Harken pipeline, `httpx.MockTransport`). The pulse tests
  skip outside a pulse environment. Run them with the lock installed and Harken's
  pinned source on `PYTHONPATH`:
  `python -B -m unittest discover -s business/agent/hire/tests -p "test_*.py"`.
- Host: `dotnet test tests/Thaddeus.Tests/Thaddeus.Tests.csproj --filter
  "FullyQualifiedName~ShiftRecoveryTests|FullyQualifiedName~EmployeeShiftTests"`.
  It covers an owner's YouTube links reaching the writing turn as evidence, a
  failed one becoming a gap, and the tool's JSON parsing. The full suite passed:
  1,516 passed, 1 skipped.
- In the app: a practice shift in the local campaign fixture (scripted turns, no
  model calls). The task named the TED talk below and a made-up video id. The
  create stage noted "Read the captions of “Inside the Mind of a Master
  Procrastinator | Tim Urban | TED” (the first 5,998 of 12,895 characters)" and
  "Could not read the captions of https://youtu.be/aaaaaaaaaaa: unavailable: …".
  The fact check passed, and Work → History listed the read, labelled Video.
- Live, no model calls:
  - On Windows (Python 3.11 with Node), a TED talk's uploaded English and
    French captions were read and paged.
  - In WSL Ubuntu 22.04, the Dockerfile's pulse-environment steps (Python 3.10,
    no Node) read the same captions in 6 s and wrote the bundled notice.
  - An unknown video id gave `unavailable`, and a channel link was refused.
  - `pulse doctor` reached Hacker News, Reddit and Google News; the scan
    straight after got Reddit's 429, now reported as `rate_limited`.
- Not yet run: the Plow image build, a hosted install, or a shift with a live
  model. The pinned v19 image doesn't include any of this.
