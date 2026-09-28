"""Offline checks against the packaged official client; no Index credentials."""

import contextlib
import ctypes
import importlib.util
import io
import json
import os
from pathlib import Path
import sqlite3
import tempfile
import time
import unittest
from unittest.mock import patch

from index_transcripts import MAX_EVENT_BYTES, _zstd, read_transcript_rows


def compress(raw):
    lib = _zstd()
    lib.ZSTD_compressBound.argtypes = [ctypes.c_size_t]
    lib.ZSTD_compressBound.restype = ctypes.c_size_t
    lib.ZSTD_compress.argtypes = [ctypes.c_void_p, ctypes.c_size_t,
                                 ctypes.c_void_p, ctypes.c_size_t, ctypes.c_int]
    lib.ZSTD_compress.restype = ctypes.c_size_t
    out = ctypes.create_string_buffer(lib.ZSTD_compressBound(len(raw)))
    size = lib.ZSTD_compress(out, len(out), raw, len(raw), 3)
    assert not lib.ZSTD_isError(size)
    return out.raw[:size]


def event(response, input_tokens, output_tokens, cache_read=0, cache_write=0):
    return json.dumps({"message": {"role": "assistant", "model": "fixture-model",
        "responseId": response, "usage": {"input": input_tokens, "output": output_tokens,
        "cacheRead": cache_read, "cacheWrite": cache_write}, "content": "Fictional café"}},
        ensure_ascii=False).encode("utf-8")


class TranscriptTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="hirezero-index-test-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        store = self.root / "agents/main/agent/openclaw-agent.sqlite"
        store.parent.mkdir(parents=True)
        self.db = sqlite3.connect(store)
        self.addCleanup(self.db.close)
        self.db.execute("CREATE TABLE transcript_events (event_json TEXT, created_at INTEGER, "
                        "event_zstd BLOB, event_utf8_bytes INTEGER)")
        self.stamp = int(time.time() * 1000)
        spec = importlib.util.spec_from_file_location("index_client",
            os.environ.get("INDEX_CLIENT_PATH", "/opt/plow/agent-index-client.py"))
        self.client = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.client)

    def add(self, raw, *, compressed=False, stamp=None):
        self.db.execute("INSERT INTO transcript_events VALUES (?, ?, ?, ?)",
            (None if compressed else raw.decode("utf-8"), self.stamp if stamp is None else stamp,
             compress(raw) if compressed else None, len(raw) if compressed else None))
        self.db.commit()

    def corrupt(self, blob=b"not a zstd frame", size=12):
        self.db.execute("INSERT INTO transcript_events VALUES (NULL, ?, ?, ?)",
                        (self.stamp, blob, size))
        self.db.commit()

    def test_mixed_storage_counts_each_response_once(self):
        self.add(event("one", 100, 7, 20, 3))
        self.add(event("two", 200, 11, 30, 5), compressed=True)
        self.add(event("one", 100, 7, 20, 3), compressed=True)
        self.add(event("old", 999, 999), compressed=True, stamp=1)
        days = self.client.merge(self.client.from_openclaw(28, str(self.root)))
        self.assertEqual(self.client.FAILURES, [])
        self.assertEqual(len(days), 1)
        self.assertEqual(days[0]["models"], [{"model": "fixture-model", "input": 300,
            "output": 18, "cache_read": 50, "cache_write": 8}])

    def test_legacy_database_and_cutoff(self):
        self.db.execute("DROP TABLE transcript_events")
        self.db.execute("CREATE TABLE transcript_events (event_json TEXT, created_at INTEGER)")
        self.db.executemany("INSERT INTO transcript_events VALUES (?, ?)",
                            [('{}', 99), ('{"message": {}}', 100)])
        self.assertEqual(read_transcript_rows(self.db, 100), [('{"message": {}}', 100)])

    def test_compressed_utf8_round_trip(self):
        raw = event("unicode", 1, 2)
        self.add(raw, compressed=True)
        self.assertEqual(read_transcript_rows(self.db, 0), [(raw.decode("utf-8"), self.stamp)])

    def test_missing_compressed_payload_refuses_collection(self):
        self.corrupt(blob=None)
        self.assertEqual(self.client.from_openclaw(28, str(self.root)), {})
        self.assertEqual(len(self.client.FAILURES), 1)

    def test_bad_frames_or_sizes_refuse_collection(self):
        raw = event("x", 1, 2)
        for blob, size in [(compress(raw)[:-3], len(raw)), (compress(raw), len(raw) + 1),
                           (compress(raw), len(raw) - 1), (compress(raw), 0),
                           (compress(raw), MAX_EVENT_BYTES + 1)]:
            with self.subTest(size=size, length=len(blob)):
                self.db.execute("DELETE FROM transcript_events")
                self.corrupt(blob, size)
                with self.assertRaises(sqlite3.DatabaseError):
                    read_transcript_rows(self.db, 0)

    def test_invalid_compressed_json_refuses_collection(self):
        for raw in (b"not json", b"[]", b'"text"', b"\xff"):
            with self.subTest(raw=repr(raw)):
                self.db.execute("DELETE FROM transcript_events")
                self.corrupt(compress(raw), len(raw))
                with self.assertRaises(sqlite3.DatabaseError):
                    read_transcript_rows(self.db, 0)

    def test_missing_size_schema_refuses_collection(self):
        self.db.execute("ALTER TABLE transcript_events DROP COLUMN event_utf8_bytes")
        with self.assertRaises(sqlite3.DatabaseError):
            read_transcript_rows(self.db, 0)

    def worker(self, response='worker-one', inputs=50, outputs=10, *, status='reported', fixture=False, stamp=None, corrupt=False):
        folder = self.root / 'hire'
        folder.mkdir(exist_ok=True)
        with sqlite3.connect(folder / 'hire.sqlite') as db:
            db.executescript('''
                CREATE TABLE IF NOT EXISTS runways(id TEXT, pilot_root_id TEXT);
                CREATE TABLE IF NOT EXISTS runway_executions(id TEXT, runway_id TEXT);
                CREATE TABLE IF NOT EXISTS runway_sources(runway_id TEXT, url TEXT);
                CREATE TABLE IF NOT EXISTS runway_model_requests(request_id TEXT, execution_id TEXT,
                    request_digest TEXT, reported_tokens INTEGER, created_at REAL, status TEXT);
                CREATE TABLE IF NOT EXISTS runway_response_receipts(request_id TEXT, request_digest TEXT, response_json TEXT);
            ''')
            key = str(db.execute('SELECT COUNT(*) FROM runway_model_requests').fetchone()[0])
            db.execute('INSERT INTO runways VALUES (?,?)', (key, key))
            db.execute('INSERT INTO runway_executions VALUES (?,?)', (key, key))
            db.execute('INSERT INTO runway_model_requests VALUES (?,?,?,?,?,?)',
                (key, key, 'a'*64, inputs+outputs, stamp if stamp is not None else self.stamp/1000, status))
            receipt = {'terminal_type':'chat.completion.done', 'provider_response_id':response,
                'input_tokens':inputs, 'output_tokens':outputs, 'evidence_digest':'b'*64}
            db.execute('INSERT INTO runway_response_receipts VALUES (?,?,?)',
                (key, 'a'*64, 'broken' if corrupt else json.dumps(receipt)))
            if fixture:
                db.execute('INSERT INTO runway_sources VALUES (?,?)', (key, 'fixture://synthetic'))

    def test_worker_receipts_share_response_deduplication(self):
        self.add(event('shared',100,20))
        self.worker(response='shared', inputs=100, outputs=20)
        self.worker(response='unique')
        self.worker(response='unique')
        days = self.client.merge(self.client.from_openclaw(28,str(self.root)))
        self.assertEqual(self.client.FAILURES,[])
        self.assertEqual(sum(m['input']+m['output'] for d in days for m in d['models']),180)

    def test_worker_excludes_unknown_fixture_and_old_receipts(self):
        self.worker(status='unknown')
        self.worker(fixture=True)
        self.worker(stamp=1)
        self.worker(status='overrun', inputs=25000, outputs=100)
        days = self.client.merge(self.client.from_openclaw(28,str(self.root)))
        self.assertEqual(self.client.FAILURES,[])
        self.assertEqual(sum(m['input']+m['output'] for d in days for m in d['models']),25100)

    def test_worker_refuses_mismatched_receipt(self):
        self.worker()
        with sqlite3.connect(self.root/'hire/hire.sqlite') as db:
            db.execute('UPDATE runway_model_requests SET reported_tokens=999')
        self.client.from_openclaw(28,str(self.root))
        self.assertEqual(len(self.client.FAILURES),1)

    def test_worker_ledger_without_runs_is_an_idle_install(self):
        (self.root/'hire').mkdir()
        with sqlite3.connect(self.root/'hire/hire.sqlite') as db:
            db.execute('CREATE TABLE profile(id TEXT)')
        self.assertEqual(self.client.from_openclaw(28,str(self.root)),{})
        self.assertEqual(self.client.FAILURES,[])

    def test_worker_failure_prevents_posting_partial_totals(self):
        self.add(event('good',100,20))
        self.worker(corrupt=True)
        self.assert_no_partial_post()

    def assert_no_partial_post(self):
        with patch.dict(os.environ, {"OPENCLAW_STATE_DIR": str(self.root)}), \
                patch.object(self.client, "use_index"), \
                patch.object(self.client, "purge_unusable_token"), \
                patch.object(self.client, "auth_headers", return_value={}), \
                patch.object(self.client, "from_agentsview", return_value={}), \
                patch.object(self.client, "from_hermes", return_value={}), \
                patch.object(self.client, "_post") as post, \
                contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(SystemExit, "NOT reporting a partial total"):
                self.client.main(["--agent", "fictional-test-only"])
            post.assert_not_called()

    def test_official_client_never_posts_partial_totals(self):
        self.add(event('good',100,20))
        self.corrupt()
        self.assert_no_partial_post()


if __name__ == "__main__":
    unittest.main(verbosity=2)
