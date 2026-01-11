SHELL := /bin/bash

ESC := \033
RESET := $(ESC)[0m
BOLD := $(ESC)[1m
DIM := $(ESC)[2m
GREEN := $(ESC)[32m
YELLOW := $(ESC)[33m
RED := $(ESC)[31m
CYAN := $(ESC)[36m

.PHONY: build cli test dev start install

CLI_ARGS := $(filter-out build cli test dev start install,$(MAKECMDGOALS))

$(CLI_ARGS):
	@:

cli: $(CLI_ARGS)
	@test -n "$(CLI_ARGS)" || (printf "$(RED)Usage: make cli path/to/file.toy$(RESET)\n" && exit 2)
	@scripts/cli.sh $(CLI_ARGS)

test:
	@$(MAKE) -C compiler build $(MAKEOVERRIDES)
	@COMPILER_PATH=$$(dirname $(abspath $(lastword $(MAKEFILE_LIST))))/compiler/bin/Release/net9.0/CompilersApp \
		scripts/test.sh

build:
	@$(MAKE) -C compiler build $(MAKEOVERRIDES)
	COMPILER_PATH=$$(dirname $(abspath $(lastword $(MAKEFILE_LIST))))/compiler/bin/Release/net9.0/CompilersApp \
		$(MAKE) -C frontend build $(MAKEOVERRIDES)

dev:
	@scripts/dev.sh $(ARGS)

start:
	COMPILER_PATH=$$(dirname $(abspath $(lastword $(MAKEFILE_LIST))))/compiler/bin/Release/net9.0/CompilersApp \
		$(MAKE) -C frontend start $(MAKEOVERRIDES) -- $(ARGS)

install:
	@scripts/install.sh
