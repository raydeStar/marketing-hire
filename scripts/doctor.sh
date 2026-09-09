#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/.."
echo 'Thaddeus doctor: inspecting the study, not your secrets.'
dotnet --version
node --version
npm --version
test ! -f src/Thaddeus.Host/wwwroot/index.html || echo 'Frontend built.'
test ! -f .data/host-key.txt || echo 'Host key present.'
curl --silent --output /dev/null --write-out 'Host HTTP status: %{http_code}\n' http://localhost:5179/ || true
