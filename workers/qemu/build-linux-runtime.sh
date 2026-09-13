#!/bin/bash
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y --no-install-recommends ca-certificates curl gnupg build-essential ninja-build python3 python3-venv python3-pip python3-wheel python3-setuptools meson pkg-config libglib2.0-dev libgnutls28-dev zlib1g-dev libfdt-dev
mkdir -p /build/gnupg /output/downloads /output/runtime/bin /output/runtime/lib /output/runtime/share
chmod 700 /build/gnupg
curl --fail --location --max-time 180 https://download.qemu.org/qemu-11.1.0.tar.xz -o /output/downloads/qemu-11.1.0.tar.xz
curl --fail --location --max-time 60 https://download.qemu.org/qemu-11.1.0.tar.xz.sig -o /output/downloads/qemu-11.1.0.tar.xz.sig
curl --fail --location --max-time 60 https://keys.openpgp.org/vks/v1/by-fingerprint/CEACC9E15534EBABB82D3FA03353C9CEF108B584 -o /output/downloads/release-key.asc
gpg --homedir /build/gnupg --batch --import /output/downloads/release-key.asc
gpg --homedir /build/gnupg --batch --status-fd 1 --verify /output/downloads/qemu-11.1.0.tar.xz.sig /output/downloads/qemu-11.1.0.tar.xz > /output/signature.txt 2>&1
grep -q 'VALIDSIG CEACC9E15534EBABB82D3FA03353C9CEF108B584 ' /output/signature.txt
printf '6ee1d1a61f68212476b27108c26da5f449dc09b626d42f8279ba0dc2e08fa858  /output/downloads/qemu-11.1.0.tar.xz\n' | sha256sum --check
# The recorded signer is expired. This is a pinned development build, not publisher qualification.
sha256sum /output/downloads/* > /output/download-sha256.txt
tar -xJf /output/downloads/qemu-11.1.0.tar.xz -C /build
mkdir /build/compiled
cd /build/compiled
../qemu-11.1.0/configure --prefix=/opt/thaddeus-qemu --target-list=x86_64-softmmu --without-default-features --enable-kvm --enable-gnutls --enable-tools --disable-tcg --disable-docs --disable-download --disable-werror
ninja -j2 qemu-system-x86_64 qemu-img
cp qemu-system-x86_64 qemu-img /output/runtime/bin/
strip /output/runtime/bin/qemu-system-x86_64 /output/runtime/bin/qemu-img
cp -rL /build/qemu-11.1.0/pc-bios /output/runtime/share/qemu
python3 - <<'PY'
import hashlib, pathlib, re, shutil, subprocess
root = pathlib.Path('/output/runtime')
libraries = {}
for executable in (root/'bin').iterdir():
    text = subprocess.check_output(['ldd', str(executable)], text=True)
    for path in re.findall(r'(/[^\s()]+)', text):
        original = pathlib.Path(path)
        digest = hashlib.sha256(original.read_bytes()).hexdigest()
        if original.name in libraries:
            assert libraries[original.name] == digest, 'Conflicting library basename'
        else:
            shutil.copyfile(original, root/'lib'/original.name)
            libraries[original.name] = digest
PY
chmod +x /output/runtime/lib/ld-linux-x86-64.so.2
env -i /output/runtime/lib/ld-linux-x86-64.so.2 --inhibit-cache --library-path /output/runtime/lib /output/runtime/bin/qemu-system-x86_64 --version > /output/version.txt
dpkg-query -W > /output/build-packages.txt
printf 'The Linux runtime has its own library cabinet. Its first worker boot comes next.\n'
