#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/.."
dotnet test --nologo
npm --prefix web run build
echo 'Checks passed. Browser tests require the demo host. The raven approves of evidence.'
