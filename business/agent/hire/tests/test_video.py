"""Video transcript contract checks: link parsing, caption choice and paging, with YouTube faked."""

import argparse
import json
import os
import sqlite3
import sys
import tempfile
import unittest
from contextlib import closing
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "bin"))
import video  # noqa: E402

TRACK = [{"ext": "json3", "url": "https://www.youtube.com/api/timedtext?x", "name": "English"}]


def info(**fields):
    return {"id": "arj7oStGLkU", "title": "A talk", "channel": "Someone", "upload_date": "20160406",
            "duration": 844, "description": "  About   the talk ", **fields}


class VideoIdTests(unittest.TestCase):
    def test_every_common_form_of_one_video_link_gives_its_id(self):
        for url in ("https://www.youtube.com/watch?v=arj7oStGLkU", "https://youtu.be/arj7oStGLkU?t=10",
                    "https://www.youtube.com/watch?feature=share&v=arj7oStGLkU&list=PL1", "youtube.com/watch?v=arj7oStGLkU",
                    "https://m.youtube.com/watch?v=arj7oStGLkU", "https://www.youtube.com/shorts/arj7oStGLkU",
                    "https://www.youtube.com/live/arj7oStGLkU?si=x", "https://www.youtube-nocookie.com/embed/arj7oStGLkU"):
            self.assertEqual("arj7oStGLkU", video.video_id(url), url)

    def test_anything_but_one_video_is_refused_with_what_to_send_instead(self):
        cases = {"https://www.youtube.com/playlist?list=PL1": "playlist", "https://www.youtube.com/@ted": "channel",
                 "https://www.youtube.com/channel/UC1": "channel", "https://vimeo.com/arj7oStGLkU": "one YouTube video link",
                 "https://www.youtube.com/watch?v=short": "one YouTube video link",
                 "javascript:alert(1)//youtu.be/arj7oStGLkU": "one YouTube video link",
                 "https://evil.example/youtu.be/arj7oStGLkU": "one YouTube video link"}
        for url, said in cases.items():
            with self.assertRaises(SystemExit) as refused:
                video.video_id(url)
            self.assertIn(said, str(refused.exception), url)


class TrackTests(unittest.TestCase):
    def test_the_creators_captions_come_before_speech_recognition(self):
        found = video.pick_track(info(subtitles={"en-US": TRACK, "live_chat": TRACK}, automatic_captions={"en-orig": TRACK, "en": TRACK}), None)
        self.assertEqual(("en-US", "manual"), found[:2])

    def test_without_uploaded_captions_the_spoken_language_is_read(self):
        found = video.pick_track(info(language="es", automatic_captions={"es-orig": TRACK, "es": TRACK, "en": TRACK}), None)
        self.assertEqual(("es-orig", "automatic"), found[:2])

    def test_another_language_is_a_labelled_machine_translation(self):
        found = video.pick_track(info(automatic_captions={"es-orig": TRACK, "es": TRACK, "en": TRACK}), "en")
        self.assertEqual(("en", "translated"), found[:2])

    def test_missing_captions_say_what_exists_or_that_there_is_nothing(self):
        only_german = info(subtitles={"de": TRACK}, automatic_captions={"de-orig": TRACK})
        self.assertIsNone(video.pick_track(only_german, "fr"))
        self.assertIn("Captions exist in: de", video.no_captions_message(only_german, "fr"))
        self.assertIn("has no captions", video.no_captions_message(info(subtitles={"live_chat": TRACK}), None))


