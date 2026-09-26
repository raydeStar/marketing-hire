# Employee shifts

The marketing employee works **shifts** of 8, 16 or 24 hours. During a shift it runs the operating
loop from the owner's research, *sense → prioritize → create → align → launch → measure → decide →
institutionalize learning*, as a repeating cycle, usually hourly. It stops when the window ends,
the budget runs out, the owner pauses it, or nothing useful is left to do. It never pads the time
with filler.

## Design rules

- **The host runs the shift.** Every cycle, stage, model turn and effect is recorded on the host.
  A browser tab being open is not the scheduler.
- **The model gets no tools.** Each model stage is one bounded turn. The host sends a packet (brief,
  permissions, signals, queue) and requires a strict JSON answer. The host then validates it and
  applies the effects itself, the same pattern the campaign runner already uses. Permissions are
  enforced in code, not in the prompt.
- **Deterministic first.** Sensing, QA, measuring and rule-based decisions are ordinary code. The
  model is used only where judgment is needed (prioritize, create, write up learning), and only when
  there is something to act on.
- **Nothing leaves the building.** No posting, sending, spending or account changes. Public-facing
  output becomes a draft for owner approval. Launch means a QA'd hand-off for a person to post.
- **The runtime can be swapped.** The model sits behind `IShiftRuntime`:
  - `scripted`: a deterministic stand-in for tests, so a full shift costs nothing;
  - `openclaw`: the local OpenClaw gateway, metered, with no tools;
  - later, a Plow runtime behind the same interface.

## Stages

| Stage | Who | Reads | Produces |
|---|---|---|---|
| **Sense** | Code | Scorecard anomalies, tasks (blocked, stale, needs a decision), drafts awaiting approval, approved drafts not yet checked, experiments due, teammate input | Signals, each with a severity |
| **Prioritize** | Model | Brief, objectives, PERMISSIONS.md, signals, the task queue, recent learnings, the owner's feedback and the Marketing notebook, the research allowlist | Up to 3 priorities (each may ask for research and up to two allowlisted pages), plus new tasks; the host updates the board |
| **Create** | Model, then a review turn | One priority, its task, sources, related Library documents, the owner's feedback and the notebook | A Library document (draft) or a channel draft for approval, critiqued against the creative-review rubric and revised if it scores 3 or lower anywhere |
| **Align** | Code | Everything created this cycle | Decisions routed to the owner (drafts, documents, "Asks you first" items) |
| **Launch** | Code | Drafts the owner approved | Campaign QA (links, HTTPS, UTM tags, channel length limits, placeholders, risky claims) and a hand-off note for a person to post |
| **Measure** | Code | Experiments whose review date has arrived | The primary metric over the test window vs. its baseline |
| **Decide** | Code | That measurement and the experiment's pre-set rule | Scale / iterate / stop, as an owner decision |
| **Institutionalize** | Model (end of shift) | The shift's record, the owner's feedback, the notebook | A shift report in the Library (`Shift reports/`), learnings, and additions to the Marketing notebook |

## Research during a shift

