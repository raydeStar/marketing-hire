#!/bin/bash
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y --no-install-recommends systemd-sysv udev dbus-user-session libpam-systemd libicu74 libssl3t64 e2fsprogs
useradd --uid 1100 --create-home --shell /bin/bash thaddeuscheck
mkdir -p /var/lib/systemd/linger /etc/systemd/system/user@1100.service.d /etc/systemd/system/multi-user.target.wants
touch /var/lib/systemd/linger/thaddeuscheck
cat > /etc/systemd/system/user@1100.service.d/delegate.conf <<'EOF'
[Service]
Delegate=cpu memory pids
EOF
cat > /etc/systemd/system/thaddeus-check.service <<'EOF'
[Unit]
Description=Disposable Thaddeus Linux ownership checks
Requires=user@1100.service
After=user@1100.service
RequiresMountsFor=/opt/probe/bin
[Service]
Type=oneshot
User=thaddeuscheck
Environment=XDG_RUNTIME_DIR=/run/user/1100
Environment=DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1100/bus
ExecStart=/opt/probe/bin/Thaddeus.LinuxProcessCheck
ExecStopPost=+/usr/bin/systemctl --no-block poweroff
TimeoutStartSec=180
StandardOutput=journal+console
StandardError=journal+console
[Install]
WantedBy=multi-user.target
EOF
ln -s /etc/systemd/system/thaddeus-check.service /etc/systemd/system/multi-user.target.wants/thaddeus-check.service
ln -sf /lib/systemd/system/multi-user.target /etc/systemd/system/default.target
chmod +x /opt/probe/bin/Thaddeus.LinuxProcessCheck
printf '/dev/vdb /opt/probe/bin ext4 ro,nodev,nosuid 0 0\n' >> /etc/fstab
dpkg-query -W > /opt/probe/packages.txt
apt-get clean
printf 'The disposable Linux estate is prepared. No host service was changed.\n'
