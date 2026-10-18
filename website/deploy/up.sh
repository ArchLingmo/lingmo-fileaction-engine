#!/usr/bin/env sh
# Starts (or restarts) the FileAction website on this server with automatic HTTPS.
#
#   ./up.sh                                  https://<this-server-ip>.sslip.io
#   SITE_ADDRESS=fileaction.example.com ./up.sh
#                                            your own domain; point its DNS A record at this server first
#
# The address is remembered in .env, so later runs (or a reboot) keep it.
# Needs Docker with the compose plugin, and ports 80 and 443 open.
set -eu
cd "$(dirname "$0")"

if [ -n "${SITE_ADDRESS:-}" ]; then
  printf 'SITE_ADDRESS=%s\n' "$SITE_ADDRESS" > .env
elif [ ! -f .env ]; then
  ip="$(curl -fsS4 --max-time 10 https://api.ipify.org || curl -fsS4 --max-time 10 https://ifconfig.me)"
  case "$ip" in
    *.*.*.*) ;;
    *) echo "Couldn't find this server's public IPv4 address; run: SITE_ADDRESS=<ip-with-dashes>.sslip.io $0" >&2; exit 1 ;;
  esac
  printf 'SITE_ADDRESS=%s.sslip.io\n' "$(printf '%s' "$ip" | tr . -)" > .env
fi

. ./.env
docker compose up -d --pull always
echo
echo "FileAction website: https://$SITE_ADDRESS"
echo "(The first visit can take a few seconds while the HTTPS certificate is issued.)"
