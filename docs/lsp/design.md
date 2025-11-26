# LSP Integration (Roslyn)

*(Server ↔ Client (Neovim / easy-dotnet.nvim))*

The server bundles a **Roslyn LSP** `Microsoft.CodeAnalysis.LanguageServer.neutral` and exposes it via a subcommand:

```bash
dotnet easydotnet roslyn start
```

This launches the LSP server using **stdio communication**, making it compatible with any LSP client.

### Key Features

* **Standard Roslyn LSP support** for C# and .NET projects.
* **Roslynator support:** By passing `--roslynator`, the server automatically loads Roslynator analyzers.
* **Custom analyzers:** Additional analyzer assemblies can be loaded via `--analyzer <PATH>`.
* **Optional tooling integrations:** DevKit, Razor source generators, and design-time paths can be specified.

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

* The server ships with a specific version of Roslyn and its analyzers.
* Users are expected to **periodically update the server**, which ensures they automatically get the latest LSP improvements and analyzer updates.
* This approach simplifies client management since the client does not need to manage individual LSP binaries or dependencies.

## Command-Line Options

```text
dotnet easydotnet roslyn start [OPTIONS]

Options:
  --version                Show the Roslyn version and exit
  --roslynator             Enable Roslynator analyzers (optional)
  --analyzer <PATH>        Additional analyzer assemblies to load
  --devKitDependencyPath   Full path to DevKit dependencies (optional)
  --razorSourceGenerator   Full path to Razor source generator (optional)
  --razorDesignTimePath    Full path to Razor design-time target (optional)
  --csharpDesignTimePath   Full path to C# design-time target (optional)
```

## Summary

From the **server perspective**:

* We provide a ready-to-use Roslyn LSP with optional analyzers.
* We handle all logging, workspace, and extension loading.
* Clients are expected to consume it using standard LSP protocols.
* Updating the server automatically updates the underlying LSP and analyzers, keeping tooling current across clients.