Prioritize may ask for research on a priority by naming one short topic. The host, not the model, does the research:
- **Discussions:** it searches recent Hacker News threads (the last 90 days) and reads the two most-discussed pages in full.
- **Headlines:** it runs the employee's own `pulse` tool inside its container (Hacker News, Reddit and Google News mentions) and keeps up to six sources in total. These are headlines and snippets, not read pages, and the model is told to cite them only for what they say. In a public draft, citation markers are removed from the text and the sources it relied on are listed in its rationale. In practice Google News supplies most of them; Reddit often returns nothing.
- **Allowlisted sites:** a priority may also name up to three pages to read, but only on the owner's **research sites** (Objectives → Research sites: the owner's own site and competitors', at most ten, subdomains included). The reader is HTTPS-only on port 443, connects to public IPv4 addresses only, follows at most three redirects and only within the allowlist, reads 2 MB at most, and keeps about 3,000 characters of text, starting near the first price when menus come first. Anything else the model names is skipped and noted in the log.
- **Pages built with JavaScript:** a pricing page whose served HTML shows no price (or any page with little text) is rendered with a Chrome or Edge already on the machine: headless, with a throwaway profile, 25 seconds at most. Every connection the page makes goes through a loopback proxy in the host that reaches public HTTPS addresses only, so a page can't reach the host, the LAN or loopback. With no browser installed, the static read stands. Set `Thaddeus:ResearchBrowser` to a browser path, or to `off`.
- **Market figures:** for market size or competitor scale, a priority may name NAICS industries and public competitors' tickers (`market`). The host adds BLS Quarterly Census of Employment and Wages figures: US private establishments, employment and pay, with no key. It also adds SEC EDGAR 10-K revenue with growth. The SEC asks every requester to identify themselves, so SEC figures need a contact the owner sets in **Settings → Research data**; without one, the log says so and nothing is sent. Establishments are locations, not firms, and one-person businesses aren't counted: the source text says so, so the model can't overclaim.
  Set `smallBusinesses` when the buyers are small businesses. The host then adds US establishments by employee size (BLS, no key). With a free Census key in Settings → Research data, it also adds businesses with no employees at all (Census Nonemployer Statistics). The key stays in the system credential store and is sent only to census.gov.
- **The owner's site:** a priority may ask for a site check (`audit`) of the owner's own site on the research list. The host reuses a check from the last day or runs one (see Site check below) and gives the model its findings.

Only the host contacts these services, over public addresses, with size limits. Without the container (the scripted fixture) the headlines are skipped.

Create receives all of these as numbered sources and may cite only them. The host then:
- appends a source list, with where each one came from and its date, so every citation can be checked;
- records each source as evidence on the task, which puts it in Library → Research → Sources.

The document labels them as signals, not proof of demand.

## Site check

**Work → Site check** is a technical SEO read of the owner's own site; it must be on the research list.
- **What it reads:** up to 25 pages from the sitemap, or following the homepage's links, and up to 40 more internal links. Pages are read one at a time with a short pause, using the research guards.
- **What it reports:**
  - missing or overlong titles and descriptions;
  - missing or repeated H1s;
  - noindex;
  - images without alt text;
  - thin pages;
  - a canonical that points elsewhere;
  - duplicate titles and descriptions;
  - broken links, each with the page that links to it;
  - a missing robots.txt or sitemap.
- **Where it goes:** the report is a Library document in Research / SEO. Nothing on the site changes.

## Competitor pages

**Objectives → Pages to watch** lists up to ten competitor pages (pricing or plans), which must be on the research sites. The hourly listening pass reads each one at most once a day, with the same reader as research, so prices drawn by JavaScript are seen. It needs no model call.
- The first read is the baseline.
- A changed set of prices becomes a **competitor_change** signal for the next shift, with the page (before and after) as its source.
- A substantial change of wording is noted in Listening but isn't a signal, because pages change their copy all the time.
- A failed read keeps the last good baseline.

## Page copy for your site

**Objectives → Your site** names the owner's own site, which is always one of the research sites. A shift may deliver a **page**: new copy for one page on that site, written to replace what is there, with a reason.
- The host reads the live page and stores it beside the proposal. **Work → Page changes** shows the two side by side.
- The owner approves or rejects the proposal, with a reason the employee learns from.
- To apply an approved proposal, the owner copies the text into the site and marks it applied. Or, with WordPress connected, **Save as a WordPress draft page** creates a new page in draft status; the live page is never changed from here.
- In **Site check**, findings on the owner's own pages have **Ask for new copy**, which assigns the next shift a page task carrying what the check found.

## Replies

A draft whose destination is one specific public post (on X, Bluesky, Hacker News, Reddit, Threads, LinkedIn or Mastodon) is a reply. It goes out from that post: X gets its reply intent, with the text; elsewhere the post opens and the reply is on the clipboard. It never goes through a connected channel, which would post it as a new, standalone post; the host refuses that. In **Listening**, mentions on Bluesky and Hacker News have **Ask for a reply**, which assigns the next shift a task carrying the post's exact URL. The employee may decline if a reply wouldn't help.

## Images for posts

