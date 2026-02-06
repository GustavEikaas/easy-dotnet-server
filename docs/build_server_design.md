# EasyDotnet Build Server Design Document

## 1. Introduction

This document outlines the design for the `EasyDotnet.BuildServer`, a dedicated long-running sidecar process designed to handle MSBuild-related operations and project detail resolution for the EasyDotnet IDE. It aims to decouple these processes from the main IDE application, providing a robust, scalable, and responsive environment. The server maintains a **1:1 relationship with a single IDE instance**, meaning its lifecycle is tightly coupled to the IDE's. This design prioritizes direct IDE control and simplifies state management, with the IDE responsible for re-instantiating the build server in case of crashes. The shift to a long-running process necessitates a re-evaluation of lifecycle management, concurrent request handling, and resource locking.

## 2. Server Scope

The `EasyDotnet.BuildServer` acts as an intermediary between the EasyDotnet IDE and the underlying Microsoft Build Engine (MSBuild). As a long-running process, its primary responsibilities are expanded to include:

*   **Executing MSBuild Targets**: Facilitating the execution of standard MSBuild targets such as `Build`, `Restore`, `Clean`, and `Publish` for specified .NET projects. This involves managing the `BuildManager` for potentially concurrent operations.
*   **Property Management**: Merging and applying global MSBuild properties, including `Configuration`, to target projects during build operations.
*   **Structured Output**: Capturing and structuring MSBuild's output, including errors and warnings, into a format consumable by the IDE.
*   **Error Diagnosis**: Providing enhanced error diagnosis for silent build failures or exceptions originating from the MSBuild engine.
*   **Project Detail Resolution**: Providing detailed information about .NET projects, similar to the `DotnetProject` structure (e.g., output paths, target frameworks, package references, runnable status). This involves querying MSBuild for project properties and items without necessarily triggering a full build.
*   **JSON RPC Interface**: Exposing its functionalities via a JSON RPC (Remote Procedure Call) interface, allowing the IDE to invoke operations asynchronously and receive structured responses.

The server's scope is focused on all interactions with MSBuild, from executing targets to querying project metadata. It is primarily designed to operate within the context of **a single solution file and its root directory** at any given time, reflecting the typical IDE workflow. While changing the active solution is possible, the server is not intended to manage multiple solution contexts concurrently. It aims to centralize and optimize these operations, reducing the overhead of repeated process spawning and improving the responsiveness of the IDE.

## 3. Server Lifecycle

The `EasyDotnet.BuildServer` is designed to operate as a long-running sidecar process, enhancing efficiency by minimizing startup overhead for repeated operations. Its lifecycle is tightly coupled with its associated IDE instance, ensuring direct control and simplified state management.

### 3.1. Startup

1.  **IDE Initiative**: The `EasyDotnet.IDE` initiates the server startup when MSBuild services are first required for an active solution.
2.  **Endpoint Establishment**: The server will establish a persistent communication endpoint. **Named Pipes (Windows)** will be the primary mechanism, offering a secure and efficient inter-process communication channel for local sidecar operation. For other operating systems (Linux/macOS), Unix Domain Sockets would be analogous.
3.  **RPC Listener**: A `StreamJsonRpc` server will be initialized to listen for incoming requests on the established named pipe. Given the capabilities of `StreamJsonRpc`, complex RPC messaging patterns are inherently supported.
4.  **Logging Setup**: Internal logging mechanisms (`IIdeLogger`, `InMemoryLogger`) are configured to capture and relay build events.
5.  **IDE Connection**: The `EasyDotnet.IDE` will connect to this persistent named pipe. Connection details (e.g., pipe name) will be managed through configuration or well-known conventions.

### 3.2. Operation

