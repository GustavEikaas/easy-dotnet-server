# easy-dotnet-server — Copilot Instructions

## What This Repo Is

This is the **C# server** for the easy-dotnet Neovim plugin. It is the brain of the system — all business logic, project analysis, build orchestration, and IDE intelligence lives here. It communicates with the Neovim Lua client (`easy-dotnet.nvim`) over **JSON-RPC via stdin/stdout**, spawned as a child process.

> **If you are about to make changes that affect the JSON-RPC wire contract** (adding, removing, or changing any method name, parameter shape, or return type), you must read the client repo's instructions first:
> `$HOME/repo/easy-dotnet.nvim/.github/copilot-instructions.md`
> Both sides must be updated atomically. The protocol is the contract.

### Reference Repositories (read-only)

| Path | Purpose |
|---|---|
| `$HOME/repo/sdk` | .NET SDK source — reference for SDK/MSBuild behavior |
| `$HOME/repo/roslyn` | Roslyn source — reference for compiler/analysis APIs |
| `$HOME/repo/msbuild` | MsBuild source — reference for MSBuild APIs |
| `$HOME/repo/netcoredbg` | Debugger source — reference for debug adapter behavior |

Always consult these when implementing features that touch build, analysis, or debugging. **Behavior must match or closely approximate Visual Studio and Rider.** MSBuild is a complex beast — when in doubt, check what the SDK does.

When working with Microsoft-specific APIs and tooling (MSBuild, MTP, Roslyn, NuGet, StreamJsonRpc, etc.), **searching the internet is encouraged**. Official documentation, GitHub issues on the relevant repos, and source-browser links often contain critical behavioral details that aren't obvious from the API surface alone.

---

## Architecture Principles

### The Server Owns Complexity

> **Lua is hard to maintain. C# is not.** Every non-trivial decision, computation, and state belongs here. The client renders and collects; the server thinks.

The client has no knowledge of MSBuild, project files, solution structure, NuGet, Roslyn, or .NET tooling. The server decides what to show, when to show it, and what data to ask for.

### Feature Slices

Features are organized as **vertical slices**, not DDD layers. `EasyDotnet.IDE/TestRunner/` is the canonical example of a well-designed feature — read it before starting any new feature. There are older remnants of DDD structure in the codebase; do not follow those patterns for new work.

### Reverse Requests and User Feedback

The primary pattern for complex workflows is **server-initiated requests**. The server doesn't just respond to client calls — it also sends requests to the client mid-operation to collect input, present choices, or report progress.

This is an **editor**. User feedback during long-running operations is critical. Always wrap long-running operations in a `ProgressScope` — see `./EasyDotnet.IDE/Editor/ProgressScope.cs` for the pattern. Never leave the user staring at a silent editor.

**Canonical references — read these before implementing any new feature:**
- `EasyDotnet.IDE/TestRunner/` — complete end-to-end example of a feature slice with reverse requests and progress reporting
- `EditorService.cs` — service layer structure and how the server orchestrates client interaction

The typical flow:

```
Client calls server  →  server opens a ProgressScope, starts async work
  →  server sends reverse request to client ("pick a framework")
  →  client responds
  →  server continues, sends progress notifications via ProgressScope
  →  server sends final result or structured error, closes ProgressScope
```

### JSON-RPC Contract

- **JSON-RPC 2.0**: `method` + `params` + `id` for requests; `result`/`error` for responses; omit `id` for notifications.
- Method names follow a **REST-style path convention**: `testrunner/run`, `project/restore`, `editor/navigate`.
- Use **notifications** for progress and telemetry. Use **requests** (with `id`) only when the client must reply.
- `rpcDoc.md` is the hand-maintained wire contract. **Never modify it.** Read it to understand existing signatures.

---

## Project Structure

### `EasyDotnet.IDE`

The main server process. Hosts the StreamJsonRpc endpoint, all feature slices, and owns the lifecycle of the BuildServer. Run with:

```bash
dotnet run --project EasyDotnet.IDE
```

