# Two-human campaign conversation acceptance

This is a test for the owner and a separate real collaborator. It has **not**
passed yet. The isolated browser fixture used two authenticated sessions on one
machine; it did not establish two-person participation. See
[the current handoff](NEXT_SPRINT_HANDOFF.md) for the last verified state.

## Prepare access

1. Keep the isolated `marketing-shared-hire` Gateway private, with no published
   port. The host currently serves only `http://localhost:5189/`. Give the
   collaborator a trusted HTTPS address that reaches the host and lets it
   observe the collaborator's actual non-loopback client address. Configure an
   exact `Thaddeus:PhoneOrigin`, or the authenticated `tailscale` proxy mode.
   A LAN IP typed into the current localhost-only host will not work.
2. In **Settings → Access**, start a pairing. The collaborator claims it from
   their own device and browser session; the owner confirms that device. Do
   not share the owner host key or browser session.
3. Owner opens **Work → Campaigns** and chooses a saved campaign whose exact
   draft and digest they want to test. In **What changed → Campaign access**,
   grant that paired device access to this campaign. The collaborator should
   see only granted campaigns in **Work → Shared campaigns**.

## Test the saved, no-model path

1. Owner clicks **Connect native conversation** for the selected campaign.
   Confirm it shows connected and does not dispatch the employee or spend
   model tokens. A local key-authenticated owner session may use this action;
   the separate HTTPS address is required for collaborator input.
2. Collaborator opens the same saved draft in **Shared campaigns** and posts
   one specific comment. Both views should show one version-linked discussion
   entry and a Gateway profile receipt. The host automatically records the
   corresponding project input; there is no manual import step.
3. Reload both views. Confirm the comment, device name, source input link,
   and Gateway profile receipt persist once. Repeating the same request ID
   must not create a second note.
4. From the collaborator session, try owner Chat, worker controls, campaign
   authorization, another unshared campaign, and direct private Gateway
   methods. These must fail without changing budgets or dispatching work.
   Revoke campaign access and confirm the collaborator loses the shared view.

Record sanitized device, profile, campaign, and request IDs with the result.
This phase verifies human attribution and saved input only; a native suggestion
is not an employee reply, worker run, or approval to publish or spend.

## Later reply and execution acceptance

An actual employee reply in the shared session, live event delivery, and a
fresh worker revision remain future work. Before trying them, prove the
provider-request and token ceilings reject a request before dispatch, preserve
the existing OAuth route, and leave paid fallbacks disabled. The owner must
review an exact artifact and authorize a bounded revision grant. Confirm the
predecessor, artifact digest, usage receipts, and resulting review controls.
No publication, outreach, account mutation, or spending follows from this
acceptance test.