1.  **Continuous Request Handling**: The server continuously listens for JSON RPC requests from its single connected IDE client.
2.  **Request Processing**: Upon receiving a request (e.g., `build`, `restore`, `clean`, `publish`, `getProjectDetails`), the server processes it according to its defined RPC methods.
3.  **Concurrency Management**: To handle multiple simultaneous requests efficiently and safely, especially those involving the singleton `BuildManager`, a robust concurrency control mechanism will be implemented (detailed in Section 4). This will likely involve a request queue and/or fine-grained locking.
4.  **MSBuild Interaction**: For build-related operations, the `BuildService` interacts with `Microsoft.Build.Execution.BuildManager`. For project detail resolution, it will use `ProjectCollection` and `Project` objects from `Microsoft.Build.Evaluation` to query properties without full target execution.
5.  **Output and Response**: Results, including build outputs, structured errors/warnings, or `DotnetProject` details, are serialized and sent back to the requesting IDE client via the established RPC channel.
6.  **Idle Management**: As the server's lifecycle is tied to the IDE's, an explicit idle timeout for automatic shutdown is generally not required in the primary operation model. The IDE will manage the server's longevity.

### 3.3. Shutdown

1.  **Graceful Termination**: The server supports graceful shutdown, initiated primarily by an **explicit JSON RPC method (e.g., `server/shutdown`) invoked by the IDE** when the IDE itself is closing or no longer requires MSBuild services. Responding to OS-level termination signals (e.g., SIGTERM) will also be supported.
2.  **Resource Release**: During shutdown, the server will:
    *   Stop listening for new RPC connections.
    *   Complete any in-progress requests, or signal their cancellation.
    *   Dispose of `BuildManager` instances and other allocated resources.
    *   Close communication endpoints.
3.  **Process Exit**: The server process exits once all resources are cleanly released.
4.  **Crash Recovery**: If the server crashes unexpectedly, the IDE is designed to detect the disconnection and re-initiate a new server instance and connection when MSBuild services are next required, ensuring resilience.

## 4. Locking and Concurrency

Transitioning to a long-running server introduces significant challenges and opportunities regarding concurrency. The primary concern is the thread-safety of `Microsoft.Build.Execution.BuildManager` and `Microsoft.Build.Evaluation.ProjectCollection`, which are not designed for direct concurrent access from multiple threads for write operations (e.g., target execution). Given the **1:1 IDE-BuildServer relationship** and the focus on **a single solution context**, the concurrency model can be streamlined.

### 4.1. Core Principle: Serializing Build Operations

To ensure stability and correctness, all MSBuild *target execution* requests (e.g., `build`, `restore`, `clean`, `publish`) will be processed serially. This avoids conflicts when interacting with the singleton `BuildManager.DefaultBuildManager` and the shared `ProjectCollection`.

### 4.2. Concurrency Mechanisms

1.  **Request Queue**:
    *   **Mechanism**: A concurrent queue (e.g., `System.Collections.Concurrent.ConcurrentQueue<T>`) will be used to hold incoming build-related RPC requests.
    *   **Processing**: A dedicated worker thread will dequeue requests and execute them one by one. This serial execution for build operations simplifies `BuildManager` interaction.
    *   **Prioritization**: Future enhancements could include request prioritization (e.g., `restore` might be higher priority than a full `build`).

2.  **Fine-Grained Locking for Project Queries and Evaluation**:
    *   **Mechanism**: Read-only operations, such as resolving project details (`DotnetProject` information), and also project evaluations themselves, will interact with `ProjectCollection`.
    *   **Read-Write Lock**: A `ReaderWriterLockSlim` or similar mechanism will be employed around `ProjectCollection.GlobalProjectCollection` to manage access.
        *   **Read Lock**: Acquired for project property queries (read operations), allowing multiple concurrent readers.
        *   **Write Lock**: Acquired for any operation that modifies `ProjectCollection` or `Project` instances (e.g., loading a project, project re-evaluation, or target execution which implicitly re-evaluates parts of the project). This will block all readers and other writers.

### 4.3. `BuildManager.DefaultBuildManager` Management

