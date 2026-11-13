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
Pass `--debugger-engine netcoredbg|dncdbg` to check a specific bundled debugger engine.

## 🤖 MCP server for AI agents

`dotnet-easydotnet mcp` runs the same build server and test runner behind an [MCP](https://modelcontextprotocol.io) server on stdin/stdout, so coding agents can build and test without parsing `dotnet build` / `dotnet test` output. Start it from the solution directory; it picks up the single `.sln`/`.slnx` there, or pass `--solution <PATH>`.

```bash
# Claude Code, registered for the current repository
claude mcp add easy-dotnet -- dotnet-easydotnet mcp
```

| Tool | What it does |
|------|--------------|
| `build` | Builds a solution or project and returns deduplicated errors (file, line, column, code, message) as JSON. |
| `list_tests` | Lists discovered tests with id, full name, file and line. The first call builds and discovers the solution. |
| `run_tests` | Runs tests selected by id, file and line, name, project or last failure, across MTP and VSTest projects in one call, and returns each failure's message and failing line in user code. |
| `get_test_result` | Returns the full message, stack trace and stdout of a test's last run. |

The process stays alive for the agent's session, so discovered tests and the warm build server are reused between calls. Logs go to stderr; raise them with `--logLevel Verbose`.

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

easy-dotnet bundles `netcoredbg` as the default debugger. It can also bundle `dncdbg`, an experimental and faster-moving fork intended for users who want to try debugger fixes earlier.

You can select or override the debugger using environment variables:

#### Environment Variables

| Variable | Description |
|----------|-------------|
| `EASY_DOTNET_DEBUGGER_ENGINE` | Bundled debugger engine to use: `netcoredbg` or `dncdbg`. Defaults to `netcoredbg`. |
| `EASY_DOTNET_DEBUGGER_BIN_PATH` | Full path to a custom debugger executable. This overrides the bundled engine path. |

#### Examples
```bash
# Try the bundled experimental dncdbg debugger
export EASY_DOTNET_DEBUGGER_ENGINE="dncdbg"

# Use a custom debugger executable (most common for testing local builds)
export EASY_DOTNET_DEBUGGER_BIN_PATH="/path/to/custom/netcoredbg"
```

> **Note:** `dncdbg` support is experimental. Report bugs that are clearly specific to `dncdbg` in the `dncdbg` repository; report issues that affect `netcoredbg` or both projects upstream to `netcoredbg`.

> **Note:** The bundled `dncdbg` binaries are prebuilt upstream releases and require Ubuntu 24.04+ (glibc 2.39+), macOS 13.3+, or Windows 10+. They will not run on older Linux distributions with older glibc versions.
