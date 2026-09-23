# Open the running study again

The native `Thaddeus.Host --desktop` entry point used by the Windows Start menu
and Unix launchers recognizes the same running package and launch profile.
A Windows desktop host keeps a small tray icon while it runs. Its **Open
Thaddeus** command issues a fresh one-use owner login link using the same host;
**Exit Thaddeus** requests normal host shutdown and removes the icon. The exit
command only stops this host and its owned work. It does not terminate unrelated
applications, an ordinary browser window, or a separately launched model bridge.
A second launch requests a fresh one-minute, one-use owner login link and opens
the browser. It exits without another application host, data-store owner, worker
or model call. `--no-browser` checks readiness without allocating a login ticket.

The channel identity includes the package path, data path, exact origin, worker
port, installation descriptor and OS account name. A different identity cannot
reuse it. The pipe enforces `PipeOptions.CurrentUserOnly` on both endpoints.
The OS account is the trust boundary; this does not defend against another process
already controlled by that same account. No durable host key is read or sent by
this protocol. Only fixed open/readiness commands are accepted, and only the
configured loopback origin plus a validated ticket can become a browser URL.

The server starts after HTTP readiness and stops with the host. Per-connection
deadlines, fixed-size packets and the existing eight-ticket limit bound a stalled
or repeated request. Invalid commands and disconnected clients cannot start work.
A second click during startup gets one bounded connection wait. Unrelated
listeners, another study/build, maintenance, an older host lacking this channel,
or a failed handshake leave existing processes untouched. There is no HTTP
credential probing, automatic port selection or forced restart.

The service uses .NET's named pipes (Unix domain sockets on Unix). With the pinned
.NET 10 runtime, Unix same-user enforcement relies on peer-credential checks;
this does not claim the additional socket-file mode hardening introduced in
[.NET 11](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/namedpipeserverstream-unix-permissions).
Native Mac/Linux desktop acceptance remains separate from Windows evidence.

## Focused validation

The desktop launch and login-ticket tests exercise the fixed IPC protocol,
profile separation, single-use claims, full ticket capacity, disconnect handling
and invalid responses. No search/model service is involved. The opt-in native
check uses a supplied current Windows package and one small fictional study:

```text
node scripts/desktop-reopen-check.mjs HOST_PACKAGE FRESH-NAME
```

It starts the actual executable, repeats the native launch, claims one IPC-issued
ticket through the real HTTP authentication endpoint, and verifies rejection of
another profile and an unrelated HTTP listener. It observes the original process
and unchanged study content. Owned processes and fictional studies are removed;
source/package hashes and compact receipts remain. It needs no VM or GPU and
does not replace the owner's running study. Browser shell-opening is not simulated
as a successful manual click: this fixture uses `--no-browser`, while separately
checking the real ticket-to-owner-session path.