On any draft, **Make an image…** draws a post image in the browser from the draft's own words: a headline, a quote or a key number. It is sized for the network (1200×627 link preview, 1600×900 wide, 1080 square or 1080×1350 portrait), in dark, light or brand colours. It is saved to Library → Media, ready to download and attach. There is no image model and no GPU, and only the saved PNG leaves the browser.

## Videos

A task that asks for a video or clip gets a **video** deliverable. The employee writes a storyboard: a format (vertical 1080×1920, square or landscape), 4–8 scenes of on-screen words (up to 90 characters each, plus an optional second line), seconds per scene, a voiceover line and a note on footage the owner could add, and the caption to post it with. The host renders the video on this machine with ffmpeg:
- each scene is a branded card (night, paper or lime) with the owner's site in the corner and a progress bar across the whole clip;
- the first scene is written to work with the sound off.

The pieces are filed together:
- the MP4 goes to Library → Campaigns → Videos, beside the storyboard document (the script as a table plus its JSON block);
- the caption becomes a draft post for approval;
- the task waits with "Watch it, then review the caption".

Posting, with the video attached, stays with the owner. For a voiceover, **Record narration** on the storyboard records one clip per line in the owner's own voice, and **Render video** renders it again with those clips, each scene lasting as long as its line.

- **Checks:** a storyboard that is too long, or a self-review that breaks its JSON, is refused rather than trimmed.
- **ffmpeg:** it isn't bundled. Set `Marketing:Ffmpeg` if it isn't on the PATH; fonts come from the system or `Marketing:VideoFontBold` / `Marketing:VideoFont`. Without ffmpeg the storyboard is still saved, ready to render later.

## Series: several posts for one task

When a task asks for several pieces (three LinkedIn posts, an onboarding email sequence, one post per channel), the answer returns them as a list of 2–5 drafts. Each becomes its own draft, with its own channel and destination, and they are reviewed together. Drafts are shaped for where they go:
- social posts (LinkedIn, X, Bluesky, Mastodon, Threads, Facebook, Instagram) lose Markdown: headings become lines, `**bold**` becomes plain, and links are written out;
- Hacker News, Reddit and Product Hunt drafts lead with the `Title:` they are submitted under.

A self-review whose revision is much shorter than a long original was cut off by the answer limit, not edited, so it's discarded.

## Listening

The owner chooses what to listen to, under Objectives → **Listening**:
- **Watch topics:** up to ten, e.g. the product name, the category, competitors' names.
- **Feeds:** up to twenty RSS or Atom feeds, e.g. competitors' blogs, newsletters, news alerts.

**How it runs.** Listening is code, not model work:
- It runs in every shift's Sense stage and hourly between shifts, so baselines build up.
- **Work → Listening → Listen now** runs a pass on demand.
- Each topic is scanned through the employee's own `pulse` tool: Hacker News, Google News and Bluesky, all keyless. Reddit is included once credentials are set.
- Feeds are fetched by the host over HTTPS, confined to the feed's own site, public IPv4 only, at most 2 MB, with no DTDs or external entities.
- Mentions are kept for 30 days, deduplicated per topic.

**Sentiment.** Every mention gets a word-list label (positive, negative or neutral). It is cheap and explainable, and it is presented as a reason to read the mentions, not as a verdict.

**Signals.** Only two changes become signals, and only these get a model turn:
- **Mention spike:** at least 5 mentions in the last 24 hours, and at least 3 times the prior week's daily rate (high at 5 times).
- **Negative turn:** at least 40% of the last day's mentions read as negative, and at least 20 points above the prior week's share.

A topic needs history before it can spike: tracked for three days, or mentions on three of the prior seven. So adding a busy topic never alarms on its first backfill. Planning also gets a short digest: each topic's day against its week, and new feed posts from the last 48 hours. A competitor's post can become a task without being treated as an alarm. When a shift answers a signal, it writes from the actual mentions, cited like any other source.

**What a real pass looked like (September 25, no model calls):**
- 3 topics and 2 feeds gave 38 mentions in 13 seconds, from Bluesky, a company blog feed and Google News.
- One feed returned 404, and it was reported as such.

