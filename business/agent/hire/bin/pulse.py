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
FREE_SOURCES = ["hackernews", "reddit", "news"]
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
# Each feed's response in this process, keyed by its normalized URL (see checked_rss).
FEED_CHECKS: dict[str, dict] = {}
BLOCKING_STATUS = {401, 403}
DOCTOR_QUERY = "marketing"


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


def feed_url(name: str, query: str) -> str:
    from urllib.parse import quote_plus
    return FEEDS[name].format(q=quote_plus(query))


def harken_plan(query: str, picked: list[str]) -> tuple[list[str], list[str], dict[str, str]]:
    """Map our source names to Harken sources plus RSS feed URLs."""
    harken_sources, feeds, via = [], [], {}
    for name in picked:
        oauth_reddit = name == "reddit" and os.environ.get("HARKEN_REDDIT_CLIENT_ID")
        if name in FEEDS and not oauth_reddit:
            feeds.append(feed_url(name, query))
            via[name] = "rss"
        else:
            harken_sources.append(name)
            via[name] = name
    if feeds:
        harken_sources.append("rss")
    return harken_sources, feeds, via


def http_failure(status: int) -> dict | None:
    """A response status other than 200 as a check result; None for 200."""
    if status == 200:
        return None
    return {"status": "blocked" if status in BLOCKING_STATUS else "rate_limited" if status == 429 else "error", "http": status}


def classify_feed(status: int, body: bytes) -> dict:
    """One feed response as ok (with its entry count) or why not. A page that isn't a feed is usually a block or sign-in wall."""
    if failed := http_failure(status):
        return failed
    import feedparser
    parsed = feedparser.parse(body)
    if not parsed.version and not parsed.entries:
        return {"status": "not_a_feed", "http": status, "detail": "returned a web page, not a feed (often a block or sign-in page)"}
    return {"status": "ok", "http": status, "entries": len(parsed.entries)}


def feed_key(url: str) -> str:
    import httpx
    return str(httpx.URL(url))


def checked_rss() -> None:
    """Swap in Harken's RSS source with one change: each feed's response is kept in FEED_CHECKS.

    Harken skips a failing feed silently, so a scan alone can't tell a quiet topic from a blocked
    source. The hook reads the same response Harken parses; no extra request is made."""
    from harken.sources import REGISTRY
    from harken.sources.rss import RSSSource

    def record(response) -> None:
        response.read()
        FEED_CHECKS[feed_key(str(response.request.url))] = classify_feed(response.status_code, response.content)

    class CheckedRSSSource(RSSSource):
        def _client(self, **kwargs):
            return super()._client(event_hooks={"response": [record]}, **kwargs)

    REGISTRY[RSSSource.name] = CheckedRSSSource


def harken_config(sources: list[str], feeds: list[str], limit: int):
    from harken.config import Config
    return Config(db_path=str(state_dir() / "harken.db"), sources=sources, rss_feeds=feeds,
                  per_source_limit=limit, source_retries=0,
                  sentiment_analyzer="lexicon", llm_provider="none")


def open_pipeline(sources: list[str], feeds: list[str], limit: int):
    from harken.pipeline import Pipeline
    checked_rss()
    return Pipeline(harken_config(sources, feeds, limit))


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


