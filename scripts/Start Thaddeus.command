#!/bin/sh
set -eu
umask 077
cd -- "$(dirname -- "$0")"
exec ./Thaddeus.Host --desktop "$@"
