"""Inspect a pinned ext4 worker without mounting it or executing its programs.

This collects installed metadata and candidate notice text. It deliberately does
not certify license completeness, binary/source equivalence, or redistribution.
"""
import hashlib
import json
import pathlib
import re
import subprocess
import sys
from dataclasses import dataclass


MAX_FILE = 4_000_000
MAX_TOTAL = 48_000_000


def notice_filename(name):
    # Upstream notices sometimes put their license identifier first. A filename
    # locates candidate bytes; it never assigns terms to the package.
    return bool(re.match(
        r"^(?:(?:licen[cs]e|copying|copyright|notice|third[-_]party[-_]notices?)(?:[._-]|$)"
        r"|[a-z0-9][a-z0-9._+-]*[-_ .]licen[cs]e(?:\.(?:txt|md|rst))?$)", name, re.I))


def digest(data):
    return hashlib.sha256(data).hexdigest()


def file_digest(path):
    with open(path, "rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


@dataclass(frozen=True)
class Entry:
    inode: int
    mode: int
    name: str
    size: int

    @property
    def kind(self):
        return self.mode & 0o170000


def parse_entries(data):
    entries = {}
    for line in data.decode("utf-8", errors="strict").splitlines():
        if not line:
            continue
        fields = line.split("/")
        if len(fields) != 8 or fields[0] or fields[-1]:
            raise ValueError("Unrecognized debugfs directory record")
        inode, mode, uid, gid, name, size = fields[1:7]
        if not all(re.fullmatch(r"[0-9]+", n) for n in (inode, mode, uid, gid)):
            raise ValueError("Invalid directory identity")
        if name in (".", ".."):
            continue
        if not name or any(ord(char) < 32 for char in name) or name in entries:
            raise ValueError("Ambiguous directory entry")
        entry = Entry(int(inode), int(mode, 8), name, int(size or "0"))
        # Unused/deleted directory records are not installed files.
        if entry.inode:
            if entry.kind not in (0o040000, 0o100000, 0o120000):
                raise ValueError("Unsupported entry in metadata directory")
            entries[name] = entry
    if len(entries) > 20_000:
        raise ValueError("Directory inventory exceeds bound")
    return entries


class Ext4:
    def __init__(self, image):
        self.image = image
        self.directories = {}

    def command(self, command, limit=MAX_FILE):
        # All debugfs operands after the fixed verb are numeric inodes. No guest
        # path is a shell command, extraction destination, or host filesystem path.
        result = subprocess.run(["/usr/sbin/debugfs", "-R", command, self.image],
                                capture_output=True, timeout=10, check=True)
        errors = result.stderr.decode("utf-8", errors="replace").splitlines()
        if any(line and not line.startswith("debugfs ") for line in errors):
            raise ValueError("debugfs refused metadata read: " + "; ".join(errors)[-500:])
        if len(result.stdout) > limit:
            raise ValueError("Metadata exceeds its byte bound")
        return result.stdout

    def listing(self, inode):
        if inode not in self.directories:
            self.directories[inode] = parse_entries(self.command(f"ls -p <{inode}>"))
            if len(self.directories) > 15_000:
                raise ValueError("Metadata directory count exceeds bound")
        return self.directories[inode]

    def link(self, entry):
        info = self.command(f"stat <{entry.inode}>", 20_000).decode("utf-8", errors="strict")
        fast = re.search(r'^Fast link dest: "(.*)"$', info, re.MULTILINE)
        data = fast[1].encode("utf-8") if fast else self.command(f"cat <{entry.inode}>", 4096)
        if len(data) != entry.size:
            raise ValueError("Guest link size differs")
        return data.decode("utf-8", errors="strict")

    def resolve(self, path):
        if not path.startswith("/") or "\x00" in path:
            raise ValueError("Use absolute guest paths")
        pending = path.split("/")[1:]
        current = Entry(2, 0o040755, "/", 0)
        visited, links = [], 0
        while pending:
            part = pending.pop(0)
            if not part or part == ".":
                continue
            if part == "..":
                raise ValueError("Guest path escapes or contains unresolved parent traversal")
            if current.kind != 0o040000:
                raise FileNotFoundError(path)
            try:
                entry = self.listing(current.inode)[part]
            except KeyError:
                raise FileNotFoundError(path) from None
            if entry.kind == 0o120000:
                links += 1
                if links > 32 or entry.size > 4096:
                    raise ValueError("Guest link chain exceeds bound")
                target = self.link(entry)
                if not target or "\x00" in target or any(ord(c) < 32 for c in target):
                    raise ValueError("Invalid guest link")
                target_parts = ([] if target.startswith("/") else visited.copy())
                for component in target.split("/"):
                    if component in ("", "."):
                        continue
                    if component == "..":
                        if not target_parts:
                            raise ValueError("Guest link escapes image root")
                        target_parts.pop()
                    else:
                        target_parts.append(component)
                pending = target_parts + pending
                current, visited = Entry(2, 0o040755, "/", 0), []
            else:
                current = entry
                visited.append(part)
        return current

    def read(self, path):
        entry = self.resolve(path)
        if entry.kind != 0o100000 or entry.size > MAX_FILE:
            raise ValueError("Metadata is not a bounded regular file: " + path)
        data = self.command(f"cat <{entry.inode}>")
        if len(data) != entry.size:
            raise ValueError("Metadata size changed: " + path)
        return data

    def children(self, path):
        entry = self.resolve(path)
        if entry.kind != 0o040000:
            raise ValueError("Expected metadata directory: " + path)
        return self.listing(entry.inode)


def control_records(data):
    records, current, previous = [], {}, None
    for line in data.decode("utf-8", errors="strict").splitlines() + [""]:
        if not line:
            if current:
                records.append(current)
            current, previous = {}, None
        elif line[0].isspace():
            if previous is None:
                raise ValueError("Orphan package metadata continuation")
            current[previous] += "\n" + line[1:]
        else:
            key, sep, value = line.partition(":")
            if not sep or not re.fullmatch(r"[A-Za-z][A-Za-z0-9-]*", key) or key in current:
                raise ValueError("Ambiguous package control record")
            current[key], previous = value.strip(), key
    return records


class Collector:
    def __init__(self, fs, output):
        self.fs, self.output = fs, output
        self.contents, self.total = {}, 0
        self.components, self.gaps = [], []
        self.package_inodes, self.modules_inodes = set(), set()

    def keep(self, path):
        data = self.fs.read(path)
        sha = digest(data)
        if sha not in self.contents:
            self.total += len(data)
            if self.total > MAX_TOTAL:
                raise ValueError("Captured metadata exceeds total bound")
            self.contents[sha] = data
        return {"path": path, "sha256": sha, "bytes": len(data), "file": "texts/" + sha + ".txt"}

    def candidate_notices(self, path):
        result = []
        for name in self.fs.children(path):
            if notice_filename(name):
                target = path + "/" + name
                entry = self.fs.resolve(target)
                if entry.kind == 0o100000:
                    result.append(self.keep(target))
        return result

    def debian(self):
        status = self.keep("/var/lib/dpkg/status")
        records = control_records(self.contents[status["sha256"]])
        for item in records:
            if item.get("Status") != "install ok installed":
                continue
            name, version = item["Package"], item["Version"]
            if not re.fullmatch(r"[a-z0-9][a-z0-9+.-]+", name):
                raise ValueError("Invalid installed package name")
            source = item.get("Source", name)
            match = re.fullmatch(r"([a-z0-9][a-z0-9+.-]+)(?: \(([^()\s]+)\))?", source)
            if not match:
                raise ValueError("Invalid source package metadata")
            component = {"ecosystem": "dpkg", "name": name, "version": version,
                         "architecture": item.get("Architecture"), "sourceName": match[1],
                         "sourceVersion": match[2] or version, "notices": []}
            try:
                component["notices"].append(self.keep("/usr/share/doc/" + name + "/copyright"))
            except FileNotFoundError:
                self.gaps.append({"component": name + "@" + version, "reason": "installed copyright file missing"})
            self.components.append(component)
        common = [self.keep("/usr/share/common-licenses/" + name)
                  for name in self.fs.children("/usr/share/common-licenses")]
        return status, common

    def npm_package(self, path):
        # Scoped packages reach this method without the unscoped branch's root
        # check. A broken workspace link is not an unidentified package folder.
        try:
            root = self.fs.resolve(path)
        except FileNotFoundError:
            self.gaps.append({"component": path, "reason": "dangling installed module link"})
            return
        if root.kind != 0o040000:
            return
        try:
            entry = self.fs.resolve(path + "/package.json")
        except FileNotFoundError:
            self.gaps.append({"component": path, "reason": "package.json missing from installed module directory"})
            return
        if entry.inode in self.package_inodes:
            return
        self.package_inodes.add(entry.inode)
        if len(self.package_inodes) > 5000:
            raise ValueError("Installed npm component count exceeds bound")
        metadata = self.keep(path + "/package.json")
        package = json.loads(self.contents[metadata["sha256"]])
        name, version = package.get("name"), package.get("version")
        if not isinstance(name, str) or not isinstance(version, str) or len(name) > 300 or len(version) > 100:
            raise ValueError("Installed npm package identity missing")
        notices = self.candidate_notices(path)
        self.components.append({"ecosystem": "npm", "name": name, "version": version, "path": path,
                                "declaredLicense": package.get("license", package.get("licenses")),
                                "metadata": metadata, "notices": notices})
        if not notices:
            self.gaps.append({"component": name + "@" + version, "path": path,
                              "reason": "no root license or notice text found; declaration alone is insufficient"})
        self.npm_modules(path + "/node_modules")

    def npm_modules(self, path):
        try:
            root = self.fs.resolve(path)
        except FileNotFoundError:
            return
        if root.inode in self.modules_inodes:
            return
        self.modules_inodes.add(root.inode)
        if len(self.modules_inodes) > 6000:
            raise ValueError("Installed node_modules count exceeds bound")
        for name in self.fs.children(path):
            target = path + "/" + name
            try:
                installed_entry = self.fs.resolve(target)
            except FileNotFoundError:
                self.gaps.append({"component": target, "reason": "dangling installed module link"})
                continue
            if installed_entry.kind != 0o040000:
                continue
            if name == ".pnpm":
                for installed in self.fs.children(target):
                    nested = target + "/" + installed
                    if self.fs.resolve(nested).kind == 0o040000:
                        self.npm_modules(nested if installed == "node_modules" else nested + "/node_modules")
            elif name.startswith("."):
                continue
            elif name.startswith("@"):
                for scoped in self.fs.children(target):
                    self.npm_package(target + "/" + scoped)
            elif self.fs.resolve(target).kind == 0o040000:
                self.npm_package(target)

    def collect(self):
        status, common = self.debian()
        print(json.dumps({"phase": "dpkg inspected", "components": len(self.components)}), flush=True)
        self.npm_package("/app")
        # OpenClaw bundles integrations alongside its main npm graph.
        for name in self.fs.children("/app/extensions"):
            target = "/app/extensions/" + name
            if self.fs.resolve(target).kind == 0o040000:
                try:
                    self.fs.resolve(target + "/package.json")
                except FileNotFoundError:
                    continue
                self.npm_package(target)
        self.npm_modules("/usr/local/lib/node_modules")
        return {"status": status, "commonLicenses": common, "osRelease": self.keep("/etc/os-release"),
                "openclawLock": self.keep("/app/pnpm-lock.yaml"),
                "components": self.components, "gaps": self.gaps}

    def write(self, report):
        (self.output / "texts").mkdir()
        for sha, data in self.contents.items():
            (self.output / "texts" / (sha + ".txt")).write_bytes(data)
        (self.output / "inventory.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


def main():
    image, expected, destination = sys.argv[1:]
    if not re.fullmatch(r"[a-f0-9]{64}", expected):
        raise ValueError("Use an explicit SHA-256 pin")
    output = pathlib.Path(destination)
    if not output.is_dir() or any(output.iterdir()):
        raise ValueError("Use an existing empty output directory")
    if file_digest(image) != expected:
        raise ValueError("Worker disk differs from trusted pin")
    print(json.dumps({"phase": "initial disk pin verified"}), flush=True)
    collector = Collector(Ext4(image), output)
    data = collector.collect()
    print(json.dumps({"phase": "metadata collected; rechecking disk pin", "components": len(collector.components)}), flush=True)
    if file_digest(image) != expected:
        raise ValueError("Worker disk changed during inspection")
    report = {"schemaVersion": 1, "inspectionPassed": True, "redistributionComplete": False,
              "diskSha256": expected, "guestExecuted": False, "diskMounted": False,
              "scope": "dpkg installed metadata, common licenses, OpenClaw installed npm graph and extensions; candidate notice text only",
              "limitations": ["Notice filenames do not prove complete license text or attribution.",
                              "No source archives or build-source equivalence have been verified.",
                              "Standalone binaries, embedded/bundled code, Python, Go, Java and other package ecosystems require separate coverage.",
                              "QEMU, firmware, kernel and initrd are outside this guest-root inventory."],
              "textFiles": len(collector.contents), "capturedBytes": collector.total, **data}
    collector.write(report)
    print(json.dumps({"inspectionPassed": True, "redistributionComplete": False,
                      "components": len(collector.components), "gaps": len(collector.gaps),
                      "capturedBytes": collector.total, "message": "The raven counted the contents; a count is not a release certificate."}))


if __name__ == "__main__":
    main()
