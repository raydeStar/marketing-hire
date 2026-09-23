# Windows preview presentation walkthrough

This is a short, fictional-data tour of the current owner preview. It is not a
release certificate. The [acceptance matrix](MVP_DELEGATION_ACCEPTANCE.md) keeps
fixture checks, controlled live checks and owner observations separate.

## Prepare before anyone watches

1. Identify the running package, rollback and SHA-256 in
   [the exact candidate handoff](NON_NOTIFICATION_MVP_HANDOFF.md). The owner
   preview currently uses package F at `http://localhost:5279`; the old private
   GitHub prerelease is not the active build. Open the host through its tray menu
   or the guarded `artifacts/preview-inbox-bound-20260922/Start-Preview.cmd` on
   the normal Windows desktop. Keep the host running and awake.
2. Use a **separate fictional study** for a public demonstration. Launch the same
   package with a dedicated `launch.json`, an empty data directory and unused
   loopback ports, following [portable package setup](PORTABLE_PACKAGES.md#optional-launch-profile).
   Do not copy the owner's SQLite study, host key, browser profile or Google
   credentials. Configure the demo model in Settings using a permitted account;
   prove one ordinary reply before the presentation. A new data directory under
   the same Windows account is a demo isolation measure, not fresh-user
   installation acceptance.
   On the owner's current workstation, this study is prepared at
   `artifacts/presentation-study-20260923/` on `http://localhost:5379`. Use its
   `Open-presentation-study.cmd` launcher. Its local receipt records a verified
   package-F manifest, two fictional To-dos, one fictional Idea and one successful
   fictional Luna High reply. The model uses the already running local bridge;
   no model key was saved in the study. Its one live reply used 18,312 charged
   tokens, so avoid repeated rehearsal calls without a reason. The profile and
   data are ignored by Git; they are not in the repository or public materials.
   Confirm that the bridge is running before presenting: the saved setting does
   not start it.
3. The two fictional To-dos and one Idea are already saved through the normal
   UI. The demo model is connected and one fictional reply succeeded; use that
   retained conversation and receipt for the evidence segment. Keep names,
   addresses and external pages free of private data.
   If Google is not connected in the demo study, omit the live Gmail/Calendar
   segment and show the saved, sanitized acceptance evidence instead.
4. Open the app side by side with Chat, set browser zoom so the approval card is
   legible, and keep the [manual QA steps](MVP_DELEGATION_MANUAL_QA.md) nearby.
   Do not schedule an external send or grant a new connector just to fill time.

## Five-minute route

1. **The promise (30 seconds):** “Ask at a time; Thaddeus does the approved work
   and records what happened.” Show Chat and the centered conversation.
2. **A real action (90 seconds):** Ask for a fictional reminder a few minutes
   ahead. Read the exact time and timezone aloud, then approve it in Chat. Open
   Upcoming to show the persisted job. Explain that the host, not the browser
   tab, watches the schedule while the PC stays running and awake.
3. **Useful saved work (90 seconds):** Ask for two To-dos from a short fictional
   note. Review the proposed items, approve, then edit one in Today. Open the
   pinned app beside Chat and show that its saved data remains after a reload.
4. **Trust and evidence (60 seconds):** Open the completed fictional reply. Show
   the human summary, then expand the receipt and its exact source/operation.
   Show the simple Always allow / Always deny list only if an appropriate
   bounded choice is already present; permissions can be removed.
5. **Optional connected or Chrome segment (30 seconds):** If a demo connector is
   ready, show its visible account and permissions and an existing bounded
   read result. Otherwise show a public-page Chrome task's exact site review and
   retained result. Pause/Take over/Resume can be shown only while allowance
   remains; an interrupted call with unknown usage can exhaust it. The current
   recovery is to close that task and review a new one.

Keep Windows native notification delivery out of the main tour until the final
normal-desktop visual/click check is accepted. An in-app result is useful but is
not proof that a person away from the browser saw a notification. Never describe
the unsigned preview as a consumer installer or the private prerelease as the
running candidate. Publication remains paused.
