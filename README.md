# Easy Dotnet Server

## Description

**Easy Dotnet Server** is the lightweight C# JSON-RPC server powering the [easy-dotnet.nvim](https://github.com/GustavEikaas/easy-dotnet.nvim) Neovim plugin.

The server communicates via named pipes using JSON-RPC and provides a unified response format for the plugin.

## Features

* JSON-RPC 2.0 communication over named pipes
* Asynchronous, multi-client server support
* MsBuild integration
* Nuget integration
* MTP integration
* VsTest integration

## Use Case

This server is an internal component of the `easy-dotnet.nvim` plugin and is **not intended for standalone use**.

## 📚 RPC API Documentation

All RPC methods exposed by the server are documented in the auto-generated [`rpcDoc.md`](./rpcDoc.md) file.

This file includes:
- JSON-RPC method names
- Parameter names, types, and optionality
- Return types
- The associated controller for each method

You can regenerate this file at any time by running the server with the `generate-rpc-docs` command:

```bash
dotnet run generate-rpc-docs -- --format markdown
```

The server also exposes a machine-readable health command:

```bash
dotnet-easydotnet healthcheck
```

It returns a deterministic JSON array of health items with `type`, `name`, `value`, and `advice` fields. Use `--format markdown` for readable terminal output.
Pass `--debugger-bin-path <PATH>` to make the debugger health rows reflect a client-provided debugger path.
Pass `--debugger-engine netcoredbg|dncdbg|sharpdbg|custom` to check a specific debugger engine.

## Configuration

### Custom Roslyn LSP Server

easy-dotnet uses the official `roslyn-language-server` .NET global tool for Roslyn LSP. If it is missing, the server can install it with:

```bash
dotnet tool install --global roslyn-language-server --prerelease
```

For testing purposes or workarounds, you can override the Roslyn language server using environment variables:

#### Environment Variables

| Variable | Description |
|----------|-------------|
| `EASY_DOTNET_ROSLYN_DLL_PATH` | Full path to `Microsoft.CodeAnalysis.LanguageServer.dll` |

#### Examples
```bash
# Use a custom Roslyn LSP DLL (most common for testing)
export EASY_DOTNET_ROSLYN_DLL_PATH="/path/to/custom/Microsoft.CodeAnalysis.LanguageServer.dll"
```

### Debugger

easy-dotnet bundles `netcoredbg` as the default debugger. Three bundled engines are available, plus a `custom` option for bringing your own binary.

| Engine | Description |
|--------|-------------|
| `netcoredbg` | Default. Stable, widely supported. |
| `dncdbg` | Experimental fork of netcoredbg — faster-moving, picks up fixes earlier. |
| `sharpdbg` | SharpDbg — a newer debugger that handles `runInTerminal` natively. Proxy polyfills are minimal. |
| `custom` | User-supplied binary. All proxy polyfills are disabled by default since the debugger's capabilities are unknown. |

#### Environment Variables

| Variable | Description |
|----------|-------------|
| `EASY_DOTNET_DEBUGGER_ENGINE` | Debugger engine to use: `netcoredbg`, `dncdbg`, `sharpdbg`, or `custom`. Defaults to `netcoredbg`. |
| `EASY_DOTNET_DEBUGGER_BIN_PATH` | Full path to a debugger executable. Overrides the bundled path for known engines; required for `custom`. |
| `EASY_DOTNET_DEBUGGER_BIN_ARGS` | Space-separated CLI arguments passed to the debugger process when using the `custom` engine. |

#### Examples
```bash
# Try the bundled experimental dncdbg debugger
export EASY_DOTNET_DEBUGGER_ENGINE="dncdbg"

# Use the bundled SharpDbg engine
export EASY_DOTNET_DEBUGGER_ENGINE="sharpdbg"

# Use a custom debugger executable
export EASY_DOTNET_DEBUGGER_ENGINE="custom"
export EASY_DOTNET_DEBUGGER_BIN_PATH="/path/to/my-debugger"
export EASY_DOTNET_DEBUGGER_BIN_ARGS="--interpreter=vscode"

# Override the binary path for a known engine (e.g. local netcoredbg build)
export EASY_DOTNET_DEBUGGER_BIN_PATH="/path/to/custom/netcoredbg"
```

> **Note:** `dncdbg` support is experimental. Report bugs specific to `dncdbg` in the `dncdbg` repository; report issues affecting `netcoredbg` or both projects upstream to `netcoredbg`.