**Known limits:**
- Bluesky search includes bot posts that use a tag (weather bots posting `#OpenClaw`). A mute list or a minimum-quality filter is a sensible next step.
- Hacker News rarely matches narrow product names.
- Without Reddit credentials, Reddit's public search feed usually returns nothing.

**Reddit credentials** (optional): create a free "script" app at reddit.com/prefs/apps with the owner's Reddit account. Put `HARKEN_REDDIT_CLIENT_ID` and `HARKEN_REDDIT_CLIENT_SECRET` in the employee container's env file, then restart it. Mastodon needs `HARKEN_MASTODON_ACCESS_TOKEN` for full-text search on most instances.

**Open-source components.** Listening, sentiment, feed parsing and site reading are this project's own code. `pulse` wraps Harken (MIT). Harken's dependencies (feedparser, httpx, pydantic, typer, rich, FastAPI, uvicorn, Jinja2, python-dotenv) are MIT or BSD licensed.

## Review before the owner sees it

Every deliverable gets a second model turn, a critique, when the budget allows. It scores the work 1–5 on the creative-review rubric: strategy, customer truth, distinctiveness, channel fit, brand, a clear action, defensible claims, and shareability. It lists up to four issues. If anything scores 3 or lower, it returns a revision with the same facts and citations and no new claims.

The host keeps the original when the revision is missing, too short, or cites a source number that doesn't exist. It then records the result where the owner will see it: at the foot of a document, or in a draft's "Why this draft", for example *Self-review 3.5/5, revised: Generic: name the segment.* That makes each deliverable cost two turns, so a shift with a small budget produces fewer, better pieces.

