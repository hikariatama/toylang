#!/usr/bin/env bash

set -u
set -o pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPILER_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
source "${REPO_ROOT}/scripts/lib.sh"

require_command dotnet "dotnet is required to build the compiler."

LOG_FILE="$(mktemp)"

cleanup() {
	stop_spinner
	rm -f "${LOG_FILE}"
}

trap cleanup EXIT

cd "${COMPILER_DIR}"

printf "\r\033[2K${CYAN}»${RESET} Building compiler..."
start_spinner "Building compiler"
if dotnet build -c Release >"${LOG_FILE}" 2>&1; then
	BUILD_STATUS=0
else
	BUILD_STATUS=$?
fi
stop_spinner

if [[ ${BUILD_STATUS} -eq 0 ]]; then
	printf "\r\033[2K${GREEN}✓${RESET} Build succeded\n"
else
	printf "\r\033[2K${RED}✗${RESET} Build failed\n\n"
	cat "${LOG_FILE}"
fi

exit "${BUILD_STATUS}"
