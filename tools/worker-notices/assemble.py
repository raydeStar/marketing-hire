"""Assemble a guest notice reference bundle from frozen local evidence.

No network, worker execution, or legal-completeness inference occurs here.
Original text bytes are preserved; the raven does not improve upstream prose.
"""
import hashlib
import html
import json
import os
import pathlib
import re
import shutil
import stat
import sys


MAX_JSON = 8_000_000
MAX_TEXT = 4_000_000
MAX_TOTAL = 64_000_000
MAX_COMPONENTS = 10_000
RESERVE = 10 * 1024 ** 3
SHA256 = re.compile(r"[a-f0-9]{64}")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def checked_path(value):
    path = pathlib.Path(os.path.abspath(value))
    # Inspect before resolve: resolving first would hide a junction or symlink.
    for part in (*reversed(path.parents), path):
        info = part.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
            raise ValueError("Linked/reparse path is not an evidence input or output")
    return path


def bounded_read(path, limit):
    path = checked_path(path)
    if not path.is_file():
        raise ValueError("Evidence must be a regular file")
    with path.open("rb") as stream:
        data = stream.read(limit + 1)
    if len(data) > limit:
        raise ValueError("Evidence exceeds its byte bound")
    return data


def load_json(data):
    def unique_pairs(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate JSON field")
            result[key] = value
        return result
    return json.loads(data.decode("utf-8"), object_pairs_hook=unique_pairs)


def sequence(value, limit=MAX_COMPONENTS):
    if not isinstance(value, list) or len(value) > limit:
        raise ValueError("Invalid or oversized evidence list")
    return value


def identity(component):
    name, version = component["name"], component["version"]
    if not isinstance(name, str) or not name or not isinstance(version, str) or not version:
        raise ValueError("Missing component identity")
    return name + "@" + version


class Texts:
    def __init__(self):
        self.reads = {}
        self.outputs = {}
        self.total = 0

    def read(self, root, ref, copy=False):
        sha = ref["sha256"]
        if not isinstance(sha, str) or not SHA256.fullmatch(sha):
            raise ValueError("Invalid text hash")
        if ref["file"] != "texts/" + sha + ".txt":
            raise ValueError("Text path must be its fixed content address")
        size = ref["bytes"]
        if type(size) is not int or not 0 <= size <= MAX_TEXT:
            raise ValueError("Invalid text byte count")
        key = (root, ref["file"])
        if key not in self.reads:
            data = bounded_read(root / ref["file"], MAX_TEXT)
            self.total += len(data)
            if self.total > MAX_TOTAL:
                raise ValueError("Combined evidence exceeds its byte bound")
            self.reads[key] = data
        data = self.reads[key]
        if len(data) != size or digest(data) != sha:
            raise ValueError("Text bytes differ from frozen reference")
        if copy:
            self.outputs[ref["file"]] = data
        return data


def escape(value):
    text = html.escape(str(value), quote=True).replace("\n", " ").replace("\r", " ")
    for char in "\\`*_{}[]|":
        text = text.replace(char, "\\" + char)
    return text


def render(bundle):
    summary = bundle["summary"]
    lines = ["# Worker guest notice reference bundle", "",
             "This is a review artifact, not a completed redistribution/source bundle.", "",
             "Original installed notices and separately sourced supplements retain their exact bytes.",
             "Package metadata hashes identify the evidence; metadata files are not copied here.",
             "All local notice links are included. Provenance is recorded in bundle.json.", "",
             "- Guest disk SHA-256: " + bundle["guestDiskSha256"],
             "- Inventory SHA-256: " + bundle["inventorySha256"],
             "- Supplement catalog SHA-256: " + bundle["catalogSha256"],
             f"- Components: {summary['components']}; with upstream supplements: {summary['supplementedComponents']}.",
             f"- Original findings retained: {summary['originalFindings']}; without a matching supplement: {summary['findingsWithoutSupplement']}.",
             "- These counts describe located text, not completed license obligations.", "",
             "## Coverage limits", ""]
    lines.extend("- " + escape(item) for item in bundle["limitations"])
    lines.extend(["", "## Common license texts", ""])
    for ref in bundle["commonLicenses"]:
        lines.append(f"- [{escape(ref['path'])}]({ref['file']})")
    for ecosystem in ("dpkg", "npm"):
        lines.extend(["", "## " + ecosystem + " components", "",
                      "| Package | Installed location or architecture | Located notice texts |",
                      "|---|---|---|"])
        for c in bundle["components"]:
            if c["ecosystem"] != ecosystem:
                continue
            links = [f"[installed {i + 1}]({n['file']})" for i, n in enumerate(c["notices"])]
            if c.get("supplement"):
                links.extend(f"[upstream {i + 1}]({n['file']})"
                             for i, n in enumerate(c["supplement"]["notices"]))
            location = c.get("path", c.get("architecture", ""))
            lines.append(f"| {escape(identity(c))} | {escape(location)} | {'; '.join(links) or 'Not located'} |")
    if bundle.get("embeddedNotices"):
        lines.extend(["", "## Embedded-library source notices", "",
                      "These source-linked library notices do not supply missing wrapper notices or prove binary/source equivalence.", ""])
        for entry in bundle["embeddedNotices"]:
            lines.extend(["### " + escape(entry["dependency"]) + " in " + escape(entry["identity"]), "",
                          "Installed wrapper: " + escape(entry["path"]), "",
                          escape(entry["basis"]), ""])
            for label in ("notices", "evidence"):
                lines.extend(f"- [{label}: {escape(ref['path'])}]({ref['file']})" for ref in entry[label])
            lines.extend(["", "Remaining review: " + escape(entry["additionalReview"]), ""])
    lines.extend(["", "## Original inventory findings", "",
                  "Every original finding is retained, including those with a supplied upstream text.", ""])
    for finding in bundle["inventoryFindings"]:
        status = "Upstream text supplied; additional review remains" if finding["supplementProvided"] else "No supplement supplied"
        lines.append(f"- {escape(finding['component'])}: {escape(finding['reason'])}. {status}.")
        if finding.get("path"):
            lines.append("  Location: " + escape(finding["path"]))
    lines.append("")
    return "\n".join(lines).encode("utf-8")


def prepare(inventory_path, catalog_path):
    inventory_path, catalog_path = checked_path(inventory_path), checked_path(catalog_path)
    inventory_bytes = bounded_read(inventory_path, MAX_JSON)
    catalog_bytes = bounded_read(catalog_path, MAX_JSON)
    inventory, catalog = load_json(inventory_bytes), load_json(catalog_bytes)
    if (inventory["schemaVersion"] != 1 or inventory["inspectionPassed"] is not True
            or inventory["redistributionComplete"] is not False
            or catalog["formatVersion"] not in (1, 2, 3) or catalog["redistributionComplete"] is not False):
        raise ValueError("Use an inspected, explicitly incomplete inventory and catalog")
    if "dpkgBindings" in catalog and catalog["formatVersion"] < 2:
        raise ValueError("dpkg supplements require catalog format 2")
    if "embeddedBindings" in catalog and catalog["formatVersion"] < 3:
        raise ValueError("Embedded-library notices require catalog format 3")
    if (catalog["inventorySha256"] != digest(inventory_bytes)
            or catalog["guestDiskSha256"] != inventory["diskSha256"]
            or not SHA256.fullmatch(inventory["diskSha256"])):
        raise ValueError("Inventory or guest disk pin differs from supplement catalog")
    texts = Texts()
    components, npm, dpkg = [], {}, {}
    # Validate the complete frozen evidence, including metadata not copied into the bundle.
    for field in ("status", "osRelease", "openclawLock"):
        texts.read(inventory_path.parent, inventory[field])
    common = sequence(inventory["commonLicenses"], 100)
    for ref in common:
        texts.read(inventory_path.parent, ref, copy=True)
    seen = set()
    for component in sequence(inventory["components"]):
        component_identity = identity(component)
        ecosystem = component["ecosystem"]
        if ecosystem not in ("dpkg", "npm"):
            raise ValueError("Unknown inventory ecosystem")
        c = {key: component[key] for key in
             ("ecosystem", "name", "version", "architecture", "sourceName", "sourceVersion", "path", "declaredLicense")
             if key in component}
        key = (ecosystem, component_identity, c.get("path", c.get("architecture")))
        if key in seen:
            raise ValueError("Duplicate installed component")
        seen.add(key)
        c["notices"] = sequence(component["notices"], 100)
        for ref in c["notices"]:
            texts.read(inventory_path.parent, ref, copy=True)
        if ecosystem == "npm":
            metadata = load_json(texts.read(inventory_path.parent, component["metadata"]))
            declared = metadata.get("license", metadata.get("licenses"))
            if identity(metadata) != component_identity or declared != c.get("declaredLicense"):
                raise ValueError("Installed package metadata identity/license differs: " + component_identity)
            c["metadataSha256"] = component["metadata"]["sha256"]
            npm[(component_identity, c["path"])] = c
        else:
            dpkg[(component_identity, c["architecture"])] = c
        components.append(c)
    if (len(texts.reads) != inventory["textFiles"] or texts.total != inventory["capturedBytes"]):
        raise ValueError("Captured evidence totals differ from inventory")
    bound = set()
    for binding in sequence(catalog["bindings"]):
        key = (binding["identity"], binding["path"])
        if key in bound or key not in npm:
            raise ValueError("Duplicate or unknown supplement binding")
        c = npm[key]
        if (binding["metadataSha256"] != c["metadataSha256"]
                or binding.get("declaredLicense") != c.get("declaredLicense")):
            raise ValueError("Supplement does not match installed metadata/license")
        refs = sequence(binding["notices"], 100)
        if not refs or not binding["basis"] or not binding["additionalReview"] or not binding["source"]:
            raise ValueError("Supplement requires text, provenance, and review limits")
        for ref in refs:
            texts.read(catalog_path.parent, ref, copy=True)
        c["supplement"] = {key: binding[key] for key in ("basis", "source", "notices", "additionalReview")}
        bound.add(key)
    dpkg_bound = set()
    for binding in sequence(catalog.get("dpkgBindings", [])):
        key = (binding["identity"], binding["architecture"])
        if key in dpkg_bound or key not in dpkg:
            raise ValueError("Duplicate or unknown dpkg supplement binding")
        c = dpkg[key]
        if (binding["statusSha256"] != inventory["status"]["sha256"]
                or any(binding[field] != c[field] for field in ("sourceName", "sourceVersion"))):
            raise ValueError("dpkg supplement differs from installed status/source identity")
        refs = sequence(binding["notices"], 100)
        if not refs or not binding["basis"] or not binding["additionalReview"] or not binding["source"]:
            raise ValueError("dpkg supplement requires text, provenance, and review limits")
        for ref in refs:
            texts.read(catalog_path.parent, ref, copy=True)
        c["supplement"] = {field: binding[field] for field in ("basis", "source", "notices", "additionalReview")}
        dpkg_bound.add(key)
    embedded, embedded_seen = [], set()
    for binding in sequence(catalog.get("embeddedBindings", [])):
        owner = (binding["identity"], binding["path"])
        dependency = binding["dependency"]
        if not isinstance(dependency, str) or not dependency.strip() or owner not in npm:
            raise ValueError("Embedded-library notice requires a known wrapper and named dependency")
        key = (*owner, dependency)
        if key in embedded_seen or binding["metadataSha256"] != npm[owner]["metadataSha256"]:
            raise ValueError("Duplicate embedded-library binding or changed wrapper metadata")
        if not binding["basis"] or not binding["source"] or not binding["additionalReview"]:
            raise ValueError("Embedded-library notice requires provenance and review limits")
        for field in ("notices", "evidence"):
            refs = sequence(binding[field], 100)
            if not refs:
                raise ValueError("Embedded-library notice requires original text and source evidence")
            for ref in refs:
                texts.read(catalog_path.parent, ref, copy=True)
        embedded.append({field: binding[field] for field in
                         ("identity", "path", "metadataSha256", "dependency", "basis", "source",
                          "notices", "evidence", "additionalReview")})
        embedded_seen.add(key)
    # A library notice is not its wrapper's missing notice. Keep that finding open.
    findings = []
    for original in sequence(inventory["gaps"]):
        finding = dict(original)
        finding["supplementProvided"] = (original["component"], original.get("path")) in bound
        if "path" not in original:
            installed = {key for key in dpkg if key[0] == original["component"]}
            # The original dpkg finding has no architecture: supplying one architecture must not hide another.
            if installed:
                finding["supplementProvided"] = installed.issubset(dpkg_bound)
        findings.append(finding)
    bundle = {
        "formatVersion": 2 if embedded else 1, "assemblyPassed": True, "redistributionComplete": False,
        "scope": inventory["scope"], "guestDiskSha256": inventory["diskSha256"],
        "inventorySha256": digest(inventory_bytes), "catalogSha256": digest(catalog_bytes),
        "limitations": sequence(inventory["limitations"], 100) + [
            "Upstream supplements do not prove complete transitive/native notice coverage or binary/source equivalence.",
            "The assembler verifies frozen local hashes and bindings; it does not authenticate upstream attestations.",
            "This guest-only reference bundle does not include QEMU/runtime, firmware, kernel or initrd notices."],
        "summary": {"components": len(components), "supplementedComponents": len(bound) + len(dpkg_bound),
                    "originalFindings": len(findings), "embeddedNoticeRecords": len(embedded),
                    "findingsWithoutSupplement": sum(not f["supplementProvided"] for f in findings),
                    "textFiles": len(texts.outputs), "textBytes": sum(map(len, texts.outputs.values()))},
        "components": components, "commonLicenses": common, "inventoryFindings": findings,
        "embeddedNotices": embedded,
    }
    files = dict(texts.outputs)
    files["bundle.json"] = (json.dumps(bundle, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    files["THIRD-PARTY-NOTICES.md"] = render(bundle)
    if sum(map(len, files.values())) > MAX_TOTAL:
        raise ValueError("Notice bundle exceeds its output byte bound")
    return bundle, files


def write_bundle(output, files):
    output = pathlib.Path(os.path.abspath(output))
    checked_path(output.parent)
    if os.path.lexists(output):
        raise ValueError("Output must be a fresh directory; existing evidence is preserved")
    required = sum(map(len, files.values())) + 16 * 1024 ** 2 + RESERVE
    if shutil.disk_usage(output.parent).free < required:
        raise ValueError("Notice bundle would consume the 10 GiB free-space reserve")
    if any(name not in ("bundle.json", "THIRD-PARTY-NOTICES.md")
           and not re.fullmatch(r"texts/[a-f0-9]{64}\.txt", name) for name in files):
        raise ValueError("Invalid generated bundle path")
    output.mkdir()
    owner = output.stat()
    created, text_dir_created = [], False
    try:
        (output / "texts").mkdir()
        text_dir_created = True
        for name, data in sorted(files.items()):
            target = output / name
            with target.open("xb") as stream:
                created.append(target)
                stream.write(data)
    except BaseException:
        # Delete only files created by this invocation, never recursively prune a parent.
        checked_path(output)
        if not os.path.samestat(owner, output.stat()):
            raise RuntimeError("Output directory changed; retained for manual cleanup")
        for target in reversed(created):
            checked_path(target)
            target.unlink()
        if text_dir_created:
            checked_path(output / "texts").rmdir()
        output.rmdir()
        raise


def assemble(inventory_path, catalog_path, output):
    bundle, files = prepare(inventory_path, catalog_path)
    write_bundle(output, files)
    return bundle


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit("Usage: assemble.py INVENTORY_JSON CATALOG_JSON FRESH_OUTPUT_DIRECTORY")
    result = assemble(*sys.argv[1:])
    print(json.dumps({"assemblyPassed": True, "redistributionComplete": False, **result["summary"]}))