class TextTests(unittest.TestCase):
    def test_captions_become_timestamped_half_minute_paragraphs(self):
        events = {"events": [{"tStartMs": 0}, {"tStartMs": 1200, "segs": [{"utf8": "Hello "}, {"utf8": " there"}]},
                             {"tStartMs": 2000, "segs": [{"utf8": "Hello there"}]}, {"tStartMs": 9000, "aAppend": 1, "segs": [{"utf8": "\n"}]},
                             {"tStartMs": 29000, "segs": [{"utf8": "still the first"}]},
                             {"tStartMs": 31000, "segs": [{"utf8": "a new\nparagraph"}]},
                             {"tStartMs": 3_725_000, "segs": [{"utf8": "an hour in"}]}]}
        self.assertEqual("[00:01] Hello there still the first\n[00:31] a new paragraph\n[1:02:05] an hour in",
                         video.transcript_text(video.transcript_lines(events)))

    def test_pages_end_on_a_word_and_say_where_the_next_starts(self):
        text = "one two three four five six seven"
        chunk, next_offset = video.page(text, 0, 12)
        self.assertEqual(("one two", 7), (chunk, next_offset))
        self.assertEqual(("three four", 18), video.page(text, next_offset, 12))
        self.assertEqual(("seven", None), video.page(text, 27, 12))
        with self.assertRaises(SystemExit):
            video.page(text, len(text), 12)

    def test_failures_are_named_for_what_the_owner_can_do(self):
        self.assertTrue(video.failure("ERROR: [youtube] x: Sign in to confirm you’re not a bot").startswith("blocked:"))
        self.assertTrue(video.failure("ERROR: [youtube] x: Private video. Sign in").startswith("unavailable:"))
        self.assertTrue(video.failure("ERROR: [youtube] x: This video is unavailable").startswith("unavailable:"))
        self.assertTrue(video.failure("<urlopen error timed out>").startswith("failed:"))


class TranscriptCommandTests(unittest.TestCase):
    def setUp(self):
        self.state = tempfile.TemporaryDirectory(prefix="video-test-")
        self.addCleanup(self.state.cleanup)
        patcher = mock.patch.dict(os.environ, {"HIRE_STATE": self.state.name})
        patcher.start()
        self.addCleanup(patcher.stop)

    def run_command(self, **args):
        data = {"events": [{"tStartMs": i * 10_000, "segs": [{"utf8": f"sentence number {i}."}]} for i in range(40)]}
        fetched = (info(), ("en-orig", "automatic", TRACK), data)
        with mock.patch.object(video, "fetch", return_value=fetched) as fetch:
            result = video.cmd_transcript(argparse.Namespace(**{"lang": None, "offset": 0, "max_chars": 2000, **args}))
        fetch.assert_called_once_with("arj7oStGLkU", args.get("lang"))
        return result

    def events(self):
        with closing(sqlite3.connect(Path(self.state.name) / "hire.sqlite")) as conn:
            return [(kind, json.loads(data)) for kind, data in conn.execute("SELECT kind, data FROM events")]

    def test_a_transcript_names_its_source_and_is_recorded_once(self):
        first = self.run_command(url="https://youtu.be/arj7oStGLkU", max_chars=2000)
        self.assertEqual("https://www.youtube.com/watch?v=arj7oStGLkU", first["video"]["url"])
        self.assertEqual(("2016-04-06", "About the talk"), (first["video"]["published"], first["video"]["description"]))
        self.assertEqual({"language": "en", "kind": "automatic", "source": "YouTube's speech recognition", "name": "English"}, first["captions"])
        self.assertTrue(first["transcript"].startswith("[00:00] sentence number 0. sentence number 1."))
        self.assertIsNone(first["next_offset"])
        self.assertEqual([("transcript", {"url": first["video"]["url"], "title": "A talk", "channel": "Someone",
                                          "language": "en", "kind": "automatic", "chars": first["total_chars"]})], self.events())

    def test_later_pages_continue_without_another_event(self):
        first = self.run_command(url="https://youtu.be/arj7oStGLkU", max_chars=2000)
        self.assertGreater(first["total_chars"], 500)
        page = self.run_command(url="https://youtu.be/arj7oStGLkU", offset=500, max_chars=2000)
        self.assertNotIn("description", page["video"])
        self.assertEqual(1, len(self.events()))


if __name__ == "__main__":
    unittest.main()
