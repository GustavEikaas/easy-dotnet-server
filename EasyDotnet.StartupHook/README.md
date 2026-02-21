## What is EasyDotnet.StartupHook?

`EasyDotnet.StartupHook` is a micro-library used to enable interactive external console debugging for C# applications using `netcoredbg`.

### Description

The open-source .NET debugger (`netcoredbg`) does not natively support launching applications in an external, interactive console (it lacks the `runInTerminal` reverse-request).

To get around this, the IDE must manually spawn the target application inside an external console wrapper and then explicitly `attach` the debugger to the process ID. However, attaching to a running process creates a **race condition**: if the app starts executing before `netcoredbg` finishes loading symbols and binding breakpoints, breakpoints placed at the very beginning of the program (like line 1 of `Program.cs`) will be silently skipped.

We use the [`DOTNET_STARTUP_HOOKS`](https://github.com/dotnet/runtime/blob/main/docs/design/features/host-startup-hook.md) environment variable to securely pause the .NET runtime *before* the user's code is JIT-compiled.

When the IDE launches the target application, it forcefully injects this `StartupHook.dll`. This tiny library does one thing: it connects to a local Named Pipe (via JSON-RPC) and physically blocks the main thread from continuing.

### How the Flow Works

1. The IDE spawns the `ExternalConsole` wrapper, which allocates a native terminal window.
2. The Wrapper launches the target `.NET` application with `DOTNET_STARTUP_HOOKS` pointing to this DLL.
3. The target app boots, loads the Hook, and freezes completely.
4. The IDE securely attaches `netcoredbg` to the frozen process.
5. The IDE configures all breakpoints.
6. The IDE sends a `resume` signal over the Named Pipe.
7. The Hook unblocks, the target app executes, and startup breakpoints are hit.

### Important Architectural Notes

* **Zero Dependencies:** This project has no external NuGet packages (not even JSON-RPC or UI libraries). This guarantees we do not pollute the debugged application's Assembly Load Context (ALC) or cause version-conflict crashes.
* **Lowest Common Denominator:** This project is compiled against `net6.0` (or `netcoreapp3.1`). Because modern .NET is forward-compatible, this allows the hook to inject seamlessly into .NET 6+ applications without throwing a `BadImageFormatException`.

