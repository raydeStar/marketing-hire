# Private tailnet access for the Marketing pilot

The Marketing host binds to `http://localhost:5189`. Tailscale Serve terminates
HTTPS for devices in the same private tailnet and proxies to
`http://127.0.0.1:5189`. The shared OpenClaw Gateway stays in its isolated
container without a published port. Do not use Tailscale Funnel or forward a
router port for this pilot.

## Start or restart

1. Sign this Windows PC and the test device into the same Tailscale tailnet.
   Confirm `tailscale status` says `Running` and reports a `.ts.net` DNS name.
2. Stop the existing Marketing Windows host if port 5189 is occupied. Preserve
   the Docker Gateway containers and their named volumes.
3. Run `./scripts/start-marketing.ps1 -Tailnet` from this checkout. The script
   reads the active Tailscale DNS name, sets the exact HTTPS phone origin and
   trusted loopback proxy mode, then starts the host. It prints the private
   collaborator address. Keep the host process running for the test.
4. In another terminal, configure private Serve once:

   ```powershell
   tailscale serve --bg --https=443 http://127.0.0.1:5189
   tailscale serve status
   ```

   If Tailscale asks to enable Serve, complete its private Serve setup in the
   browser. The status must say `tailnet only` and proxy to
   `http://127.0.0.1:5189`. This background Serve configuration persists until
   disabled. To turn it off, run `tailscale serve --https=443 off`.

The local owner URL remains `http://localhost:5189/`. The HTTPS URL is for
paired devices. Membership in the tailnet alone does not grant app access:
pair each browser from **Settings** in the right company panel, then grant a specific campaign in
**Work → Campaigns → What changed → Campaign access**. Never give a
collaborator the owner host key.

## Check the route

The private HTTPS home page should load. An unauthenticated request to
`/api/marketing/state` should return HTTP 401, showing that the app is reached
but has not granted access. A real collaborator must then complete the
[two-human acceptance script](REAL_MULTIPLAYER_ACCEPTANCE.md). Testing the URL
from the host itself does not establish a separate person's identity or a
non-loopback client address at the Gateway.

The host must stay awake. If Tailscale disconnects or the host process exits,
the collaborator route will stop working even if Serve remains configured.
