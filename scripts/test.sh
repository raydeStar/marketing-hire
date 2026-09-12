#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/.."
dotnet test --nologo
node --test scripts/luna-protocol.test.mjs workers/openclaw/configuration.test.mjs
npm --prefix web run build
echo 'Checks passed. Browser tests require the demo host. The raven approves of evidence.'
