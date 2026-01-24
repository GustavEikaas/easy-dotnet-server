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

## Configuration

### Custom Roslyn LSP Server

For testing purposes or workarounds, you can override the bundled Roslyn language server using environment variables:

#### Environment Variables

| Variable | Description |
|----------|-------------|
| `EASY_DOTNET_ROSLYN_DLL_PATH` | Full path to `Microsoft.CodeAnalysis.LanguageServer.dll` |

#### Examples
```bash
# Use a custom Roslyn LSP DLL (most common for testing)
export EASY_DOTNET_ROSLYN_DLL_PATH="/path/to/custom/Microsoft.CodeAnalysis.LanguageServer.dll"
```

### Custom Debugger

You can override the bundled `netcoredbg` debugger using environment variables:

#### Environment Variables

| Variable | Description |
|----------|-------------|
| `EASY_DOTNET_DEBUGGER_BIN_PATH` | Full path to a custom debugger executable (e.g., `netcoredbg`, `vsdbg`) |

#### Examples
```bash
# Use a custom debugger executable (most common for testing)
export EASY_DOTNET_DEBUGGER_BIN_PATH="/path/to/custom/netcoredbg"
```

> **Note:** These overrides are primarily intended for development, testing, and workarounds. The bundled versions are recommended for normal use.
