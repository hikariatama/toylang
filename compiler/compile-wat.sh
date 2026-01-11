#!/bin/bash

if [ -z "$1" ]; then
    echo "Usage: $0 <path-to-test> <path-to-output>"
    exit 1
fi

if [ -z "$2" ]; then
    echo "Usage: $0 <path-to-test> <path-to-output>"
    exit 1
fi

dotnet run -i $1 | jq -r '.WasmModuleBase64' | base64 -d > /tmp/module.wasm
wasm-objdump -d /tmp/module.wasm
wasm2wat /tmp/module.wasm > $2
rm /tmp/module.wasm
