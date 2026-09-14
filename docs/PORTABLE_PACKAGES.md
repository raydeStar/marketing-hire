# Portable development packages

Each package contains the .NET runtime, the host and a freshly built PWA. Opening
the study requires no SDK, Node, Docker account or GPU. Your model connection and
a supported isolated worker are separate setup choices. These are unsigned
development archives, not consumer installers or qualified worker releases.

| Package | Launch | Private data by default | Isolated worker |
|---|---|---|---|
| Windows x64 | Open `Start Thaddeus.cmd` | `%LOCALAPPDATA%\Thaddeus2` | Explicitly enrolled Windows QEMU development preview only |
| macOS Apple silicon | Open `Start Thaddeus.command` | `~/Library/Application Support/Thaddeus2` | Unavailable; no fallback |
| macOS Intel | Open `Start Thaddeus.command` | `~/Library/Application Support/Thaddeus2` | Unavailable; no fallback |
| Linux x64 | Run `./start-thaddeus.sh` | `$XDG_DATA_HOME/Thaddeus2`, or `~/.local/share/Thaddeus2` | Explicitly enrolled Linux KVM development preview; see prerequisites below |
| Phone | Connect the browser/PWA to a supported host | Stored on the host | Uses that host's worker |

Keep the entire extracted folder together. macOS/Linux run in a terminal: keep
it open while using the study; Ctrl+C stops the host. Do not launch a second copy
against the same data. If a port is occupied, return to the existing study or
stop it first. No launcher stops an unrelated process or silently changes ports.
Windows retains its recorded-instance launcher and reuses an exact matching host.

The browser receives a one-minute, single-use owner login link after the host
starts. The permanent access key is not put in a URL. If browser opening fails,
visit `http://localhost:5179` and use `host-key.txt` in your private data folder.
No model call, local inference or VM boot is part of opening the application.

Use **Settings → Connect a model** to enter a provider URL, model and API key.
Choose the native system credential store or explicitly keep the key only until
the host stops. No environment-file editing is needed. See
[credential setup and native service requirements](MODEL_CONNECTIONS.md).

The Unix data folder is created with owner-only permissions (700). An existing
folder with wider permissions is refused without changing it. Keep private data
outside the extracted package. The development study in the source checkout is
not automatically moved, replaced or opened by these packages.

## Optional launch profile

Place `launch.json` in the extracted folder. Paths must be absolute and contain no
filesystem links. Use the appropriate path syntax for your operating system:

```json
{
  "schemaVersion": 1,
  "dataDirectory": "/home/example/.local/share/Thaddeus2",
  "localOrigin": "http://localhost:5179",
  "workerPort": 5183
}
```

Unix: `./Thaddeus.Host --desktop --launch-profile /absolute/launch.json`.
Windows: `./launch-host.ps1 -LaunchProfile C:\absolute\launch.json`.
For automated startup without opening a browser, add `--no-browser` to the Unix
command, or `-NoBrowser` to the PowerShell launcher. The portable entry point
ignores inherited Thaddeus configuration except an explicit API-key environment
override and its explicit endpoint binding. Phone exposure requires separate, deliberate HTTPS setup.

The optional `developmentWorkerInstallation` profile field accepts Windows and
Linux x64 installations and does not enable a worker by itself. macOS refuses
that setting. Linux needs the published host executable, read-only worker inputs,
KVM and a suitable systemd user session; see [Linux product preview](LINUX_PRODUCT_PREVIEW.md).
The current release does not install a model, a CLI bridge or a virtualization stack.

## Included worker preview

A prepared Windows x64 or Linux x64 package can include a `worker` folder beside
the host executable. Both desktop entry points discover its `installation.json`
without an installation path in `launch.json`. Keep the complete application
folder together when moving it. Open **Settings → Host research setup**, check the
installation, then explicitly enable it. Opening the app or discovering that
folder does not boot a VM or make a model call. An explicit operator installation
path still takes precedence.

The bundle records relative paths and exact hashes for the runtime, kernel,
initrd and base image. Paths outside the bundle, filesystem links and a descriptor
for another platform are refused. Existing runtime inventory, hash and host
requirement checks still control admission; finding a folder is not verification.
Moving a bundle changes its installation identity and requires a fresh check.
Linux retains its read-only input and systemd/KVM requirements. macOS worker
bundles are not implemented.

For an operator preparing a development bundle from an already pinned installation:

```powershell
dotnet run --project tools/Thaddeus.WorkerBundle --configuration Release -- C:\inputs\installation.json C:\packages\thaddeus-win-x64\worker win-x64
```

Use `linux-x64` and native absolute paths on Linux. The destination must be new.
Preparation copies files independently, verifies their hashes and writes the
descriptor last. An interrupted attempt leaves its incomplete directory for
inspection and never replaces the original inputs. The tool does not download,
execute or enroll a worker. Large images preserve zero-filled regions as sparse
files; allow enough disk space for their nonzero contents. Preparation checks
space for the complete logical copy plus a 10 GiB reserve before creating output,
and checks that reserve during copying. Other processes can still consume space concurrently. On Windows
it also respects NTFS compression already selected for the destination folder,
without changing an existing folder's compression or any source file.