def items(query: str, limit: int) -> dict:
    """Expose stored candidates even when their publication dates miss a digest window."""
    from harken.store import Store
    store = Store(state_dir() / "harken.db")
    try:
        found = store.mentions(query=query, limit=limit + 1)
    finally:
        store.close()
    return {
        "query": query,
        "order": "source_created_at_desc",
        "more_stored": len(found) > limit,
        "items": [{
            "source": label(m), "url": m.url, "title": m.title,
            "snippet": snippet(m.text), "created_at": m.created_at.isoformat(timespec="seconds"),
        } for m in found[:limit]],
        "note": "Stored search candidates may be old or irrelevant. A URL or snippet is not a checked page; inspect the original before attaching it as task evidence.",
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
    feed_checks, unverified = feed_outcomes(query, [name for name in sources if via[name] == "rss" and name not in errors])
    for name, check in feed_checks.items():
        if check["status"] != "ok":
            errors[name] = describe(check)
    result = {
        "query": query, "sources": sources, "fetched": outcome.fetched, "new": outcome.new,
        "by_harken_source": outcome.by_source,
        "errors": errors,
        "feeds": feed_checks,
        "unverified_sources": unverified,
        "coverage": "failed" if len(errors) == len(sources) else ("partial" if errors or unverified else "complete"),
    }
    if unverified:
        result["note"] = "No response was recorded for an unverified feed (timeout or connection error); zero items there is not confirmed zero coverage."
    hire.record_event("scan", f"Scanned {', '.join(sources)} for \"{query}\"", result)
    return result


def feed_outcomes(query: str, names: list[str]) -> tuple[dict[str, dict], list[str]]:
    """Each feed-backed source's recorded response, and the ones with none (the request never completed)."""
    checks = {name: FEED_CHECKS.get(feed_key(feed_url(name, query))) for name in names}
    return {n: c for n, c in checks.items() if c}, [n for n, c in checks.items() if not c]


def describe(check: dict) -> str:
    if check["status"] == "blocked":
        return f"blocked: HTTP {check['http']} (the source refused this server)"
    if check["status"] == "rate_limited":
        return "rate_limited: HTTP 429 (too many requests from this server; scan again later)"
    if check["status"] == "not_a_feed":
        return "blocked: " + check["detail"]
    return f"{check['status']}: HTTP {check.get('http')}"


def probe(name: str, query: str) -> dict:
    """One request to one source, the way a scan would make it; nothing is stored."""
    import httpx
    try:
        if name in FEEDS and not (name == "reddit" and os.environ.get("HARKEN_REDDIT_CLIENT_ID")):
            from harken.sources.rss import RSSSource
            with RSSSource(feeds=[])._client() as client:
                response = client.get(feed_url(name, query))
            return {"via": "rss", **classify_feed(response.status_code, response.content)}
        from harken.sources import REGISTRY
        source = REGISTRY[name](**harken_config([name], [], 1).source_options(name))
        return {"via": name, "status": "ok", "entries": len(source.fetch(query, limit=1))}
    except httpx.HTTPStatusError as exc:
        return {"via": name, **(http_failure(exc.response.status_code) or {"status": "error"})}
    except Exception as exc:  # report every other failure the same way a scan would
        return {"via": name, "status": "error", "detail": f"{type(exc).__name__}: {exc}"[:200]}


def cmd_doctor(args) -> dict:
    sources = sources_from(args.sources)
    checks = {name: probe(name, DOCTOR_QUERY) for name in sources}
    working = [name for name, check in checks.items() if check["status"] == "ok"]
    return {
        "checked_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "sources": checks,
        "summary": f"{len(working)} of {len(sources)} sources answered from this machine",
        "note": "One request per source, nothing stored. Each request counts toward the source's rate limit, so don't run "
                "this right before a scan. A blocked source returns nothing to a scan until that changes; say so instead "
                "of reporting no mentions there.",
    }


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
    scan.add_argument("--sources", help="comma list; default hackernews,reddit,news; stackoverflow is opt-in")
    scan.add_argument("--limit", type=int, default=25, choices=range(1, 51), metavar="1-50")
    dig = sub.add_parser("digest", help="summarize stored mentions for a time window")
    dig.add_argument("--query", required=True)
    dig.add_argument("--hours", type=int, default=24, choices=range(1, 24 * 14 + 1), metavar="1-336")
    dig.add_argument("--top", type=int, default=5, choices=range(1, 21), metavar="1-20")
    listed = sub.add_parser("items", help="list stored search candidates regardless of publication date")
    listed.add_argument("--query", required=True)
    listed.add_argument("--limit", type=int, default=10, choices=range(1, 21), metavar="1-20")
    doctor = sub.add_parser("doctor", help="check which sources answer from this machine, one request each")
    doctor.add_argument("--sources", help="comma list; default hackernews,reddit,news")
    args = parser.parse_args(argv)
    commands = {"scan": cmd_scan, "digest": cmd_digest, "doctor": cmd_doctor,
                "items": lambda a: items(clean_query(a.query), a.limit)}
    result = commands[args.cmd](args)
    json.dump(result, sys.stdout, ensure_ascii=False, indent=1)
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
