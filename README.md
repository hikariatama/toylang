<img src="https://github.com/user-attachments/assets/00c605c9-73cd-4fd7-9aff-fcb9650d251c" width="200" />

![academic project](https://img.shields.io/badge/academic%20project-3DB420)
![source only](https://img.shields.io/badge/source%20only-545454)

> WASM compiler for the invented object-oriented language, built from scratch in C#.

<img width="4014" height="1445" alt="image" src="https://github.com/user-attachments/assets/5af48260-eec7-43e6-8dd3-5924d2edf931" />

## Prerequisites

* .NET 9 SDK
* Node 18+ + Pnpm
* wasi-sdk/wasm-tools

Make sure `dotnet`, `npm`, `pnpm`, `nodemon` and `wasm-validate` are on your `$PATH`.

## Auto-tests

```bash
make install
make test
```

## Manual tests

```bash
make install
make cli compiler/tests/donut.toy
make cli compiler/tests/os.toy
# ...
```

## Starting the web UI

```bash
make install
make build
make start
```

Open http://localhost:3000.

## Style

* C#: `dotnet format` with the default style.
* TS/JS: prettier via `npx prettier . --fix`.
