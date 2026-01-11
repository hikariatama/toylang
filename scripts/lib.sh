#!/usr/bin/env bash

if [[ -n "${__COMMON_LIB_SH:-}" ]]; then
	return
fi

__COMMON_LIB_SH=1

ESC=$'\033'
RESET="${ESC}[0m"
BOLD="${ESC}[1m"
DIM="${ESC}[38;5;237m"
GREEN="${ESC}[32m"
YELLOW="${ESC}[33m"
RED="${ESC}[31m"
CYAN="${ESC}[36m"

log_info() {
	printf "${CYAN}»${RESET} %s\n" "$*"
}

log_warn() {
	printf "${YELLOW}!${RESET} %s\n" "$*"
}

log_error() {
	printf "${RED}✗${RESET} %s\n" "$*" >&2
}

log_success() {
	printf "${GREEN}✓${RESET} %s\n" "$*"
}

require_command() {
	local cmd=$1
	local message=${2:-"Command '${cmd}' is required."}
	if ! command -v "$cmd" >/dev/null 2>&1; then
		log_error "$message"
		exit 1
	fi
}

__spinner_pid=""

start_spinner() {
	local label=${1:-Working}
	if [ -t 1 ] && [ -z "${__spinner_pid}" ]; then
		(
			local frames=('⠋' '⠙' '⠹' '⠸' '⠼' '⠴' '⠦' '⠧' '⠇' '⠏')
			local i=0
			while true; do
				printf "\r\033[2K${CYAN}%s${RESET} ${DIM}%s${RESET}" "${frames[$i]}" "$label"
				i=$(( (i + 1) % ${#frames[@]} ))
				sleep 0.05
			done
		) &
		__spinner_pid=$!
	fi
}

stop_spinner() {
	if [ -n "${__spinner_pid}" ]; then
		kill "${__spinner_pid}" 2>/dev/null || true
		wait "${__spinner_pid}" 2>/dev/null || true
		__spinner_pid=""
		printf "\r\033[2K"
	fi
}
