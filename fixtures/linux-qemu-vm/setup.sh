#!/bin/bash
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y --no-install-recommends kmod squashfs-tools p7zip-full python3
mkdir /module-source
7z x /alpine.iso boot/modloop-virt -o/module-source -y >/dev/null
unsquashfs -d /module-source/extracted /module-source/boot/modloop-virt >/dev/null
cp -a /module-source/extracted/modules/. /lib/modules/
depmod -a 6.18.35-0-virt
getent group kvm >/dev/null || groupadd --system kvm
usermod -a -G kvm thaddeuscheck
printf 'kvm_intel\n' > /etc/modules-load.d/thaddeus-check.conf
printf 'KERNEL=="kvm", GROUP="kvm", MODE="0660"\n' > /etc/udev/rules.d/80-thaddeus-check.rules
sed -i 's|ExecStart=/opt/probe/bin/Thaddeus.LinuxProcessCheck|ExecStart=/opt/probe/bin/Thaddeus.LinuxProcessCheck --session-check /opt/probe/bin/installation.json /home/thaddeuscheck/qemu-check|;s/TimeoutStartSec=180/TimeoutStartSec=270/' /etc/systemd/system/thaddeus-check.service
dpkg-query -W > /output/packages.txt
printf 'Nested KVM modules are prepared in the disposable image. The host keeps its own keys.\n'
