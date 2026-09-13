# Research from the ordinary composer

The product now owns the research lifecycle through `ResearchCoordinator` and a
hosted pump. OpenClaw remains the only agent/model/tool loop. A research message
freezes selected notes, allowed public hostnames, provider, policy and budget.
Worker creation and native commands follow durable intents. A broker question
or proposal first causes native quiescence, a filesystem checkpoint and whole
worker shutdown. Only then can the user answer or decide through the normal API.

An answer queues continuation with a fresh scoped grant and the existing native
session. An import requires an independent worker artifact readback matching the
exact proposal before the usual digest, expiry, source-version and destination
checks. Verified import establishes exact saved content, not research accuracy.
The stopped workspace is retired and retained. Another task can then start.

Startup reconciles local transitions after committed answers and decisions. It
does not replay uncertain execution commands or automatically continue queued
work. Failed cleanup blocks a new worker. Settings now provides an explicit
[workspace inspection and removal](WORKSPACE_MAINTENANCE.md) flow. It preserves
imported notes and task receipts; separate personal-data deletion is refused
until worker storage and ownership are reconciled.

## Admission and evidence

The default product factory still refuses research execution pending worker
qualification. There is no browser admission flag, automatic QEMU fallback, or
host-execution bypass. The Windows QEMU factory is composed explicitly by the
test-only `tools/Thaddeus.ResearchCheck` executable.

That fixture runs the real product entry point with
[WebApplicationFactory.UseKestrel](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1.usekestrel?view=aspnetcore-10.0).
Only the worker factory and model transport are replaced; the actual API,
authentication, background pump, brokers, store and UI perform the workflow.
The model transport uses scripted replies and cannot contact an inference server.
Its declared endpoint is `https://model.fixture.invalid/v1`, and its exact model
is `scripted-native-protocol-fixture`. No GPU or paid model call is involved.

September 12 checkpoint: `artifacts/research-browser-20260912-d` passed a real
OpenClaw/QEMU browser workflow: selected note, real brokered public HTTPS page,
question, page reload, answer, native continuation, exact artifact review,
approval, Markdown readback, export and workspace retirement. It recorded five
synthetic model responses. Screenshots at 1440 and 390 pixels have no horizontal
overflow. `verified.json` and `browser-export.json` retain the receipts. This is
development integration evidence, not production isolation or model efficacy.

The later `artifacts/research-browser-removal-20260912-b` fixture repeated the
workflow and removed its workspace through Settings after exact confirmation.
Independent host verification found the imported note and task receipts intact,
the grant revoked and the private workspace absent. Its 1440/390-pixel screenshots
and before/after exports are retained. See [maintenance evidence](WORKSPACE_MAINTENANCE.md).

Earlier attempts are retained: `-a` failed test-host content-root setup; `-b`
reached review but exposed a long-URL title overflow; `-c` was denied by the local
GPU admission gate because the fictional model name used the Luna-only loopback
endpoint. The final fixture fixes its configuration without weakening that gate.
The rejected case made zero model dispatches. All are outside the main `.data`.

To repeat, start ResearchCheck with a fresh directory under `artifacts` and a
verified QEMU installation JSON. It serves port 5182 and waits at most 12 minutes.
Run `web/tests/research.spec.ts` with `THADDEUS_NATIVE_RESEARCH=1`,
`THADDEUS_TEST_ORIGIN=http://127.0.0.1:5182` and `THADDEUS_TEST_DATA` pointing at that
directory. The browser supplies the normal task decisions. Do not run it against
the user's data. The current fixture also removes its own completed workspace
through the reviewed Settings flow. The native check is explicitly skipped in
ordinary CI.

Production qualification, broader interruption
recovery, v1 mechanism integration, independent Lab and distribution remain open.
