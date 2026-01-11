#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
source "${REPO_ROOT}/scripts/lib.sh"

require_command nodemon "nodemon is required for watching compiler changes."
require_command pnpm "pnpm is required to run the frontend."
require_command make "GNU make is required."

cd "$REPO_ROOT"

nodemon_pid=""
frontend_pid=""

cleanup() {
	stop_spinner
	if [ -n "${nodemon_pid}" ]; then
		kill "${nodemon_pid}" 2>/dev/null || true
		wait "${nodemon_pid}" 2>/dev/null || true
		nodemon_pid=""
	fi
	if [ -n "${frontend_pid}" ]; then
		kill "${frontend_pid}" 2>/dev/null || true
		wait "${frontend_pid}" 2>/dev/null || true
		frontend_pid=""
	fi
}

trap cleanup EXIT INT TERM

log_info "Watching compiler with nodemon..."
nodemon --watch compiler --ext cs --ignore compiler/obj \
	--exec "make -C compiler build && echo 'Rebuilt compiler'" &
nodemon_pid=$!

log_info "Starting frontend development server..."
frontend_cmd=(make -C frontend dev)
if [ $# -gt 0 ]; then
	frontend_cmd+=("--" "$@")
fi
COMPILER_PATH="${REPO_ROOT}/compiler/bin/Release/net9.0/CompilersApp" "${frontend_cmd[@]}" &
frontend_pid=$!

if ! wait "${nodemon_pid}" "${frontend_pid}"; then
	status=$?
else
	status=0
fi

trap - EXIT INT TERM
cleanup
exit $status
