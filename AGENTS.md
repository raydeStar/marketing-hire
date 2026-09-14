# Development constraints

The owner has limited disk space and requires cleanup as part of testing.

- Before a large build, package, image copy or VM test, check available space and
  budget its peak allocation while retaining at least 10 GiB free. Refuse the run
  when it cannot fit. Do not treat a sparse file's logical size as reclaimed bytes.
- Use existing pinned immutable images and fresh small overlays for routine VM
  checks. Do not create another 8–13 GiB image for every revision.
- Clean disposable disks, exports and build intermediates after test processes
  have exited, including failed runs. Keep compact logs, manifests, hashes and
  receipts. Record what was removed. A fixture needed for a specific follow-up
  may be retained explicitly; remove it when that follow-up finishes.
- Preserve the active application package and worker inputs, one rollback
  package, user data/backups, model weights and unrelated benchmark artifacts.
  Resolve and verify cleanup paths before deleting. Never prune Docker volumes,
  stop unrelated services or compact their disks as incidental test cleanup.
- Use the smallest checks that address the changed behavior. Reuse valid evidence
  for unchanged inputs. No GitHub Actions, live model calls or GPU inference as
  part of routine checks.

See `docs/LOCAL_CHECKS.md` for the supported commands and retention behavior.
