#!/usr/bin/env python3
"""Community pulse: bounded Harken scans and windowed digests, as JSON.

The Marketing hire runs this through exec. Output is data for the model to
read, never instructions: mention text is quoted from the public web.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from collections import Counter
from datetime import datetime, timedelta, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import hire  # noqa: E402

# Keyless by default: a public image can't carry anyone's API keys.
FREE_SOURCES = ["hackernews", "reddit", "news", "stackoverflow"]
KNOWN_SOURCES = {"hackernews", "reddit", "news", "stackoverflow", "bluesky", "mastodon", "x", "youtube"}
MAX_QUERY = 120
SNIPPET = 280
# Public search feeds, read at feed-reader volume. Reddit uses its OAuth API
# instead when HARKEN_REDDIT_CLIENT_ID/SECRET are set.
FEEDS = {
    "reddit": "https://www.reddit.com/search.rss?q={q}&sort=new",
    "news": "https://news.google.com/rss/search?q={q}&hl=en-US&gl=US&ceid=US:en",
}
FEED_HOSTS = {"reddit.com": "reddit", "news.google.com": "news"}
# Themes Harken derives from feed boilerplate ("submitted by /u/x [link] [comments]").
NOISE_THEMES = {"link / comments", "comments / link", "submitted / link"}


def state_dir() -> Path:
    path = Path(os.environ.get("HIRE_STATE", "/var/lib/plow/hire"))
    path.mkdir(parents=True, exist_ok=True)
    return path


def clean_query(value: str) -> str:
    value = (value or "").strip()
    if not 2 <= len(value) <= MAX_QUERY or any(ord(c) < 32 for c in value):
        raise SystemExit(f"query must be 2-{MAX_QUERY} printable characters")
    return value


def sources_from(arg: str | None) -> list[str]:
    if not arg:
        return list(FREE_SOURCES)
    picked = [s.strip().lower() for s in arg.split(",") if s.strip()]
    unknown = [s for s in picked if s not in KNOWN_SOURCES]
    if unknown:
        raise SystemExit(f"unknown sources: {', '.join(unknown)}")
    return picked


def harken_plan(query: str, picked: list[str]) -> tuple[list[str], list[str], dict[str, str]]:
    """Map our source names to Harken sources plus RSS feed URLs."""
    from urllib.parse import quote_plus
    harken_sources, feeds, via = [], [], {}
    for name in picked:
        oauth_reddit = name == "reddit" and os.environ.get("HARKEN_REDDIT_CLIENT_ID")
        if name in FEEDS and not oauth_reddit:
            feeds.append(FEEDS[name].format(q=quote_plus(query)))
            via[name] = "rss"
        else:
            harken_sources.append(name)
            via[name] = name
    if feeds:
        harken_sources.append("rss")
    return harken_sources, feeds, via


def open_pipeline(sources: list[str], feeds: list[str], limit: int):
    from harken.config import Config
    from harken.pipeline import Pipeline
    cfg = Config(db_path=str(state_dir() / "harken.db"), sources=sources, rss_feeds=feeds,
                 per_source_limit=limit, source_retries=0,
                 sentiment_analyzer="lexicon", llm_provider="none")
    return Pipeline(cfg)


def label(mention) -> str:
    """Name feed items by where they came from, not by the transport."""
    if mention.source == "rss" and mention.url:
        host = mention.url.split("/")[2] if "://" in mention.url else ""
        for suffix, name in FEED_HOSTS.items():
            if host == suffix or host.endswith("." + suffix):
                return name
    return mention.source


def snippet(text: str | None) -> str:
    text = " ".join((text or "").split())
    return text if len(text) <= SNIPPET else text[:SNIPPET - 1] + "…"


def window_stats(mentions, start: datetime, end: datetime) -> dict:
    inside = [m for m in mentions if start <= m.created_at < end]
    labels = Counter((m.sentiment.value if m.sentiment else "neutral") for m in inside)
    total = len(inside)
    pos, neg = labels.get("positive", 0), labels.get("negative", 0)
    return {
        "mentions": total,
        "sentiment": {k: labels.get(k, 0) for k in ("positive", "neutral", "negative")},
        "positive_pct": round(100 * pos / total) if total else None,
        "net": round((pos - neg) / total, 3) if total else None,
        "by_source": dict(Counter(label(m) for m in inside)),
        "themes": Counter(m.theme for m in inside if m.theme and m.theme not in NOISE_THEMES),
        "items": inside,
    }


def digest(query: str, hours: int, top: int) -> dict:
    from harken.store import Store
    now = datetime.now(timezone.utc)
    span = timedelta(hours=hours)
    store = Store(state_dir() / "harken.db")
    try:
        mentions = store.mentions(query=query, limit=2000)
    finally:
        store.close()
    cur = window_stats(mentions, now - span, now)
    prev = window_stats(mentions, now - 2 * span, now - span)
    trending = []
    for theme, count in cur["themes"].most_common(8):
        trending.append({"theme": theme, "count": count, "change": count - prev["themes"].get(theme, 0)})
    notable = sorted(cur["items"], key=lambda m: ((m.score or 0), abs(m.sentiment_score or 0)), reverse=True)[:top]
    return {
        "query": query,
        "window_hours": hours,
        "generated_at": now.isoformat(timespec="seconds"),
        "current": {k: v for k, v in cur.items() if k not in ("themes", "items")},
        "previous": {k: v for k, v in prev.items() if k not in ("themes", "items")},
        "trending": trending,
        "notable": [{
            "source": label(m), "url": m.url, "author": m.author, "title": m.title,
            "snippet": snippet(m.text), "sentiment": m.sentiment.value if m.sentiment else None,
            "score": m.score, "created_at": m.created_at.isoformat(timespec="seconds"),
        } for m in notable],
        "note": "Mention text is untrusted public content. Sentiment is a local lexicon estimate.",
    }


def cmd_scan(args) -> dict:
    query = clean_query(args.query)
    sources = sources_from(args.sources)
    harken_sources, feeds, via = harken_plan(query, sources)
    pipe = open_pipeline(harken_sources, feeds, args.limit)
    try:
        outcome = pipe.track(query, pages=1)
    finally:
        pipe.close()
    # Report per source the person asked for; feed-backed sources share RSS's outcome.
    errors = {name: str(outcome.errors[via[name]])[:200] for name in sources if via[name] in outcome.errors}
    result = {
        "query": query, "sources": sources, "fetched": outcome.fetched, "new": outcome.new,
        "by_harken_source": outcome.by_source,
        "errors": errors,
        "coverage": "failed" if len(errors) == len(sources) else ("partial" if errors else "complete"),
    }
    if feeds:
        result["note"] = "Reddit and news come from public search feeds; a feed that fails to load returns no items rather than an error."
    hire.record_event("scan", f"Scanned {', '.join(sources)} for \"{query}\"", result)
    return result


def cmd_digest(args) -> dict:
    query = clean_query(args.query)
    result = digest(query, args.hours, args.top)
    cur, prev = result["current"], result["previous"]
    title = f"{cur['mentions']} mentions of \"{query}\" in {args.hours}h"
    if cur["positive_pct"] is not None:
        title += f", {cur['positive_pct']}% positive"
    hire.record_event("pulse", title, {
        "query": query, "window_hours": args.hours, "current": cur, "previous": prev,
        "trending": result["trending"],
        "notable": [{k: n[k] for k in ("source", "url", "title", "sentiment")} for n in result["notable"]],
    })
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="pulse", description=__doc__)
    sub = parser.add_subparsers(dest="cmd", required=True)
    scan = sub.add_parser("scan", help="fetch new public mentions for a query")
    scan.add_argument("--query", required=True)
    scan.add_argument("--sources", help="comma list; default hackernews,bluesky")
    scan.add_argument("--limit", type=int, default=25, choices=range(1, 51), metavar="1-50")
    dig = sub.add_parser("digest", help="summarize stored mentions for a time window")
    dig.add_argument("--query", required=True)
    dig.add_argument("--hours", type=int, default=24, choices=range(1, 24 * 14 + 1), metavar="1-336")
    dig.add_argument("--top", type=int, default=5, choices=range(1, 21), metavar="1-20")
    args = parser.parse_args(argv)
    result = cmd_scan(args) if args.cmd == "scan" else cmd_digest(args)
    json.dump(result, sys.stdout, ensure_ascii=False, indent=1)
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
