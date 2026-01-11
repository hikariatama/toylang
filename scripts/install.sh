#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
source "${REPO_ROOT}/scripts/lib.sh"

if [[ $EUID -ne 0 ]]; then
	if command -v sudo >/dev/null 2>&1; then
		SUDO="sudo"
	else
		log_error "Please run this script as root or install sudo."
		exit 1
	fi
else
	SUDO=""
fi

run_with_sudo() {
	if [[ -n "${SUDO}" ]]; then
		"${SUDO}" "$@"
	else
		"$@"
	fi
}

APT_UPDATED=0
mark_apt_cache_dirty() {
	APT_UPDATED=0
}

apt_update_if_needed() {
	if [[ ${APT_UPDATED} -eq 0 ]]; then
		log_info "Updating apt package lists..."
		run_with_sudo apt-get update
		APT_UPDATED=1
	fi
}

apt_install() {
	if [[ $# -eq 0 ]]; then
		return
	fi
	apt_update_if_needed
	log_info "Installing packages: $*"
	DEBIAN_FRONTEND=noninteractive run_with_sudo apt-get install -y "$@"
}

install_prereqs() {
	apt_install ca-certificates curl gnupg lsb-release software-properties-common build-essential pkg-config
}

ensure_jq() {
	if ! command -v jq >/dev/null 2>&1; then
		apt_install jq
	fi
}

ensure_wabt() {
	if ! command -v wasm-validate >/dev/null 2>&1; then
		apt_install wabt
	fi
}

dotnet_has_required_sdk() {
	if ! command -v dotnet >/dev/null 2>&1; then
		return 1
	fi
	while IFS= read -r line; do
		local version="${line%% *}"
		local major="${version%%.*}"
		if [[ -n "${major}" && "${major}" =~ ^[0-9]+$ && "${major}" -ge 9 ]]; then
			return 0
		fi
	done < <(dotnet --list-sdks 2>/dev/null || true)
	return 1
}

install_dotnet() {
	if dotnet_has_required_sdk; then
		return
	fi

	local distro
	local release
	distro="$(lsb_release -is | tr '[:upper:]' '[:lower:]')"
	release="$(lsb_release -rs)"
	local config_url=""

	case "${distro}" in
		ubuntu|debian)
			config_url="https://packages.microsoft.com/config/${distro}/${release}/packages-microsoft-prod.deb"
			;;
		*)
			log_error "Automatic .NET installation is not supported on ${distro}. Please install dotnet-sdk-9.0 manually."
			exit 1
			;;
	esac

	local config_pkg
	config_pkg="$(mktemp)"
	if ! curl -fsSL "${config_url}" -o "${config_pkg}"; then
		if [[ "${distro}" == "ubuntu" ]]; then
			local major_release="${release%%.*}.04"
			config_url="https://packages.microsoft.com/config/ubuntu/${major_release}/packages-microsoft-prod.deb"
			if ! curl -fsSL "${config_url}" -o "${config_pkg}"; then
				log_error "Failed to download Microsoft package config for Ubuntu ${release}."
				exit 1
			fi
		else
			log_error "Failed to download Microsoft package config for ${distro} ${release}."
			exit 1
		fi
	fi

	run_with_sudo dpkg -i "${config_pkg}"
	rm -f "${config_pkg}"
	mark_apt_cache_dirty
	apt_install dotnet-sdk-9.0
}

node_meets_requirement() {
	if ! command -v node >/dev/null 2>&1; then
		return 1
	fi
	local version
	version="$(node -v | sed 's/^v//')"
	local major="${version%%.*}"
	if [[ -n "${major}" && "${major}" =~ ^[0-9]+$ && "${major}" -ge 18 ]]; then
		return 0
	fi
	return 1
}

install_node() {
	if node_meets_requirement; then
		return
	fi

	log_info "Installing Node.js 18..."
	local nodesource_script
	nodesource_script="$(mktemp)"
	curl -fsSL https://deb.nodesource.com/setup_18.x -o "${nodesource_script}"
	run_with_sudo bash "${nodesource_script}"
	rm -f "${nodesource_script}"
	mark_apt_cache_dirty
	apt_install nodejs
}

install_pnpm() {
	if command -v pnpm >/dev/null 2>&1; then
		return
	fi

	if ! command -v corepack >/dev/null 2>&1; then
		log_info "Installing pnpm globally via npm..."
		run_with_sudo npm install -g pnpm
		return
	fi

	log_info "Enabling pnpm via corepack..."
	corepack enable
	corepack prepare pnpm@latest --activate || run_with_sudo corepack prepare pnpm@latest --activate
}

install_compiler_dependencies() {
	log_info "Restoring compiler dependencies..."
	dotnet restore "${REPO_ROOT}/compiler/CompilersApp.csproj"
	log_success "Compiler dependencies restored."
}

install_frontend_dependencies() {
	cd "${REPO_ROOT}/frontend"
	log_info "Installing frontend dependencies..."
	pnpm install
	log_success "Frontend dependencies installed."
}

install_prereqs
ensure_jq
ensure_wabt
install_dotnet
install_node
install_pnpm
install_compiler_dependencies
install_frontend_dependencies

log_success "All dependencies installed successfully."
