# Two-human native conversation acceptance

This script is for a real owner and a separate real collaborator. The current checkout has **not** passed it. Fixture profiles, a second owner tab, or another agent persona do not count as two humans.

## Prerequisites

1. Confirm the current host is serving port 5189 and `/api/marketing/state` advertises `sharedGatewayEnabled=true` for the owner. The current localhost host is loaded, but no shared session was created; its loopback start attempt returned 409 without changing the project.
2. Configure a trusted HTTPS phone origin or identity proxy for the cockpit. The current host listens only on `localhost:5189`; entering the machine's LAN IP in a browser will not reach it. The host requires an exact HTTPS `Thaddeus:PhoneOrigin` for direct network access, or its `tailscale` proxy mode with a trusted loopback proxy that forwards an observed non-loopback client address. The current localhost URL cannot start the native suggestion path: OpenClaw rejects a loopback client as unattributable. Keep the isolated `marketing-shared-hire` Gateway without a published port and do not expose the original Gateway Control UI.
3. The collaborator pairs a **separate** device/session through Settings → Access. The owner approves that exact device for the project. Do not share the owner's host key or browser session.

## Read-only and no-model phase

1. Owner opens **Work → Marketing project**, confirms the saved project ID and authorizes the paired collaborator device. Owner connects one native shared conversation for that project.
2. Collaborator opens the same project from their own authenticated device. Both sides read the same shared session ID. The collaborator submits one specific project suggestion; record its exact suggestion ID, authenticated profile ID, and text.
3. Owner sees the suggestion, imports/reconciles its exact receipt into the project input ledger, then refreshes. Both devices see the attributed note once. Repeat the same request ID to prove no duplicate note appears.
4. Try direct forbidden Gateway methods and owner-only host controls from the collaborator device. They must fail without exposing private owner sessions, changing budgets, approving a draft, dispatching work, or creating a new task.

These steps can be run without a model call. They prove two human identities and a shared suggestion path only after actual people complete them; a synthetic fixture is not evidence of this phase.

## Reply phase, only after metering is proved

1. Confirm the provider-request/token guard rejects the next request **before dispatch** at both configured ceilings, preserves the OAuth subscription route, and leaves paid fallbacks disabled.
2. Owner authorizes one exact bounded revision grant after reviewing an artifact. Confirm the linked grant is unexpired, cites the predecessor review and artifact digest, and passes a fresh pilot-wide budget check.
3. Permit one Marketing reply in the shared session. Both humans should see the same actual agent reply and its authenticated conversation receipt. Confirm the saved revised artifact, usage/request receipts, and owner review controls in Work. No publication, outreach, account mutation, or spending follows from this proof.

Record fixture results, real human participation, model requests, and any unknown outcomes separately. If identity ingress, metering, or a second person is unavailable, stop at the last proved phase and retain the exact blocker.