The review sees the assignment (the task's title and next action) and judges the work against it. A format the owner asked for, such as a fenced JSON block a tool reads, a table or a length, is correct, never an issue. A revision that drops a fenced block the original had is discarded. Prioritize treats assigned tasks as the owner's instructions: it does them as written and never swaps one for a prerequisite it would rather do.

## Feedback and the Marketing notebook

The employee learns from the owner in two ways:
- **Verdicts.** Every document the employee wrote has a **Useful / Not useful** row with an optional reason. Every draft decision has an optional **Your reason** field. Verdicts are stored per item (the latest wins; 200 are kept) at `GET/POST /api/feedback`. Owners, managers and contributors can give them.
- **The notebook.** At the end of each shift the employee adds what it established to the **Marketing notebook** (Library → Company): what we know, what was decided, open questions, what worked, and what didn't. Each list is capped at 15, and answered questions are removed. The owner can edit the page freely. The next update starts from the owner's version of each list.

The latest twelve verdicts, with their reasons, and the notebook go into every prioritize, create and end-of-shift turn. When the owner rejects a draft, the reason is written onto the task that goes back to the queue ("The owner rejected draft #4 because: …").

Text meant for somewhere the host can't post to, such as a submission, a bio or an email body, is saved as a draft document in Library → Campaigns → Drafts for review, instead of being rejected.

## The scorecard: the employee's data

Successful marketing leaders run from **one scorecard**, not raw platform dashboards: a primary KPI,
leading indicators, spend and efficiency, funnel movement and current experiments. The employee does
the same. The scorecard is fed by:

- **CSV import.** Every platform exports CSV: Analytics, ad platforms, Shopify, Stripe, CRMs. Both
  *wide* (`date, sessions, signups, …`) and *long* (`date, metric, value`) layouts are accepted.
- **A published Google Sheet** (`File → Share → Publish to web → CSV`). The host fetches only
  `docs.google.com`.

Anomaly detection is plain statistics: the latest value against the trailing 14-day mean. A change is
flagged as material when it is at least 2.5 standard deviations or 25 %, and only metrics with 7 or
more points qualify. Future connectors (Analytics, Search Console, ads) write into the same scorecard
from Plow.

**Experiments the employee proposes.** When a scorecard metric could show whether an idea works, a shift can propose an experiment:
- a hypothesis, the one change, the metric, a length (7–42 days), a direction and a threshold worth acting on, and an ICE score;
- it's saved on the scorecard as **Proposed**, and the task tells the owner.

Nothing is measured until the owner presses **Start**; the experiment then runs from that day for its proposed length. **Decline** asks why, and the reason is kept on the experiment. A metric that isn't on the scorecard is refused rather than invented.

**Search queries.** With Search Console connected, each sync also keeps the last four weeks of queries by page. The employee acts on the ones ranked 4–20 with real impressions, most missed clicks first:
- they arrive in a shift as a signal ("N search queries within reach of page one");
- page-copy and blog work sees them, so titles and headings aim at real searches.

The rationale names the query targeted. `GET /api/data-connections/search-queries` returns the snapshot and the opportunities.

## Data connections

**Work → Scorecard → Connect data** links read-only analytics. The employee reads daily numbers and can never change anything in these tools.

| Source | Sign-in | Default metrics |
|---|---|---|
| **Google Analytics 4** | The owner's Google app (the one already set up for Gmail and Calendar), `analytics.readonly` | Sessions, Users, New users, Key events (optional: Engaged sessions, Page views) |
| **Search Console** | The same Google app, `webmasters.readonly` | Search clicks, Search impressions, Search CTR (%), Average search position (lower is better) |
| **Plausible** (open source, cloud or self-hosted) | An API key the owner creates in Plausible | Visitors, Visits, Page views, Bounce rate (%) |

**Google sign-in:**
- PKCE, with Google returning to this computer (`127.0.0.1`), so it only starts from the local workspace.
- After consent, the owner chooses the GA4 property or Search Console site from the ones the account can read.
- Search Console sites the account hasn't verified aren't offered.

**Google Cloud setup,** in the project behind the Google app:
- enable the **Google Analytics Data API**, **Google Analytics Admin API** and **Google Search Console API**;
- while the app is in testing, the owner's account must be a test user.

**Secrets.** Refresh tokens and API keys live only in the operating system's credential store. They never appear in the workspace data, the connection list or backups.

**Sync:**
- Every six hours in the background, and at the start of each shift, so Sense reads current numbers.
- The first sync reads 90 days; later syncs re-read the last ten days to pick up late data, replacing those days rather than adding to them.
- **Only complete days** are kept, whatever the source returns. Today is always partial, and Search Console runs about three days behind, so its last three days are left out. Otherwise they would read as a drop.
- Synced metrics are ordinary scorecard metrics: they feed anomaly detection, experiments and the north star.

**Not yet:** ads platforms, Stripe or other revenue sources, CRM, and Umami (its self-hosted API needs a login token rather than an API key).

## Publishing

Each person connects only the channels they post to, in **Settings → Publishing channels**:

| Channel | How it connects | Notes |
|---|---|---|
| **Bluesky** | Handle and an **app password** (Settings → Privacy and security → App passwords) | Links become clickable facets; 300 graphemes. Self-hosted servers are supported |
| **Mastodon** | Server address and an access token (Preferences → Development, `write:statuses` and `read:accounts`) | Sent with an idempotency key; 500 characters by default |
| **WordPress** | Site address, username and an **application password** (Users → Profile) | The first heading becomes the title; the owner can choose "save as WordPress draft" instead of publishing |
| **LinkedIn** | The person's own developer app ("Share on LinkedIn" and "Sign In with LinkedIn using OpenID Connect"), OAuth | Posts to the personal profile; access lasts about 60 days; the `LinkedIn-Version` header is set by the host (currently 202607) |
| **X** | The person's own developer app, OAuth 2.0 with PKCE | Tokens refresh automatically; links count as 23 characters. X has had no free tier since February 2026: pay-per-use is about $0.015 a post, **$0.20 for a post with a link**, $0.005 a read. Assisted posting (below) is free |
| **Email (Gmail drafts)** | The owner's saved Google app, `gmail.compose` | Approved emails are **saved to Gmail drafts, never sent**; the person sends them from Gmail. The employee writes emails as `Subject:` and optional `To:`/`Cc:` lines, a blank line, then the body. Addresses are validated; a missing subject is refused |

**The rules.** They extend the approval model, not replace it:
1. **Approving never publishes.** After approval, the draft card shows **Publish to …** and **Schedule**. The owner chooses, and confirms the exact text, channel and account.
2. **Only the exact approved text goes out.** The request carries the digest the owner reviewed; a changed draft is refused.
3. **Checks run before anything is sent:**
   - launch QA (a blocked draft can't be published);
   - the channel's own length rule;
   - the draft's channel must match the connected channel.
4. **Never twice.** One live, scheduled or uncertain publication per draft. Request IDs make retries replays.
5. **Uncertain outcomes wait for a person.** If the connection fails after sending, the post is marked "may or may not have been published" and is never retried automatically. The owner checks the channel, then records either "It was posted" (with its link) or "It wasn't posted".
6. **Scheduled posts** go out on time from the background pump (checked every 20 seconds), shift or not:
   - If the draft changed or is no longer approved by then, it isn't posted.
   - If the workspace wasn't running at the time and comes back **more than two hours late**, the post is marked **Missed** and held for the owner rather than going out late. It can be rescheduled or published now.
   - Times are chosen in the owner's own time zone, with quick picks (Tomorrow 7:00 AM, Tomorrow 9:00 AM, Monday 9:00 AM), and stored as exact UTC instants.
   - **Work → Content calendar** lists what's scheduled, what was missed or needs checking, and what went out in the last two weeks.
   - The host must be awake at the scheduled time.
7. **After publishing**, the employee's ledger marks the draft posted with its live link, the same receipt as posting by hand.

### Newsletters: Buttondown drafts

Connect Buttondown (a newsletter service with a free plan) with an API key. An approved **Newsletter** draft (a `Subject:` line, a blank line, then the issue in Markdown) is saved as a Buttondown draft; you review it and press Send in Buttondown. Buttondown sends an email the moment it is created unless it is created as a draft. So the host always asks for a draft, and a reply reporting any other status is recorded as a failure, with an alarm to check Buttondown. There is no scheduling from here and no results polling.

### Assisted posting: any network, no connection

Any approved draft can be posted by the owner through the network's own composer. **Post it yourself on X** opens the network's official compose link with the approved text. The text is also copied to the clipboard, because some networks ignore the prefill.

| Network | Composer |
|---|---|
| X | `x.com/intent/post?text=` |
| Bluesky | `bsky.app/intent/compose?text=` |
| Threads | `threads.net/intent/post?text=` (Meta web intent) |
| LinkedIn | `linkedin.com/feed/?shareActive=true&text=` (desktop only; paste from the clipboard elsewhere) |
| Mastodon | `<your server>/share?text=` (the server is asked for once) |
| Email | a `mailto:` with the subject and body |
| Anything else | the draft's destination, with the text on the clipboard |

The same checks apply as for publishing: approved, unchanged since review, launch QA, and the length rule. The post then waits for its link: paste the live URL and it is recorded as posted. Results are read back from the link:
- Bluesky: resolved through the public AppView, so no account is needed;
- Mastodon: the server's public status API;
- X: when X is connected;
- every channel: visits from the tracking link.

**Remind me at a time** sets a reminder instead of posting. At that time the post turns **Time to post**, and chat says "It's time to post the X post" with the composer one click away. The host never posts an assisted post itself.

The cockpit deliberately doesn't drive a network's website with a bot. X, like most networks, forbids automated use outside its API, and bot-driven accounts get locked.

Approved drafts that aren't out yet sit under **Ready to post** in the cockpit, separate from decisions. Tokens and app passwords live only in the operating system's credential store.

**Tested with stand-ins, not the live services:** the requests follow each service's published API, but LinkedIn and X in particular need the person's own app approved on their side. The LinkedIn redirect must be registered exactly as the dialog shows it: `http://127.0.0.1:<port>/api/publishing/oauth/callback`.

Drafts for email, Mastodon and a blog now reach publishing: email drafts point at Gmail, and Mastodon and blog drafts use the address of the channel the owner connected. Before, they were kept as documents because they had no posting address.

**Not yet:** sending email (only drafts), Outlook and Microsoft 365, Threads, Reddit posting, images and link previews, the employee proposing a posting time, and reading engagement back into the scorecard.

## Results: how posts did

The host reads each published post's own counts on a settling schedule: about an hour after posting, every 6 hours for the first day, every 12 hours until day 3, then daily until day 8.

| Channel | What comes back |
|---|---|
| Bluesky | Likes, reposts, replies, quotes (the public AppView, no key) |
| Mastodon | Favourites, boosts, replies |
| X | Likes, reposts, replies, quotes and impressions (API reads may be billed by X) |
| LinkedIn | Nothing directly: LinkedIn doesn't share personal-post analytics with self-serve apps |
| WordPress, email | No audience counts |

**Visits** come from the post's tracking link when Google Analytics is connected: sessions with the link's `utm_source` and `utm_campaign` since it went out. Posts that share a campaign and source share their visits.

**Where results show up:**
- the content calendar, per post;
- chat ("Posted to Bluesky. So far: 12 likes, 3 reposts, 17 visits.");
- the weekly update, including its best post;
- the employee's planning and end-of-shift notebook (`recentPosts`), which is told that small numbers are noise, not lessons.

## Weekly rhythm

**Work → This week** holds a **weekly plan** and a **weekly update**. By default they are written on Monday at 08:00 and Friday at 16:00, in the owner's time zone; the days and times can be changed. They are written from the records, at no model cost, and filed in Library → Reports → Weekly.
- **The plan:** the north star, this week's focus, the employee's queue, what waits on the owner, what goes out this week, experiments to decide, open questions, and where the numbers are.
- **The update:** a headline, the north star, each metric's last 7 days against the 7 before, what went out and how it did, work done, the owner's decisions, what waits, what people said on the watch topics, learnings, next week's focus, and model spend.

Each is written once per week. A plan isn't written for a week that's already over. **Write now** works any time. The update can also be saved as a Gmail draft to forward. Chat announces both: "Your weekly update is ready."

## Versions for other channels

Any draft offers **Versions for other channels…**: pick channels (LinkedIn, X, Bluesky, Mastodon, Threads, Email, Blog) and add a note if you like. The request goes to the employee in chat. It writes one new draft per channel with `hire draft add`:
- native to that channel and within its limit;
- the same facts;
- the tracking link's `utm_source` set to the channel;
- a rationale starting "Adapted from draft #N".

Each version arrives as its own draft to approve, and in chat as "I drafted a Bluesky post for you to review". Chat is told where drafts for each channel go and their limits, so it can also do this when asked in plain words.

## Working hours

**Cockpit → Shift → Set working hours** sets the days, start and end times, check-in interval, and model turns and token limit per day. The owner's browser time zone is saved with them.

On a working day, the first pump tick after the start time starts a shift that ends at the end time and writes its report. The shift carries the day's turn and token limits. The shift is named `schedule-<date>`, so the day can't start twice.
- A shift the owner stops is **not restarted that day**.
- A day the workspace wasn't running is skipped, not made up.
- Nothing starts in the last 15 minutes of the window.

The cockpit shows the hours and when the next shift starts. `GET/PUT /api/shifts/schedule`; the ledger is `shift-schedule-v1`, included in backups.

## Chat that acts

The main conversation does three things beyond talking.

**1. Updates in the employee's voice.** These are built by the host from its own records, not by the model, so they are always accurate and cost nothing. They are woven into the conversation by time:

| When | The update | One-click answers |
|---|---|---|
| A draft waits for review | "I drafted a LinkedIn post for you to review." (with an excerpt) | **Approve** · **Reject** (with an optional reason the employee learns from) · **Details** |
| A draft is approved but not out | "The LinkedIn post is approved. Want me to put it out?" | **Publish now** · **Tomorrow 7:00 AM** · **Other time…**, or **Connect a channel** when none is connected |
| A post is scheduled | "…scheduled for Sat 7:00 AM." | **Calendar** · **Details** |
| A scheduled post missed its time, or its outcome is uncertain | "…didn't go out: the workspace wasn't running. Pick a new time?" | **Open the draft** · **Calendar** |
| A post went out (last two days) | "Posted to LinkedIn." / "The email is in your Gmail drafts." | **View the post** / **Open in Gmail** |
| A shift is on, or has ended (last three days) | "My shift is done: 3 pieces of work, 1 waiting on you. Want to see the report?" | **Open the report** · **Shift log** |

Each update can be dismissed. **Details** and **Open** show the item in the work window beside the chat when there is room.

**2. Buttons in replies.** The employee is told the owner's local time, the drafts by ID, the connected channels, and how to offer actions. It may end a reply with up to three fenced `action` blocks, which the cockpit renders as cards that say exactly what they will do. The action types:
- `open`: any item, view or Work section;
- `approve` / `reject`;
- `schedule`: a pending draft is approved and scheduled in one click;
- `publish`;
- `shift`: start one, with a token limit;
- `watch` a topic, or follow a `feed`;
- `document`: save the reply to the Library.

The blocks never show as text. Nothing happens until the owner clicks. Anything public or costly asks for confirmation first, naming the text's destination and account. The host re-checks everything: approvals, digests, QA, channel limits and budgets. Only the owner sees buttons that change things; others see navigation only.

**3. Navigate anywhere.** Chat can point at any draft, task, document, page, the brief or objectives, a view (Library, Team, Settings, Work), or a Work section (content calendar, scorecard, listening, shift log, board).

## Budget and control

Only the owner starts a shift, choosing:
- its length (8, 16 or 24 hours);
- how often it cycles (default 60 minutes);
- a model-turn budget;
- optionally, a token limit (8,000 to 20,000,000; the owner's daily cap is 10,000,000, the default for live shifts and working hours).

The host counts the tokens the provider reports. The token limit is checked before each turn is sent: a work turn needs room for about 3,000 tokens plus 1,500 kept for the shift report, and the report needs its own 1,500. One turn can't be stopped midway, so the limit is close, not exact. Every critique turn costs as much as a writing turn. Live run 4 averaged about 2,800 tokens a turn. Pause, resume and stop take effect before the next stage.
Chat, the campaign runner and shifts share one execution gate, so they never run model turns at
the same time.

**When a live turn fails.** A turn whose outcome is uncertain stops the shift, and the meter bills its full reservation (25,000 tokens) until it's reconciled. Two proven cases release it:
- **Never reached the provider.** The meter refused it before reserving (an oversized packet, for one). The turn is released and the reservation refunded, and the shift log says nothing was spent.
- **The Gateway recorded the run as failed.** A provider refusal such as an expired sign-in (HTTP 401) is one. The host reads the Gateway's own audit trail (`shift-reconcile`, which gives the record a few seconds to appear) and releases the turn. Its usage stays unknown and the reservation stays billed, with a receipt. The stage fails with the provider's reason, and the next turn goes ahead.

Anything else, including a run the Gateway says succeeded whose answer never arrived, stays unknown and holds the employee until someone looks. If the provider keeps refusing, re-authenticate it in the employee container; the host never retries a refused turn by itself.

## Objectives: what the work is for

**Library → Company → Objectives & positioning** is a versioned record. It holds:
- the **north star**: a metric, a target, a date and why it matters, optionally tied to a scorecard metric so its progress is tracked;
- this quarter's **objectives**, each with key results;
- **positioning**: who it's for, the problem, what they use instead, why us, and **proof points**;
- **competitors**;
- the **current focus** and **non-goals**.

Every prioritize, create and end-of-shift turn receives it, together with the brief (claims and examples included) and the whole scorecard. Prioritize ranks work by contribution to the north star and respects the non-goals. Create uses only the proof points given.

The cockpit shows the north star's progress. Onboarding (links or interview) drafts all of this for review. Managers and the owner can edit it; teammates can read it.

## Running it for real

```bash
powershell -File scripts/start-marketing.ps1 -LiveShifts
```

That starts your real workspace with live shifts. Each shift turn is metered in the employee's receipt ledger, and a shift is capped by the turns you give it. For a disposable test against the real employee, use `scripts/start-campaign-fixture.ps1 -LiveShiftContainer marketing-business-hire`: work records stay in a throwaway ledger, and only model turns reach the employee.

The first live run spent 5,232 tokens over 6 turns, roughly 900–1,900 tokens per turn. An 8-hour shift checking in hourly with a 16-turn budget is therefore on the order of 20–30k tokens.
