# Phone setup — final manual step

Use **Tailscale Serve** for a private device network and automatically provisioned
HTTPS. No router port forwarding or manual certificate installation is needed.
Tailscale is not currently installed on this computer; no account, hostname or
physical phone has been configured by the development session.

1. Install [Tailscale for Windows](https://tailscale.com/docs/install/windows)
   and the Tailscale app on your phone. Sign both into the same private network.
   If Windows requests elevation, this is the Tailscale installer, published by
   Tailscale Inc.; it installs the VPN client, not a Thaddeus service.
2. Finish or cancel any active Thaddeus task, then stop the current host with
   Ctrl+C. Leave the Luna bridge running in its own terminal.
3. From this repository, run `./scripts/start-phone.ps1`. It reads the computer's
   actual MagicDNS hostname, prints the phone address, and starts Thaddeus on
   loopback with the explicitly configured proxy boundary.
4. In another terminal, run:

   ```powershell
   tailscale serve --https=443 http://localhost:5179
   ```

   Follow Tailscale's HTTPS enablement link if it presents one. Keep the terminal
   open for this development cycle. The command intentionally omits `--bg`, so it
   does not configure persistent sharing. Use **Serve**, not public Funnel.
5. On the computer, open `http://localhost:5179`, unlock with the local host key,
   then Settings → Create one-time pairing code.
6. On the phone, keep Tailscale connected and open the printed **https** address.
   Choose “Connect a phone instead,” enter the code and request pairing. Confirm
   that device in Settings on the computer, then tap “Finish pairing” on the phone.
   The host key stays on the computer. Codes expire after five minutes.
7. Verify one fictional plan: inspect the source notes, approve or deny the exact
   proposal, and confirm the computer sees the same result. Install using the
   browser's “Install app” or “Add to Home Screen” action if offered.
8. Turn off the phone's connection: Thaddeus should show Disconnected and disable
   writes. Reconnect and confirm it catches up. Revoke the phone in host Settings;
   the next private request must fail. Pair again if you want continued access.

Send back the phone OS/browser and which of pairing, plan approval, installation,
offline/reconnect and revocation worked. **Those physical-device checks remain
pending.** Automated real-TLS tests verify the server's pairing/confirmation,
secure cookies, host-only authority and revocation; they cannot certify your phone.

The host must remain awake. Ctrl+C stops the foreground Serve command; Ctrl+C in
the host terminal stops Thaddeus. To return to desktop-only development, use a new
terminal and `./scripts/start.ps1`. Provider settings and notes persist.

Only loopback proxy addresses and one forwarded hop are trusted, with the exact
configured HTTPS hostname. Forwarded remote clients cannot bootstrap an owner
session. Funnel-marked requests are rejected. Tailscale identity headers do not
replace Thaddeus pairing. Direct Kestrel TLS remains available with PhoneMode
`direct` and standard certificate configuration.

References: [Serve command and foreground lifetime](https://tailscale.com/docs/reference/tailscale-cli/serve),
[HTTPS provisioning](https://tailscale.com/docs/how-to/set-up-https-certificates).
