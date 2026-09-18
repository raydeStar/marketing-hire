# Model connections and credentials

In **Settings → Connect a model**, select a compatible provider, its base API URL,
the exact model ID and reasoning effort. Choose how to authenticate, then save.
Saving does not make a model call. **Check saved connection** requests only a
model list; it does not generate text or prove tool support. Observed capabilities
come from retained runs for that exact connection.

Reasoning labels are provider capabilities, not interchangeable promises. Select
only a mode the chosen endpoint advertises. For example, LM Studio's native model
catalog reports the allowed reasoning modes and whether a model was trained for
tool use; an OpenAI-compatible endpoint may still accept a label that its loaded
model maps differently. **Off** is an explicit no-thinking mode for providers that
support `reasoning_effort: none`; it can help a tightly bounded tool call, but it
also removes planning.

For explicit new-app requests on a compatible Qwen3.8 model, Thaddeus uses the
configured reasoning mode for a small, schema-bounded design plan and records it
without creating an app. A second call uses `none` only to emit the bounded HTML,
CSS and JavaScript from that plan. The task receipt records each call's actual
reasoning mode and purpose. The final app is still one atomic save: cancellation,
failure or budget exhaustion during emission leaves no partial app. Ordinary chat,
clarification and later app edits continue to use the configured mode.

| Choice | Lifetime and location |
|---|---|
| Save in the system credential store | Windows Credential Manager, macOS Keychain or Linux Secret Service, under the current host user |
| Keep only until this host stops | Host memory; enter the key again after a restart |
| This endpoint needs no API key | Explicit unauthenticated connection |
| Host environment key | Existing `Thaddeus__ApiKey`, bound to `Thaddeus__ApiKeyEndpoint` or the compatible endpoint saved when the host starts |

If the native store is locked or unavailable, unlock it and retry or explicitly
choose session storage. There is no automatic plaintext fallback. Linux requires
libsecret and a running, unlocked Secret Service in the host user's desktop
session. A headless installation can deliberately choose session storage.

The ordinary owner browser on the host can manage keys. Paired devices cannot.
The password field clears when saving begins. Keys are not stored in browser
local/session storage, SQLite, notes, exports, worker configuration or command
arguments. The selected provider stores an opaque credential reference; saved
credential metadata records its endpoint, storage type and lifecycle status.

## Destination and lifecycle

A key is bound to one canonical base API endpoint, including its path. Changing
that destination requires an explicit authentication choice. Redirects, cookies
and automatic proxy inheritance are disabled on provider HTTP clients. HTTPS is
required outside loopback. A removed or missing credential fails before model
dispatch and token reservation; it never silently becomes an anonymous request.

Manage saved credentials below the connection form. Removing an entry verifies
its absence from the native store. Finish or cancel active work using the entry
before removal. Removal here does not revoke the provider's own API key.
Replacing a connection leaves earlier entries visible for deliberate cleanup.
An interrupted or failed save retains a pending entry and the previously selected
connection. An interrupted removal remains visible for retry and cannot dispatch.

The host caches resolved keys in memory while running. Locking the operating
system keyring after resolution does not revoke an already cached key; use
Thaddeus's removal control and the provider's revocation mechanism as appropriate.
The OS store protects persistence under the user's account. It is not a boundary
against arbitrary software already running as that same user. The isolated agent
does not receive a key or access to these host processes.

Each native operation runs in a short-lived mode of the trusted host executable,
with bounded stdin/stdout and a 15-second parent deadline. Credentials travel over
stdin, never argv; inherited provider secrets are stripped from the helper's
environment. Only metadata is committed to the application database. Native
writes are read back before the connection is selected. No custom encryption,
keyring dependency package or background credential daemon is installed by the app.

Token totals remain at the top of the study, with a separate allowance for each
message. Reported tokens are not a provider bill. Requested output limits are
not certified hard caps; strict admission rejects providers without a verified
bound. Connecting a provider does not acquire the shared GPU resource lease.

## Verification boundary

Domain and owner-API tests cover endpoint binding, stale updates, session expiry,
missing keys, pending writes, removal, export redaction and rejection before
dispatch/reservation. The browser check uses a fictional key and a local model-list
server at desktop and 390-pixel widths. Neither performs inference.

The native package matrix checks Windows x64, Linux x64 and both Mac architectures.
It saves a fictional system key through the actual product API, stops and restarts
the extracted host, authenticates model-list discovery again, removes the key and
verifies that subsequent discovery makes no HTTP request. A separate helper check
covers exact Unicode bytes, replacement and missing entries across processes.
Only a successful target receipt establishes native verification; a build alone
does not. Linux CI uses a fresh private DBus/keyring session, not the user's keyring.

Signed application identity, consumer Keychain prompts across upgrades, locked
desktop sessions and broader Linux distributions require separate qualification.
These checks do not qualify a worker VM, model quality or physical-phone setup.

Native references: [Windows credential structure](https://learn.microsoft.com/en-us/windows/win32/api/wincred/ns-wincred-credentiala),
[Apple Keychain services](https://developer.apple.com/documentation/security/keychain-services),
[libsecret synchronous storage](https://gnome.pages.gitlab.gnome.org/libsecret/func.password_store_sync.html).
