#!/usr/bin/env bash
set -euo pipefail
umask 077
root="$PWD/artifacts/credential-linux-session"
mkdir "$root"
export XDG_DATA_HOME="$root/data"
export XDG_RUNTIME_DIR="$root/runtime"
mkdir "$XDG_DATA_HOME" "$XDG_RUNTIME_DIR"
# This is a fresh CI bus and keyring, holding fictional test values only.
printf '%s' 'fictional-ci-keyring-password' | gnome-keyring-daemon --foreground --unlock --components=secrets >"$root/keyring.log" 2>&1 &
keyring_pid=$!
trap 'kill "$keyring_pid" 2>/dev/null || true; wait "$keyring_pid" 2>/dev/null || true' EXIT
gdbus wait --session --timeout 15 org.freedesktop.secrets
node scripts/portable-check.mjs artifacts/portable-ci artifacts/portable-check-ci
node scripts/credential-vault-check.mjs artifacts/portable-ci/thaddeus-linux-x64/Thaddeus.Host artifacts/credential-native-ci
