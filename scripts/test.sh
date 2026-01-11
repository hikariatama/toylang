#!/usr/bin/env bash

set -u
set -o pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
source "${REPO_ROOT}/scripts/lib.sh"

require_command jq "jq is required to read compiler output."
require_command base64 "base64 is required to decode wasm modules."
require_command pnpm "pnpm is required to execute compiled wasm."
require_command wasm-validate "wasm-validate is required to inspect runtime errors."

if [ -z "${COMPILER_PATH:-}" ]; then
	log_error "COMPILER_PATH is not set. Run via 'make test' or export COMPILER_PATH."
	exit 1
fi

cd "$REPO_ROOT"

STATUS_DIR="$(mktemp -d)"
declare -a test_names=()
declare -a test_status=()
declare -a test_message=()
declare -a test_status_file=()
declare -a test_output_file=()
declare -a test_pids=()

cursor_hidden=0

show_cursor() {
	if [ "$cursor_hidden" -eq 1 ]; then
		if command -v tput >/dev/null 2>&1; then
			tput cnorm >/dev/null 2>&1 || true
		fi
		cursor_hidden=0
	fi
}

cleanup() {
	show_cursor
	for pid in "${test_pids[@]}"; do
		wait "$pid" 2>/dev/null || true
	done
	rm -rf "$STATUS_DIR"
}

trap cleanup EXIT

if [ -t 1 ] && command -v tput >/dev/null 2>&1; then
	if tput civis >/dev/null 2>&1; then
		cursor_hidden=1
	fi
fi

