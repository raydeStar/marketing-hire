"""Decode OpenClaw transcript storage for the pinned official Index collector.

Only storage decoding lives here. The official client retains its response-ID
deduplication, calendar buckets, counters, authentication and reporting policy.
"""

import ctypes
import ctypes.util
import functools
import json
import sqlite3


MAX_EVENT_BYTES = 64 * 1024 * 1024


@functools.lru_cache(maxsize=1)
def _zstd():
    try:
        library = ctypes.util.find_library("zstd")
        if not library:
            raise OSError("libzstd is unavailable")
        lib = ctypes.CDLL(library)
        lib.ZSTD_decompress.argtypes = [ctypes.c_void_p, ctypes.c_size_t,
                                       ctypes.c_void_p, ctypes.c_size_t]
        lib.ZSTD_decompress.restype = ctypes.c_size_t
        lib.ZSTD_isError.argtypes = [ctypes.c_size_t]
        lib.ZSTD_isError.restype = ctypes.c_uint
        return lib
    except (OSError, AttributeError) as error:
        raise sqlite3.DatabaseError("OpenClaw compressed transcript decoder unavailable") from error


def _decode(blob, expected_bytes):
    if (not isinstance(blob, bytes) or not blob or len(blob) > MAX_EVENT_BYTES
            or type(expected_bytes) is not int or not 0 < expected_bytes <= MAX_EVENT_BYTES):
        raise sqlite3.DatabaseError("Invalid OpenClaw compressed transcript size")
    lib = _zstd()
    output = ctypes.create_string_buffer(expected_bytes)
    size = lib.ZSTD_decompress(output, expected_bytes, blob, len(blob))
    if lib.ZSTD_isError(size) or size != expected_bytes:
        raise sqlite3.DatabaseError("OpenClaw compressed transcript could not be decoded completely")
    try:
        raw = output.raw.decode("utf-8")
        if not isinstance(json.loads(raw), dict):
            raise ValueError("event must be an object")
        return raw
    except (UnicodeError, ValueError) as error:
        # A missing receipt is not an idle employee. Refuse a partial day's total.
        raise sqlite3.DatabaseError("Invalid OpenClaw compressed transcript JSON") from error


def read_transcript_rows(db, since):
    """Return the official collector's (JSON, created_at) rows, or fail closed."""
    columns = {row[1] for row in db.execute("PRAGMA table_info(transcript_events)")}
    if "event_zstd" not in columns:
        return db.execute(
            "SELECT event_json, created_at FROM transcript_events WHERE created_at >= ?",
            (since,)).fetchall()
    if "event_utf8_bytes" not in columns:
        raise sqlite3.DatabaseError("OpenClaw compressed transcript size column is missing")
    rows = []
    for raw, stamp, blob, size in db.execute(
            "SELECT event_json, created_at, event_zstd, event_utf8_bytes "
            "FROM transcript_events WHERE created_at >= ?", (since,)):
        if raw is None:
            raw = _decode(blob, size)
        rows.append((raw, stamp))
    return rows
