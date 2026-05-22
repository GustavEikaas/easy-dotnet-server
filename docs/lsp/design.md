# LSP Integration (Roslyn)

*(Server ↔ Client (Neovim / easy-dotnet.nvim))*

The server uses the official **Roslyn LSP** `roslyn-language-server` global tool and exposes it via a subcommand:

```bash
dotnet easydotnet roslyn start
```

This launches the LSP server using **stdio communication**, making it compatible with any LSP client.

### Key Features

* **Standard Roslyn LSP support** for C# and .NET projects.
* **Automatic install:** If `roslyn-language-server` is missing, the server installs it with `dotnet tool install --global roslyn-language-server --prerelease`.
* **Roslynator support:** By passing `--roslynator`, the server automatically loads Roslynator analyzers.
* **Custom analyzers:** Additional analyzer assemblies can be loaded via `--analyzer <PATH>`.
* **Optional tooling integrations:** DevKit dependencies and analyzer extensions can be specified.

## Why the Server Provides LSP

* **Automatic updates:** Users only need to update the server to get the latest LSP binaries and analyzers.
* **Extensibility:** Optional extensions like Roslynator and custom analyzers can be injected at startup.
* **Cross-client compatibility:** Any editor or tool implementing LSP can consume the server over stdio or TCP (if configured).

## Typical Client Behavior

Clients (such as `easy-dotnet.nvim`) usually:

1. Spawn the server using the `dotnet easydotnet roslyn start` command.
2. Pass optional flags for Roslynator or additional analyzers.
3. Connect to the server via stdio or TCP.
4. Send standard LSP requests (e.g., `textDocument/didOpen`, `textDocument/diagnostic`) and receive responses.

> **Note:** Clients are responsible for editor integration (e.g., showing diagnostics, code actions, and refactorings). The server only provides the language intelligence and workspace management.

## Versioning & Updates

* The server tracks a recommended minimum Roslyn tool version and warns if the installed tool is older.
* Users are periodically notified when a newer Roslyn tool is available.
* Existing Roslyn tool installs are not updated automatically; clients can invoke the update command when ready.
* Health checks can read Roslyn tool state from the deterministic `dotnet-easydotnet healthcheck` item list instead of reimplementing version parsing.

## Command-Line Options

```text
dotnet easydotnet roslyn start [OPTIONS]

Options:
  --version                Show the Roslyn version and exit
  --roslynator             Enable Roslynator analyzers (optional)
  --analyzer <PATH>        Additional analyzer assemblies to load
  --devKitDependencyPath   Full path to DevKit dependencies (optional)
  --clientProcessId        Client process id Roslyn should monitor for shutdown
```

## Summary

From the **server perspective**:

* We provide a ready-to-use Roslyn LSP launcher with optional analyzers.
* We handle all logging, workspace, and extension loading.
* Clients are expected to consume it using standard LSP protocols.
* Updating the server automatically updates the underlying LSP and analyzers, keeping tooling current across clients.