tests_dir="$REPO_ROOT/compiler/tests/basic"
shopt -s nullglob
test_files=("$tests_dir"/*.toy)
shopt -u nullglob

if [ ${#test_files[@]} -eq 0 ]; then
	log_warn "No tests found in ${tests_dir}"
	exit 1
fi

SPINNER_FRAMES=('⠋' '⠙' '⠹' '⠸' '⠼' '⠴' '⠦' '⠧' '⠇' '⠏')
spinner_tick=0
rendered_lines=0

render_statuses() {
	local total=${#test_files[@]}
	if [ "$total" -eq 0 ]; then
		return
	fi
	if [ "$rendered_lines" -gt 0 ]; then
		printf "\033[%dF" "$rendered_lines"
	fi
	local frame_count=${#SPINNER_FRAMES[@]}
	for i in "${!test_files[@]}"; do
		local name="${test_names[$i]}"
		local status="${test_status[$i]}"
		local message="${test_message[$i]}"
		local symbol=""
		local status_text=""
		case "$status" in
			running)
				local frame_index=$(( (spinner_tick + i) % frame_count ))
				symbol="${CYAN}${SPINNER_FRAMES[$frame_index]}${RESET}"
				status_text="${DIM}${message:-Running}${RESET}"
				;;
			pass)
				symbol="${GREEN}✓${RESET}"
				status_text="${GREEN}OK${RESET}"
				;;
			fail)
				symbol="${RED}✗${RESET}"
				status_text="${RED}${message:-Failed}${RESET}"
				;;
			*)
				symbol="${DIM}-${RESET}"
				status_text="${DIM}${message:-Pending}${RESET}"
				;;
		esac
		printf "\r\033[2K%s %-30s %s\n" "$symbol" "$name" "$status_text"
	done
	rendered_lines=$total
}

write_stage_error_pretty() {
	local json_file="$1"
	local dest="$2"
	if jq -e '.StageError' "$json_file" >/dev/null 2>&1; then
		jq -r '
			def fmt($v):
				if $v == null then "n/a" else ($v | tostring) end;
			.StageError
			| "\(fmt(.Stage)) (line \(fmt(.Line))): \(fmt(.Message))"
		' "$json_file" >"$dest"
		return 0
	fi
	return 1
}

stage_failure_label() {
	printf "CTE"
}

run_test_worker() {
	local index="$1"
	local file="$2"
	local status_path="$3"
	local output_path="$4"
	local exp="${file}.output"
	local wasm
	local actual
	local log
	local diff_log
	local json_output
	local actual_str
	wasm=$(mktemp)
	actual=$(mktemp)
	log=$(mktemp)
	diff_log=$(mktemp)
	json_output=$(mktemp)

	cleanup_worker() {
		rm -f "$wasm" "$actual" "$log" "$diff_log" "$json_output"
	}

	write_result() {
		local status="$1"
		local message="$2"
		printf "%s|%s\n" "$status" "$message" >"$status_path"
	}

	write_failure_output() {
		local source_file="$1"
		if [ -s "$source_file" ]; then
			cat "$source_file" >"$output_path"
		else
			: >"$output_path"
		fi
	}

	write_stage_failure_output() {
		if ! write_stage_error_pretty "$json_output" "$output_path"; then
			write_failure_output "$log"
		fi
	}

	if [ ! -f "$exp" ]; then
		printf "Missing expected output file %s\n" "$exp" >"$output_path"
		write_result "fail" "PE"
		cleanup_worker
		return
	fi

	if ! "$COMPILER_PATH" -i "$file" >"$json_output" 2>"$log"; then
		write_stage_failure_output
		write_result "fail" "$(stage_failure_label "$json_output")"
		cleanup_worker
		return
	fi

	if ! jq -er '.WasmModuleBase64' "$json_output" 2>>"$log" | base64 -d >"$wasm" 2>>"$log"; then
		write_stage_failure_output
		write_result "fail" "$(stage_failure_label "$json_output")"
		cleanup_worker
		return
	fi

	if ! actual_str=$(cd frontend && pnpm run --silent cli "$wasm" 2>"$log"); then
		local wasm_validate_status
		if wasm-validate "$wasm" >"$output_path" 2>&1; then
			wasm_validate_status=0
		else
			wasm_validate_status=$?
		fi
		if [ ! -s "$output_path" ]; then
			if [ "$wasm_validate_status" -eq 0 ]; then
				printf "wasm-validate: module reported no issues\n" >"$output_path"
			else
				printf "wasm-validate failed without diagnostics (exit %d)\n" "$wasm_validate_status" >"$output_path"
			fi
		fi
		write_result "fail" "RTE"
		cleanup_worker
		return
	fi

	case "$actual_str" in
	*$'\n')
		printf "%s" "$actual_str" >"$actual"
		;;
	*)
		printf "%s\n" "$actual_str" >"$actual"
		;;
	esac

	if diff --color=always -u --strip-trailing-cr --label expected "$exp" --label actual "$actual" >"$diff_log"; then
		: >"$output_path"
		write_result "pass" ""
	else
		write_failure_output "$diff_log"
		write_result "fail" "WA"
		cleanup_worker
		return
	fi

	cleanup_worker
}

for i in "${!test_files[@]}"; do
	test_names[$i]="$(basename "${test_files[$i]}")"
	test_status[$i]="running"
	test_message[$i]="Running"
	test_status_file[$i]="${STATUS_DIR}/${i}.status"
	test_output_file[$i]="${STATUS_DIR}/${i}.output"
	run_test_worker "$i" "${test_files[$i]}" "${test_status_file[$i]}" "${test_output_file[$i]}" &
	test_pids[$i]=$!
done

render_statuses

total=${#test_files[@]}
completed=0

while [ "$completed" -lt "$total" ]; do
	for i in "${!test_files[@]}"; do
		if [ "${test_status[$i]}" = "running" ] && [ -f "${test_status_file[$i]}" ]; then
			IFS='|' read -r result message < "${test_status_file[$i]}"
			test_status[$i]="$result"
			test_message[$i]="$message"
			rm -f "${test_status_file[$i]}"
			completed=$((completed + 1))
		fi
	done

	if [ "$completed" -eq "$total" ]; then
		break
	fi

	spinner_tick=$(( (spinner_tick + 1) % ${#SPINNER_FRAMES[@]} ))
	render_statuses
	sleep 0.05
done

render_statuses
show_cursor
printf "\n"

failed=0
for i in "${!test_files[@]}"; do
	if [ "${test_status[$i]}" = "fail" ]; then
		failed=$((failed + 1))
		printf "${RED}✗ %s${RESET} %s\n" "${test_names[$i]}" "${test_message[$i]}"
		if [ -s "${test_output_file[$i]}" ]; then
			cat "${test_output_file[$i]}"
			printf "\n"
		fi
	fi
done

if [ "$failed" -ne 0 ]; then
	printf "${RED}%d/%d tests failed${RESET}\n" "$failed" "$total"
	exit 1
else
	printf "${GREEN}All %d tests passed${RESET}\n" "$total"
fi