### `EasyDotnet.BuildServer`

A **standalone executable** that the IDE project owns through `BuildHost` / `BuildHostFactory`. All MSBuild work happens exclusively in this process — never in `EasyDotnet.IDE` directly. It is generally fine to make changes here, but keep it strictly MSBuild-centric. If something isn't about evaluating or building projects via MSBuild, it does not belong in `BuildServer`.


### `EasyDotnet.StartupHook`
A .NET startup hook injected into user processes at launch. It emits the process PID and conditionally pauses execution to wait for a debugger to attach — pausing only when launched in debug mode, not for regular runs. Both run and debug go through this hook; the pause behaviour is the only difference.
Keep this project minimal and conservative:

It targets a low TFM for broad compatibility — do not raise it without a clear reason.
Avoid adding dependencies or complexity. Startup hooks run before user code and failures here are hard to diagnose.

### `EasyDotnet.AppWrapper`
A wrapper executable that sits around the user's running process to reuse external terminal windows. When a user stops and reruns their app, the same terminal window is reattached rather than a new one opened. Changes here affect run/debug UX directly — treat it carefully.

---

## Tech Stack

- **.NET 8+**, C# 12
- **[StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc)** — JSON-RPC transport over stdin/stdout
- **Microsoft NuGet packages** (e.g. `NuGet.ProjectModel`) — for NuGet/package-related features
- **`EasyDotnet.BuildServer`** — for anything requiring MSBuild APIs
- **Microsoft.CodeAnalysis** (Roslyn) — code analysis
- **xUnit** — testing

**Test:**
```bash
dotnet test ./EasyDotnet.slnx
```

---

## Code Guidelines

### RPC Handlers

- Handlers must be **thin**: validate input, call a service, return. No logic in the handler itself.
- One service class per domain, following the pattern in `EditorService.cs`. The RPC layer is just wiring.
- Every async method takes a `CancellationToken`. Long-running operations (restore, build, analysis) must be cancellable.
- Use **immutable DTOs** for everything crossing the RPC boundary. Never expose internal model objects.
- Never use `Console.WriteLine` except on the JSON-RPC transport stream itself. Use the structured logging infrastructure for diagnostics.

### Coding Style

Prefer a **functional style**:

- Use **LINQ** idiomatically — chain `Select`, `Where`, `GroupBy`, `ToDictionary`, etc. rather than imperative loops.
- For `IAsyncEnumerable<T>`, use the project's `ToListAsync()` extension instead of manual `await foreach` collection.
- Prefer expression-bodied members and `=>` lambdas for concise methods.
- Minimize mutable local state. Stay in the happy path with nullable reference types and chained transforms.
- Do not rewrite working imperative code to functional purely for style — apply the style on new code and during natural edits.

### MSBuild

All MSBuild work goes through `EasyDotnet.BuildServer` via `BuildHost`/`BuildHostFactory`. Do not reference or invoke MSBuild APIs directly from `EasyDotnet.IDE`. When in doubt about whether something belongs in BuildServer, ask: is this purely about evaluating or building project files? If not, it belongs elsewhere.

- Prefer **design-time builds** over full builds for property/item evaluation, matching VS/Rider behavior.
- `GetProjectPropertiesBatchAsync` flattens multi-targeted projects into a list across all TFMs. **Always default to `Debug` configuration** unless the operation specifically requires otherwise (e.g. `pack`).
- Consult `$HOME/repo/sdk` before implementing anything that touches target resolution, implicit usings, or SDK imports.

---

## Key Files

| File | What it teaches |
|---|---|
| `EasyDotnet.IDE/TestRunner/` | Canonical feature slice — reverse requests, progress, service structure |
| `EasyDotnet.IDE/Editor/ProgressScope.cs` | How to report progress to the user during long-running operations |
| `EditorService.cs` | Service layer structure |
| `BuildHost` / `BuildHostFactory` | How the IDE communicates with the BuildServer process |
| `rpcDoc.md` | Wire contract — **read only, never modify** |
