# Portable development packages

Each package contains the .NET runtime, the host and a freshly built PWA. Opening
the study requires no SDK, Node, Docker account or GPU. Your model connection and
a supported isolated worker are separate setup choices. These are unsigned
development archives, not consumer installers or qualified worker releases.

For Windows desktop use, open the launcher from File Explorer or a normal Windows
terminal. An MSIX development app's child processes can inherit private registry
and AppData redirection: notification registration can then appear successful
inside that environment while remaining invisible to the Windows desktop. The
host must remain running and awake for schedules; notification clicks return to
its saved results and use the browser's ordinary sign-in.
The native `--desktop` host also has a Windows tray icon: double-click it or use
**Open Thaddeus** to open a fresh one-use browser login link. **Exit Thaddeus**
stops this host gracefully, including its owned browser task and worker services.
It does not close an ordinary browser window or stop a separately launched model
provider or Luna development bridge. Closing the browser alone leaves the host
and scheduled work running.

The [Mac MVP scope](MAC_MVP_SCOPE.md) describes the remaining Apple silicon/Intel
worker and consumer app work. Mac packaging targets below are supported by the
publisher source; they are not a claim of native Mac acceptance or a shipped
standalone Mac research worker.

Windows also has a [per-user installer preview](WINDOWS_INSTALLER.md) built from
an existing verified host package. It adds a native Start menu entry and
data-preserving uninstall. It remains unsigned and host-only; it does not replace
the running manual QA package or complete consumer release qualification.

Every new native host publication includes a generated third-party notice index
at `ThirdPartyNotices/Generated/THIRD-PARTY-NOTICES.txt`, with preserved full texts
and a machine-readable dependency inventory. Publication refuses missing or
mismatched notices. See `THIRD_PARTY.md` in the extracted package for its scope;
worker-image and QEMU redistribution requirements remain separate.

| Package | Launch | Private data by default | Isolated worker |
|---|---|---|---|
| Windows x64 | Open `Start Thaddeus.cmd` | `%LOCALAPPDATA%\Thaddeus2` | Explicitly enrolled Windows QEMU development preview only |
| macOS Apple silicon | Open `Start Thaddeus.command` | `~/Library/Application Support/Thaddeus2` | Unavailable; no fallback |
| macOS Intel | Open `Start Thaddeus.command` | `~/Library/Application Support/Thaddeus2` | Unavailable; no fallback |
| Linux x64 | Run `./start-thaddeus.sh` | `$XDG_DATA_HOME/Thaddeus2`, or `~/.local/share/Thaddeus2` | Explicitly enrolled Linux KVM development preview; see prerequisites below |
| Phone | Connect the browser/PWA to a supported host | Stored on the host | Uses that host's worker |

Keep the entire extracted folder together. macOS/Linux run in a terminal: keep
it open while using the study; Ctrl+C stops the host. The native desktop entry
can reopen its exact running package/study through a same-account local channel,
without creating another host. Different packages/profiles and unrelated occupied
ports remain refused. No launcher stops another process or silently changes ports.
Windows also retains its separate recorded-instance PowerShell launcher.
See [desktop reopening](DESKTOP_REOPEN.md) for the native verification boundary.

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
study and prepare its own launcher using the current package. **Open restored
study** closes maintenance and starts the verified app directly; saved launchers
remain available for later use. Both guided and offline restore refuse existing
targets. Guided selection of another compatible package prepares a separate copy
and a launcher that verifies the reviewed package before starting it; see the
[upgrade and rollback steps](STUDY_BACKUPS.md#upgrade-and-rollback). Automatic
process switching, signed downloads and automatic updates remain open.

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
notarization, consumer qualification of the Windows installer and a Mac application
bundle, credential prompts across
signed upgrades, upgrades with closed backups and rollback, broader Linux
distribution qualification, and a native macOS worker. Linux has a bounded KVM
development preview, not general distribution qualification. A
self-contained .NET package still needs its operating system's native runtime
dependencies. Physical phone setup remains deferred and user-operated.

References: [Microsoft macOS deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos)
and [GitHub native runner matrix](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).

## Invited Windows browser preview (September 22 working candidate)

The in-development Windows candidate bundles the pinned browser runtime and
requires Google Chrome to be installed. Ask in chat to open Chrome for a concrete
website task. Review the exact objective, website hosts and allowance before it
opens. Clicks and form actions receive a separate one-time review in chat.
Pause and Take over stop AI work; enter passwords and complete CAPTCHA in Chrome,
then Resume AI. Close Chrome ends authorization and retains the receipts.

This uses a dedicated saved profile under `%LOCALAPPDATA%/Thaddeus/BrowserProfiles`,
not the owner's everyday Chrome profile. The profile is outside study exports
and backups. Website content remains untrusted. The host limits its own reading
and actions to reviewed public HTTPS hosts; this is not a network firewall for
website scripts, redirects or third-party resources. Sensitive forms are left to
the owner. No model-authored JavaScript, shell, cookies or local-file tools are
exposed. A submission with an uncertain transport outcome is not automatically
repeated. Restart ends browser authorization; it does not resume automation.

Browser allowances default to eight model calls, twelve browser actions, 64,000
tokens and 600 active seconds. Proposal usage counts; the chat Info panel allows
lower browser limits. Lower reply token/time ceilings still apply. Waiting for
review or manual sign-in is excluded from active time, and scope authorization
ends after two hours. These fixtures do not establish live-site acceptance.

This candidate advances study schema to 12. Preserve a closed-study backup before
upgrading; a schema-11 package cannot open the upgraded study. The owner's current
schema-11 study and known-good package have not been upgraded by fixture testing.