*   **Singleton Access**: Access to `BuildManager.DefaultBuildManager` will be strictly controlled. All calls that initiate a build (`Build`, `Restore`, `Clean`, `Publish`) will pass through the request queue and be executed serially, acquiring the necessary write lock on the shared `ProjectCollection`.
*   **Context Isolation**: `BuildManager` operations often involve loading and evaluating projects. The serial nature of build requests, combined with the read-write lock for `ProjectCollection`, helps maintain context isolation and prevent interference between operations.

### 4.4. Implications for RPC Design

*   **Asynchronous RPC**: The JSON RPC methods exposed by the server must be asynchronous (`Task`-returning) to allow the server to remain responsive while requests are queued or processed.
*   **Request Correlation**: The IDE might need mechanisms to correlate responses with original requests, especially if requests are queued and processed out of the immediate RPC call order.
*   **Cancellation Tokens**: Incorporating `CancellationToken`s into RPC methods will allow the IDE to signal a request to be abandoned.

In summary, the long-running server prioritizes stability by serializing complex build operations. Read-only project detail queries can achieve higher concurrency through careful locking, but explicit project re-evaluation will be the primary mechanism for ensuring data freshness, in line with avoiding file watchers in the initial iteration.

## 5. Project Detail Resolution

Beyond executing build targets, the `EasyDotnet.BuildServer` will expose functionality to resolve comprehensive details about .NET projects. This information is crucial for the IDE to provide rich features like project configuration display, dependency analysis, and intelligent code assistance.

### 5.1. Resolution Mechanism

1.  **MSBuild Project Evaluation**: Project details will be resolved by programmatically loading and evaluating the `.csproj` file using `Microsoft.Build.Evaluation.ProjectCollection` and `Microsoft.Build.Evaluation.Project`. This process involves:
    *   **Loading**: Creating a `Project` instance from a given project file path.
    *   **Evaluation**: MSBuild evaluates the project file, including all imported `.props` and `.targets` files, to determine properties, items, and metadata. This evaluation is performed within the context of the shared `ProjectCollection`, protected by the read-write lock.
    *   **Property/Item Query**: Specific MSBuild properties (e.g., `OutputPath`, `TargetFramework`, `IsTestProject`) and items (e.g., `PackageReference`, `ProjectReference`) are queried from the evaluated `Project` object.

2.  **`DotnetProject` Mapping**: The raw properties and items obtained from MSBuild evaluation will be mapped to the structured `EasyDotnet.MsBuild.DotnetProject` record. This record provides a strongly-typed and consistent representation of project metadata.

3.  **Caching and Re-evaluation**: To optimize performance, resolved `DotnetProject` instances and their underlying `Microsoft.Build.Evaluation.Project` objects can be cached.
    *   **Invalidation**: **In the initial iteration, automatic cache invalidation via file system watchers will be avoided.** Instead, the IDE will explicitly request a re-evaluation if it suspects project files have changed, or if it requires absolute certainty of fresh data. This "re-evaluate if not certain" approach mirrors the behavior of spawning new `dotnet msbuild` processes for each request, ensuring data freshness when needed without the overhead of continuous file monitoring.
    *   **Concurrency**: Access to the project cache would be protected by the `ReaderWriterLockSlim` used for `ProjectCollection` access, allowing concurrent reads while ensuring exclusive access during updates or re-evaluations.

### 5.2. Exposure via JSON RPC

A new JSON RPC method (e.g., `project/getDetails`) will be introduced, taking a project file path and optional configuration parameters (e.g., target framework) as input. It will return a `DotnetProject` object.

*   **Request**: `project/getDetails(string projectFile, string? configuration, string? targetFramework)`
*   **Response**: `DotnetProject`

### 5.3. Considerations for Project Detail Resolution

*   **Performance**: Loading and evaluating projects can be resource-intensive. The caching strategy and efficient querying of MSBuild properties are critical.
*   **Target Framework Specificity**: Projects can target multiple frameworks. The RPC method should allow specifying a particular target framework for resolution, or provide details for all target frameworks.
*   **Error Handling**: Robust error handling is required for malformed project files or issues during MSBuild evaluation.

