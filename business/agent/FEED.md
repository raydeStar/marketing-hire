# Activity feed contract (v1)

The hire records every consequential step in its ledger. `hire feed` returns
those events in order; this is the contract a cockpit or web app reads.
Changing a field's meaning or removing one requires a new `feed_version`.
Adding a new event kind or a new optional field does not.

```sh
hire feed --since 0 --limit 200
```

```json
{
  "feed_version": 1,
  "events": [
    {"id": 7, "ts": 1790000000.0, "kind": "draft", "title": "Draft #3 for reddit awaits approval",
     "data": {"draft": 3, "revision": 1, "replaces": null, "channel": "reddit", "destination": "https://…",
              "content": "…", "rationale": "…", "rules_url": "https://… or UNVERIFIED"}}
  ],
  "next_since": 7
}
```

Poll with `--since <next_since>`. Event ids only increase. `ts` is Unix seconds.

| kind | data |
| --- | --- |
| `scan` | `query`, `sources`, `fetched`, `new`, `by_harken_source`, `errors`, `coverage` (`complete`/`partial`/`failed`) |
| `pulse` | `query`, `window_hours`, `current`, `previous`, `trending[] {theme,count,change}`, `notable[] {source,url,title,sentiment}` |
| `watch` | `query`, `sources` (added or removed; title says which) |
| `draft` | `draft`, `revision`, `replaces`, `channel`, `destination`, `content`, `rationale`, `rules_url` |
| `decision` | `draft`, `decision` (`approved`/`rejected`), `by`, `note` |
| `posted` | `draft`, `url` |
| `campaign` | free-form object written by the campaign-desk skill (goal, by, audience, channels, hypothesis) |
| `checkpoint` | `recommendation` (`continue`/`pivot`/`pause`), `evidence[]`, `uncertain[]`, `question` |
| `note`, `report` | free-form object |

`current`/`previous` in a pulse: `mentions`, `sentiment {positive,neutral,negative}`,
`positive_pct` (null when there are no mentions), `net`, `by_source`.

Event text that came from the public web (titles, snippets, draft destinations)
is untrusted. A consumer must escape it before display and never execute it.
