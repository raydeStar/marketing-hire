# Research experience for the MVP

The product should be useful before the owner connects a search account. Finish
the current manual QA and supported installation path before adding providers,
benchmark campaigns or sandbox backends. This is a product recommendation; future
items below are not implemented features or new MVP acceptance gates.

## Available now

- Search the study's saved content from the sidebar, without a web-search API.
- Research selected notes and supplied public links with public search off.
  Links still pass the existing retrieval permissions and bounds. Model usage
  remains separate and can have a cost.
- Use Feed subscriptions and saved links to collect recurring reading without
  repeating discovery queries. Feeds are chosen by the owner, not an open-ended
  autonomous search loop.
- Enable Brave per research task when discovery is useful and the account's
  terms fit the current retention behavior. Search is off by default, with a
  one-to-four query task allowance and a visible study-wide monthly cap of 100.
  Zero pauses searches; failures and uncertain attempts remain counted.
- Run routine development checks against fictional provider responses. They
  consume zero Brave queries. A real provider-quality check is a separate,
  deliberately bounded activity, not a prerequisite for every code change.

The study counter cannot measure another application or study using the same
account, and restoring an older backup can undercount account usage. Use the
provider's own spending controls as well. See [public search](SEARCH_CONNECTIONS.md).

## Brave's free credit and the retention mismatch

Checked on September 14, 2026: Brave's Search plan advertises $5 per 1,000 requests
and $5 in monthly credit, equivalent to about 1,000 searches. Its FAQ describes
$0 prepaid signup, usage limits, and optional automatic balance reload. An owner
seeking zero purchased usage should keep automatic reload off and use the
provider-side limit; do not infer their current billing settings from Thaddeus.
Sources: [pricing](https://brave.com/search/api/) and
[billing FAQ](https://api-dashboard.search.brave.com/documentation/resources/help-feedback).

The same FAQ currently prohibits retaining API-returned data under its standard
terms and directs customers who need storage to contact Brave. Thaddeus currently
retains search results and snippets in task history for replay, and connection
setup requires confirmation of storage rights. Free credits alone do not make
that retained-results design suitable for an ordinary account. Do not market
the present Brave connection as an unconditional free research service, or build
a result cache around that assumption.

For this MVP, keep supplied-source research usable without Brave. Users can find
a page in their own browser and supply its URL. Automatic public discovery stays
an optional connection with its actual requirements disclosed. The independent
page retrieval and model costs are distinct from search-provider quota.

## Recommended follow-up after manual QA

1. Make the source choice easy to understand: saved material, supplied links,
   or public discovery. A request should explain why a search is needed before
   spending its allowance.
2. Make allowance exhaustion a useful handoff: show what was learned, preserve
   the work, and invite a supplied URL or later continuation. Do not silently
   switch providers or buy more quota.
3. Consider discovery without retained provider results only after designing
   its history/export behavior and checking the chosen provider's terms. The
   current replay architecture would need changes; this is not a toggle that
   already exists.
4. Evaluate another provider only if manual use exposes a real quality, terms
   or cost problem. A self-hosted search aggregator still needs upstream search
   engines and operational support; it is not a prerequisite for this launch.

Keep the agent's execution boundary: scoped files, brokered credentials, bounded
network access and explicit imports. Defer alternate VM/container backends. The
user should experience a task completing and an artifact arriving; routine UI
should not make them manage virtualization internals.