### One archive containing the host and worker

The combined publisher reuses a checked host package and an existing pinned
installation. It streams them directly into a new ZIP, including the guest disk,
without staging another worker directory or rebuilding the application:

```text
node scripts/package-with-worker.mjs HOST_PACKAGE PINNED_INSTALLATION FRESH-NAME
```

Run this on Windows x64 or Linux x64 for that native platform. The host folder
must match its original manifest exactly; keep private `launch.json` files outside
it and supply them through the launcher's explicit profile option. Output goes to
`artifacts/portable-combined-FRESH-NAME/thaddeus-NATIVE-RID.zip`.

The combined manifest covers every host and worker file. It retains the host's
source provenance and records the original manifest hash, relative worker
descriptor and exact worker pins. Separate receipts record the packaging tool's
source hashes and assembly hash, so an older checked host is not presented as a
newly built application. SHA256SUMS and manifest/receipt sidecars accompany the ZIP.

The publisher budgets the full logical input size plus archive overhead and a
10 GiB free-space reserve, even when a sparse disk will compress. It checks space
during writes, hashes inputs while streaming, then reads and hashes every archived
entry before atomically publishing the final name. Failure or cancellation removes
only its owned incomplete ZIP; the wrapper removes its small builder output after
the process exits. Original inputs and already completed archives are preserved.

Verify a candidate with the existing native package check:

```text
node scripts/portable-check.mjs artifacts/portable-combined-FRESH-NAME artifacts/combined-native-FRESH-NAME
```

This performs one complete extraction, verifies the expanded inventory and checks
the real host's startup, login, credentials, backup/restore and worker discovery.
Extraction requires space for the full expanded files plus its fixture allowance
and 10 GiB reserve. Linux combined ZIP checks require `unzip`; host-only tar.gz
checks still use `tar`. The extracted package is removed after owned processes
and fictional credentials have been cleaned. Checking the archive does not enroll
or boot its worker; execution qualification remains a separate receipt.

The original `publish-portable.mjs` still produces host-only packages, including
macOS packages. Combined macOS workers are not implemented. Both publication paths
produce unsigned development archives. File hashes establish integrity, not
publisher identity or a qualified cross-platform security boundary. Complete the
[third-party notice requirements](THIRD_PARTY.md) before redistributing a build.

## Evidence and release boundary

The host includes [guided backup, shutdown and separate-study restore](STUDY_BACKUPS.md).
Settings can close an idle study, display a verified private backup, and reopen
the same study. The maintenance screen can restore a recorded backup into a new
study and prepare its own launcher using the current package. Finish shutdown
before opening that launcher. Both guided and offline restore refuse existing
targets. Different-version package selection, automatic switching/rollback and
automatic updates remain open.

`node scripts/publish-portable.mjs NATIVE-RID FRESH-NAME` captures sources in a
fresh ignored staging folder, restores the committed dependency locks, builds the
web client there and publishes a self-contained host for the machine's native
architecture. Runtime-specific lock additions stay in staging. The manifest
records source hashes, resolved lock hashes and every packaged file. ZIP/tar.gz
archives have a SHA-256 checksum. Checksums detect corruption; they do not prove
publisher identity. Do not disable operating-system security to open a download.

The disabled package CI matrix defines extracted native checks on Windows x64,
Ubuntu x64, macOS Intel and macOS Apple silicon. Its receipt records the actual
OS/architecture and checks page assets, local login, private data, duplicate and
occupied-port refusal, data-preserving restart and fail-closed worker admission.
It also saves and removes a fictional native credential through the product API,
including authenticated discovery after a host restart. The Linux job starts its
own private DBus/keyring session. Separate helper checks verify exact native bytes.
The extracted-host check additionally backs up a stopped study, restores it to a
new directory, starts the restored copy and compares history and access keys.
It also enters the maintenance screen with an open event stream, verifies that
product and worker endpoints close, reopens the same study and exits through the
owner controls. Guided restore additionally verifies exact review binding and
executes the generated platform launcher against the restored history. The Windows
check also makes another backup from that launch and verifies that package context
remains available for a subsequent restore. Local browser verification can use `node scripts/browser-check.mjs
PACKAGE NEW_ARTIFACT_DIRECTORY [SPEC...]`; it owns a disposable host and never
uses the running study's data or ports.
A passing host check is not evidence of a working VM on that platform. See the
exact local or historical CI run and its `verified.json` receipt before describing a package as tested.

Remaining distribution requirements include developer signing, Apple
notarization, a consumer installer/application bundle, credential prompts across
signed upgrades, upgrades with closed backups and rollback, broader Linux
distribution qualification, and a native macOS worker. Linux has a bounded KVM
development preview, not general distribution qualification. A
self-contained .NET package still needs its operating system's native runtime
dependencies. Physical phone setup remains deferred and user-operated.

References: [Microsoft macOS deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos)
and [GitHub native runner matrix](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).
