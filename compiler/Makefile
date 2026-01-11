.DEFAULT_GOAL := build

ARGS ?=

build:
	@scripts/build.sh

run: build
	dotnet run -- $(ARGS)

clean:
	dotnet clean

.PHONY: build run clean
