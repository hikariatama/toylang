#!/usr/bin/env bash

set -u
set -o pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
source "${REPO_ROOT}/scripts/lib.sh"

require_command jq "jq is required to parse compiler output."
require_command base64 "base64 is required to decode wasm modules."
require_command pnpm "pnpm is required to execute wasm."
require_command dotnet "dotnet is required to build the compiler."
require_command wasm-validate "wasm-validate is required to inspect runtime failures."

if [ $# -lt 1 ]; then
	log_error "Usage: scripts/cli.sh path/to/file.toy"
	exit 2
fi

target="$1"
wasm_tmp="$(mktemp)"
log_tmp="$(mktemp)"
json_tmp="$(mktemp)"

cleanup() {
	stop_spinner
	rm -f "$wasm_tmp" "$log_tmp" "$json_tmp"
}

trap cleanup EXIT

print_stage_error() {
	local json_file="$1"
	if jq -e '.StageError' "$json_file" >/dev/null 2>&1; then
		jq -r '
			def fmt($v):
				if $v == null then "n/a" else ($v | tostring) end;
			.StageError
			| "Stage failure:",
			  "  Stage    : \(fmt(.Stage))",
			  "  Severity : \(fmt(.Severity))",
			  "  Line     : \(fmt(.Line))",
			  "  Start    : \(fmt(.Start))",
			  "  End      : \(fmt(.End))",
			  "  Message  : \(fmt(.Message))"
		' "$json_file" >&2
		return 0
	fi
	return 1
}

start_spinner "Compiling ${target}"
if dotnet run --project compiler -c Release -- -i "$target" >"$json_tmp" 2>"$log_tmp"; then
	compile_status=0
else
	compile_status=$?
fi
stop_spinner

if [ $compile_status -ne 0 ]; then
	printf "\r\033[2K"
	log_error "Failed to compile ${target}"
	if ! print_stage_error "$json_tmp"; then
		cat "$log_tmp"
	fi
	exit $compile_status
fi

if ! jq -er '.WasmModuleBase64' "$json_tmp" | base64 -d >"$wasm_tmp" 2>"$log_tmp"; then
	printf "\r\033[2K"
	log_error "Compiler produced invalid wasm module for ${target}"
	if ! print_stage_error "$json_tmp"; then
		cat "$log_tmp"
	fi
	exit 1
fi

printf "\r\033[2K${GREEN}✓${RESET} Built ${BOLD}%s${RESET}\n" "$target"
log_info "Running wasm..."
if ! (cd "${REPO_ROOT}/frontend" && pnpm run --silent cli "$wasm_tmp"); then
	log_error "Runtime error while executing wasm."
	log_info "wasm-validate output:"
	wasm-validate "$wasm_tmp"
	exit 1
fi
