# Check this computer

Settings → Set up this host → **Check this computer** inspects the local host
before a worker package is configured. It reports each prerequisite and an action
to take when something is missing. The owner can repeat it after making a change.
It does not install software, change system settings, create a VM, start a model
or consume model tokens. Ordinary chat remains available when a requirement fails.

The check describes the computer running Thaddeus. A phone, Mac or another browser
connected to that host sees the host's result, not its own device's capabilities.

## Current preview requirements

| Host | Checks |
| --- | --- |
| Windows x64 | Native x64 OS and process; Windows Hypervisor Platform reports an available hypervisor. |
| Linux x64 | Native x64 OS and process; packaged application entry point; this account can query KVM API 12; a reachable systemd user manager at version 255 or newer; cgroup v2 CPU, memory and process controllers delegated to this account. |
| macOS or Arm | Isolated worker support remains unqualified. Use ordinary chat or connect to a supported host. An x64 application running under Arm emulation does not qualify the host. |

Windows uses `WHvGetCapability` without allocating a hypervisor partition. Linux
opens `/dev/kvm` and issues only `KVM_GET_API_VERSION`, without creating a VM.
The Linux service queries use a fixed system executable, a bounded deadline and
output limit, and the current user's service identity. They read controller
availability; each worker still checks its actual applied limits when starting.

A passing report is not worker enrollment or full isolation qualification.
**Check installed worker** separately validates the pinned runtime and image.
Enabling preview research still requires the owner's installation-bound decision.
The prerequisite report is returned to the owner and is not saved as a permanent
claim about future hardware or session state. Paired devices and worker tokens
cannot invoke the owner setup endpoint.

## Verification

Contract tests cover platform selection, systemd response parsing, delegated
controllers, missing prerequisites and owner-only access. Packaged Windows browser
tests exercise the actual read-only check with no configured worker, at desktop
and phone viewport sizes, and verify unchanged tasks and provider configuration.
The Linux packaged fixture calls the same endpoint in its native user session
before worker enrollment. Full research continuation remains a separate check;
passing prerequisites alone cannot establish it.

See [local validation](LOCAL_CHECKS.md), [preview setup](HOST_SETUP_PREVIEW.md) and
[the Linux product preview](LINUX_PRODUCT_PREVIEW.md).

API references: [Windows capability query](https://learn.microsoft.com/en-us/virtualization/api/hypervisor-platform/funcs/whvgetcapability),
[KVM API](https://docs.kernel.org/virt/kvm/api.html),
[systemd controller delegation](https://systemd.io/CGROUP_DELEGATION/).
