## What is EasyDotnet.StartupHook?

`EasyDotnet.StartupHook` is a micro-library used to enable interactive external console debugging for C# applications using `netcoredbg`.

### Description

The open-source .NET debugger (`netcoredbg`) does not natively support launching applications in an external, interactive console (it lacks the `runInTerminal` reverse-request).

When using `runInTerminal` request the client is responsible for starting your program. However, attaching to a running process creates a **race condition**: if the app starts executing before `netcoredbg` finishes loading symbols and binding breakpoints, breakpoints placed at the very beginning of the program (like line 1 of `Program.cs`) will be silently skipped.

We use the [`DOTNET_STARTUP_HOOKS`](https://github.com/dotnet/runtime/blob/main/docs/design/features/host-startup-hook.md) environment variable to securely pause the .NET runtime *before* the user's code is JIT-compiled.

When the IDE launches the target application, it forcefully injects this `StartupHook.dll`. This tiny library does two things: it connects to a local Named Pipe (via JSON-RPC) and immediately reports its PID, then it physically blocks the main thread from continuing.

### How the Flow Works

1. The IDE Server intercepts the user's `launch` request and sends a `runInTerminal` reverse request to the DAP client (Neovim).
2. Neovim natively allocates a terminal buffer (or external window) and executes the target `.NET` application directly, injecting the `DOTNET_STARTUP_HOOKS` environment variable.
3. The target app boots, loads the Hook, connects to the IDE Server via a Named Pipe, and writes its 32-bit Process ID (PID).
4. The Hook immediately freezes the application's main thread by blocking on a read stream.
5. The IDE Server receives the PID, mutates the original `launch` command into an `attach` command, and forwards it to `netcoredbg`.
6. `netcoredbg` securely attaches to the frozen process and configures all user breakpoints.
7. Upon receiving `configurationDone` it sends a 1-byte `resume` signal over the Named Pipe.
8. The Hook unblocks, the target app executes, and startup breakpoints are hit flawlessly.

### Important Architectural Notes

* **Zero Dependencies:** This project has no external NuGet packages (not even JSON-RPC). This guarantees we do not pollute the debugged application's Assembly Load Context (ALC) or cause version-conflict crashes.
* **Lowest Common Denominator:** This project is compiled against `net6.0`. Because modern .NET is forward-compatible, this allows the hook to inject seamlessly into .NET 6+ applications without throwing a `BadImageFormatException`.
