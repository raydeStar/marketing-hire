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
| Linux x64 | Run `./start-thaddeus.sh` | `$XDG_DATA_HOME/Thaddeus2`, or `~/.local/share/Thaddeus2` | Unavailable; no fallback |
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

The optional `developmentWorkerInstallation` profile field is Windows-only and
does not enable a worker by itself. macOS and Linux refuse that setting. The
current release does not install a model, a CLI bridge or a virtualization stack.

## Evidence and release boundary

The host includes [guided backup/shutdown and offline restore](STUDY_BACKUPS.md).
Settings can close an idle study, display a verified private backup, and reopen
the same study. Offline restore refuses an existing target. Guided package
selection, restore/rollback and automatic updates remain open.

`node scripts/publish-portable.mjs NATIVE-RID FRESH-NAME` captures sources in a
fresh ignored staging folder, restores the committed dependency locks, builds the
web client there and publishes a self-contained host for the machine's native
architecture. Runtime-specific lock additions stay in staging. The manifest
records source hashes, resolved lock hashes and every packaged file. ZIP/tar.gz
archives have a SHA-256 checksum. Checksums detect corruption; they do not prove
publisher identity. Do not disable operating-system security to open a download.

The package CI matrix executes the extracted native package on Windows x64,
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
owner controls. Local browser verification can use `node scripts/browser-check.mjs
PACKAGE NEW_ARTIFACT_DIRECTORY [SPEC...]`; it owns a disposable host and never
uses the running study's data or ports.
A passing host check is not evidence of a working VM on that platform. See the
exact CI run and its `verified.json` receipt before describing a package as tested.

Remaining distribution requirements include developer signing, Apple
notarization, a consumer installer/application bundle, credential prompts across
signed upgrades, upgrades with closed backups and rollback, broader Linux
distribution qualification, and actual macOS/Linux isolated workers. A
self-contained .NET package still needs its operating system's native runtime
dependencies. Physical phone setup remains deferred and user-operated.

References: [Microsoft macOS deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos)
and [GitHub native runner matrix](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).
