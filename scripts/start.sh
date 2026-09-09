#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/.."
npm --prefix web ci
npm --prefix web run build
dotnet restore --locked-mode
dotnet build --no-restore
echo 'Open http://localhost:5179. Access key: .data/host-key.txt. The raven has opened the study.'
dotnet run --no-build --project src/Thaddeus.Host --no-launch-profile
