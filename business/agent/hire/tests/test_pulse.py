"""Pulse source checks against faked HTTP: a blocked feed is named, never mistaken for a quiet topic.

Needs the image's pulse environment (Harken and its lock); skipped elsewhere.
"""

import argparse
import importlib.util
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "bin"))
HAVE_HARKEN = importlib.util.find_spec("harken") is not None and importlib.util.find_spec("feedparser") is not None
if HAVE_HARKEN:
    import httpx
    import pulse

RSS = b"""<?xml version="1.0"?><rss version="2.0"><channel><title>t</title>
<item><title>Teal Fern launches a planner</title><link>https://news.example/a</link>
<description>Teal Fern planner review</description><pubDate>Fri, 02 Oct 2026 10:00:00 GMT</pubDate></item>
</channel></rss>"""
BLOCK_PAGE = b"<!doctype html><html><head><title>Just a moment...</title></head><body>Checking your browser</body></html>"


@unittest.skipUnless(HAVE_HARKEN, "needs Harken, feedparser and httpx (the pulse image environment)")
class PulseSourceTests(unittest.TestCase):
    def setUp(self):
        self.state = tempfile.TemporaryDirectory(prefix="pulse-test-")
        self.addCleanup(self.state.cleanup)
        for patcher in (mock.patch.dict(os.environ, {"HIRE_STATE": self.state.name}), mock.patch.dict(pulse.FEED_CHECKS, clear=True)):
            patcher.start()
            self.addCleanup(patcher.stop)
        self.requests = []

    def serve(self, routes):
        """Answer every client the sources make from routes {host: response or exception}."""
        real = httpx.Client

        def handler(request):
            self.requests.append(request.url.host)
            answer = routes[request.url.host]
            if isinstance(answer, Exception):
                raise answer
            return answer

        patcher = mock.patch("httpx.Client", lambda *a, **kw: real(*a, transport=httpx.MockTransport(handler), **kw))
        patcher.start()
        self.addCleanup(patcher.stop)

    def scan(self, sources):
        return pulse.cmd_scan(argparse.Namespace(query="Teal Fern", sources=sources, limit=10))

    def test_feed_responses_are_classified(self):
        self.assertEqual({"status": "blocked", "http": 403}, pulse.classify_feed(403, b""))
        self.assertEqual({"status": "rate_limited", "http": 429}, pulse.classify_feed(429, b""))
        self.assertEqual({"status": "error", "http": 502}, pulse.classify_feed(502, b""))
        self.assertEqual("not_a_feed", pulse.classify_feed(200, BLOCK_PAGE)["status"])
        self.assertEqual({"status": "ok", "http": 200, "entries": 1}, pulse.classify_feed(200, RSS))

    def test_a_blocked_feed_is_an_error_beside_a_working_one_with_no_extra_request(self):
        self.serve({"www.reddit.com": httpx.Response(403, content=b"blocked"), "news.google.com": httpx.Response(200, content=RSS)})
        result = self.scan("reddit,news")
        self.assertEqual(["www.reddit.com", "news.google.com"], self.requests)
        self.assertEqual({"reddit": "blocked: HTTP 403 (the source refused this server)"}, result["errors"])
        self.assertEqual({"status": "ok", "http": 200, "entries": 1}, result["feeds"]["news"])
        self.assertEqual(([], "partial", 1), (result["unverified_sources"], result["coverage"], result["fetched"]))

    def test_a_challenge_page_and_a_rate_limit_are_named(self):
        self.serve({"www.reddit.com": httpx.Response(429), "news.google.com": httpx.Response(200, content=BLOCK_PAGE)})
        result = self.scan("reddit,news")
        self.assertTrue(result["errors"]["reddit"].startswith("rate_limited: HTTP 429"))
        self.assertTrue(result["errors"]["news"].startswith("blocked: returned a web page"))
        self.assertEqual("failed", result["coverage"])

    def test_a_feed_that_never_answered_stays_unverified(self):
        self.serve({"www.reddit.com": httpx.ConnectTimeout("slow"), "news.google.com": httpx.Response(200, content=RSS)})
        result = self.scan("reddit,news")
        self.assertEqual((["reddit"], {}, "partial"), (result["unverified_sources"], result["errors"], result["coverage"]))
        self.assertIn("No response was recorded", result["note"])

    def test_all_feeds_answering_is_complete_coverage(self):
        self.serve({"www.reddit.com": httpx.Response(200, content=RSS), "news.google.com": httpx.Response(200, content=RSS)})
        result = self.scan("reddit,news")
        self.assertEqual(({}, [], "complete"), (result["errors"], result["unverified_sources"], result["coverage"]))
        self.assertNotIn("note", result)

    def test_doctor_checks_each_source_once_and_stores_nothing(self):
        self.serve({"hn.algolia.com": httpx.Response(200, json={"hits": [], "nbPages": 1}),
                    "www.reddit.com": httpx.Response(429), "news.google.com": httpx.Response(200, content=RSS)})
        report = pulse.cmd_doctor(argparse.Namespace(sources=None))
        self.assertEqual(["hn.algolia.com", "www.reddit.com", "news.google.com"], self.requests)
        self.assertEqual({"via": "hackernews", "status": "ok", "entries": 0}, report["sources"]["hackernews"])
        self.assertEqual({"via": "rss", "status": "rate_limited", "http": 429}, report["sources"]["reddit"])
        self.assertEqual("2 of 3 sources answered from this machine", report["summary"])
        self.assertEqual([], [p.name for p in Path(self.state.name).iterdir()])

    def test_doctor_names_a_blocked_api_source(self):
        self.serve({"hn.algolia.com": httpx.Response(403)})
        report = pulse.cmd_doctor(argparse.Namespace(sources="hackernews"))
        self.assertEqual({"via": "hackernews", "status": "blocked", "http": 403}, report["sources"]["hackernews"])
        json.dumps(report)


if __name__ == "__main__":
    unittest.main()
