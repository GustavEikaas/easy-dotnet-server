# RFC: JSON-RPC Test Runner Protocol (Updated)

## 1. Overview

This document specifies the JSON-RPC protocol between the **test-runner server** and the **client**.

* The server does **not** send tree structures.
* The server only sends **flat TestNode objects**, and the client builds the tree from `id`/`parentId`.
* Long-running operations (build, discover, run, debug) **block the JSON-RPC request** until completion.
* While an operation is in progress, the server **pushes status updates** via notifications.
* After an operation completes, the server sends a **final status** (e.g. `"success"`, `"failed"`), which persists until the next operation.
* A client can cancel an in-flight operation using standard JSON-RPC cancellation semantics.

## 2. Data Structures

### 2.1 TestNode (server → client)

The server returns the following fields:

```jsonc
{
  "id": "string",
  "displayName": "string",
  "parentId": "string | null",
  "filePath": "string | null",
  "lineNumber": "number | null",
  "type": "solution | project | namespace | test | subcase"
}
```

**Client responsibilities:**

* Maintain a global map of all known nodes.
* Build hierarchical structure using `parentId`.
* Render the tree.
* Update nodes in-place when they are re-registered.

### 2.2 Node Relationships and Rationale for the Notification Model

#### Hierarchical Node Structure

TestNodes form a **tree-shaped hierarchy**, represented only by `id` and `parentId`:

```
Solution
 └── Project
      └── Namespace
           └── TestClass
                └── TestMethod / Subcase
```

The server **never sends the tree directly** — only individual TestNode objects.
The client is responsible for materializing and maintaining this hierarchy.

#### Why this design?

* Nodes are discovered incrementally.
* Nodes may be re-registered later (invalidate/build).
* Nodes exist across frameworks or test engines.
* Tree shape can change dynamically during rediscovery.

This allows:

* Streaming discovery.
* Dynamic updates.
* State resets without server-side tree reconstruction logic.

---

### 2.3 Multi-Node Effects During Operations

#### Key insight

A **single operation on one node** (e.g., `testrunner/run`, `testrunner/invalidate`) may **affect multiple nodes** in its ancestry or its descendants.

#### Examples:

1. **Running a single test method**

   * Affects the test node itself.
   * May also require updating:

     * The parent class (aggregate pass/fail)
     * The namespace
     * The project-level status (running, idle)
     * Possibly other siblings if the test runner loads the entire test DLL

2. **Invalidating a project node**

   * Forces rebuilding a DLL.
   * Forces rediscovery of:

     * All namespaces under it
     * All test classes
     * All test methods
   * Causes the client to receive many new `registerTest` calls (updated TestNodes).

3. **Debugging or running a group of tests**

   * Involves parent nodes (project-level build or framework switch).
   * Involves children (tests executed inside a group).

### Therefore:

* **Operations cannot be modeled as acting only on a single node.**
* The server must be able to send updates for *all affected nodes*.

---

### 2.4 Why Streaming Notifications Are Required

Given the multi-node impact described above:

#### **A. The server cannot return a large hierarchical result**

Because:

* Node updates are incremental.
* New TestNodes may be discovered midway through the operation.
* Multiple nodes may update simultaneously.
* The server does not maintain a global tree.

Therefore, **streamed notifications** are the only practical way to reflect state changes.

---

### 2.5 updateStatus Applies to Any Node

During an operation on a specific node (e.g. `id: "project-A"`), the server may update:

* The node itself.
* Its parents (`solution` → `workspace`).
* Its children (all test nodes inside the project).
* Its test framework runtime nodes (if applicable).

Each affected node receives independent statuses:

* `"queued…"`
* `"building…"`
* `"discovering…"`
* `"running…"`
* `"success"`
* `"failed"`
* `"cancelled"`

This is why `updateStatus` is:

```json
{
  "jsonrpc": "2.0",
  "method": "updateStatus",
  "params": { "id": "any-node-id", "status": "…" }
}
```

Not:

```json
{ status: "running", affects: [...] }
```

The client already has the tree model and can reason about the aggregation.

---

### 2.6 Why the Request/Response Must Be Long-Lived

When the client calls:

```json
{"method": "testrunner/run", "params": { "id": "xyz" }}
```

The server needs to:

1. Queue the operation.
2. Wait for other operations on that DLL to finish.
3. Build the project if needed.
4. Rediscover tests.
5. Start the test engine.
6. Run the tests.
7. Aggregate results.
8. Emit status updates for every affected node.
9. Emit the final status.
10. **Only then** send a JSON-RPC result.

Short-lived requests cannot model this complexity.

---

### 2.7 Summary: Why the Notification Pattern Makes Sense

The design is justified because:

### ✔ Nodes form a dynamic hierarchical graph

The client reconstructs the tree, so updates must be granular.

### ✔ Operations cascade through parents/children

You cannot model dependencies with a single synchronous response.

### ✔ Discovery and invalidation emit dozens or thousands of nodes

Only streaming incremental updates reflect reality.

### ✔ The server must stay stateless and functional

No mutable server-side tree simplifies debugging and resets.

### ✔ Requests are long-lived; cancellations must interrupt them

Streaming status allows the UI to reflect progress instantly.

### ✔ Status resets (`null`) must occur only on new operations

Because final statuses are meaningful and must persist.

## 3. Discovery Phase

### 3.1 testrunner/start (client → server)

Starts initial discovery. Returns only when discovery is finished.

```json
{
  "jsonrpc": "2.0",
  "method": "testrunner/start",
  "id": 1,
  "params": {}
}
```

### 3.2 registerTest (server → client, notification)

While discovery is running, the server issues **many** of these:

```json
{
  "jsonrpc": "2.0",
  "method": "registerTest",
  "params": {
    "test": TestNode
  }
}
```

Notes:

* May be called rapidly and in any order.
* If the same `id` is sent again, the client replaces the existing node.
* The client must support incremental rendering.


## UpdateStatus (consolidate)

```
Idle
Queued
Building
Discovering
Running
Debugging
Cancelling
Cancelled
Passed
Failed
Skipped
```

```json
{
  "jsonrpc": "2.0",
  "method": "updateStatus",
  "params": {
    "id": "string",
    "status": "Idle | Queued | Building | Discovering | Running | Debugging | Cancelling | Cancelled | Passed | Failed | Skipped | null"
  }
}
```

## 4.3 State Glossary (consolidate)
### Idle
Represents a node with no pending operation and no active status.
This is the default internal state after status: null.

Use cases:

- After discovery
- After finishing an operation (logical idle, but status persists until next op resets it)
- When the client first registers a node

### Queued
The server has accepted an operation such as:
- `testrunner/run`
- `testrunner/invalidate`
- `testrunner/debug` 

but the operation is waiting for:
- another operation(s) on the DLL to finish

### Building

The project containing this node is being built.

Triggered by:

- `invalidate`
- `run`
- `debug`

### Discovering

The test runner is enumerating tests.

Triggered by:

- initial discovery (testrunner/start)
- `invalidate`

Often affects many nodes at once:

- Whole project
- Every test class
- Every test method

### Running

Tests within that node are executing.

Applies to:

- individual test nodes
- namespace/class nodes (aggregated activity)
- project nodes (entire project test run)
- parent nodes representing summarized activity

### Debugging

A test is executing under debug instrumentation.

Applies when:

- starting a debug session
- attaching to VSTest or MTP debug processes

### Cancelling

A cancellation request was received, and the server is working on shutting down:

- test process
- discovery operation
- build pipeline

### Cancelled

The operation was successfully cancelled.

Terminal state.

### Passed

Final state meaning:

- Test execution succeeded
- Or all children succeeded (aggregate)
- Or invalidation/build/discovery process completed without error

### Failed

Final state meaning:

- One or more tests failed
- Build failed
- Discovery failed
- Debug failed

### Infrastructure failure (VSTest/MTP crash)

The client can optionally request more details using a separate endpoint.

### Skipped

Final state meaning:

- Test was ignored
- Test filtered out by engine


## 4. Running Operations

### 4.1 General Behavior

All long-running operations follow this pattern:

1. **Client sends a JSON-RPC request** (e.g., `testrunner/run`).
2. **Server begins the operation but does NOT respond immediately.**
3. While the request is in-flight, the server sends **updateStatus** notifications for that node.
4. When the operation completes:

   * The server sends **one final status** (`"success"`, `"failed"`, `"cancelled"`, etc.).
   * **Then the server sends the JSON-RPC response for the original request.**
5. To return a node to normal state, the server sends `status: null` only when a *new* operation begins.

### 4.2 updateStatus (server → client)

Used during any operation.

```json
{
  "jsonrpc": "2.0",
  "method": "updateStatus",
  "params": {
    "id": "string",
    "status": "building…" | "discovering…" | "running…" | "debugging…" | "success" | "failed" | "cancelled" | null
  }
}
```

Rules:

* **During** an operation: status changes frequently (`"building..."`, `"running..."`, etc.).
* **After completion:** server sends the final status (`"success"` or `"failed"`).
* The server does **not** send `status: null` after completion.
* `status: null` is only sent **when a new operation starts**, indicating reset to idle state.


## 5. Operations

### 5.1 Run Tests

#### Request (client → server)

```json
{
  "jsonrpc": "2.0",
  "method": "testrunner/run",
  "id": 2,
  "params": { "id": "test-node-id" }
}
```

#### While running

Server sends:

```jsonc
updateStatus { id, status: "building..." }
updateStatus { id, status: "running..." }
```

#### After completion

Server sends:

```jsonc
updateStatus { id, status: "success" }
```

Then:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": { "success": true }
}
```

Final state persists until next operation.

### 5.2 Invalidate / Rebuild / Rediscover

Same pattern:

```json
{
  "jsonrpc": "2.0",
  "method": "testrunner/invalidate",
  "id": 3,
  "params": { "id": "node-id" }
}
```

Possible status sequence:

```
"building…"
"discovering…"
"success"
```

### 5.3 Debug

Same structure, with debug-specific statuses:

```
"building…"
"running…"
"debugging…"
"success"
```

## 6. Cancellation

Because operations are long-running and the server halts the response, the client may send a **standard JSON-RPC cancellation request** referencing the original request ID.

Server behavior on cancel:

1. Stop the operation if possible.
2. Send:

```json
updateStatus { id, status: "cancelling" }
```
3. 

```json
updateStatus { id, status: "cancelled" }
```

4. Respond to the original request with:

```json
{
  "jsonrpc": "2.0",
  "id": <original-id>,
  "error": {
    "code": -32800,
    "message": "Request cancelled"
  }
}
```

4. The node stays in `"cancelled"` until a new operation resets it with `status: null`.

## 7. Summary of Responsibilities

### Server

* Emits TestNodes using `registerTest`.
* Stores no tree; clients reconstruct it.
* Blocks requests until operation finishes.
* Streams status via `updateStatus`.
* Sends final status but **does not** reset to null after completion.
* Resets with `status: null` only at the start of a new operation.
* Supports JSON-RPC cancellation.

### Client

* Builds the entire tree from `id`/`parentId`.
* Tracks “pending request ID” for cancellable operations.
* Updates UI based on `updateStatus`.
* Renders final statuses until next operation.

## **8. Duplicate Requests**

It is possible for the client to issue a new long-running request (run, debug, invalidate) **on a node that already has an in-flight operation**.
To prevent representing invalid states (e.g., `"running"` and `"queued"` simultaneously), the protocol defines strict and deterministic rules for **duplicate requests**.

These rules follow functional-programming principles:

* **No mutable shared state**
* **No overlapping operations on the same node**
* **Operations form a linear, deterministic sequence**

---

## **8.1 Principle: Only One Operation Per Node at a Time**

At any point in time:

> **A node may have at most one in-flight operation.**
> A second request for the same node is **never allowed to implicitly queue or merge**.

If a duplicate request occurs, the server must choose between:

### **A. Immediate rejection** (default, simplest)

or

### **B. Automatic cancellation of the previous request** (optional extension)

Both models ensure the node state remains valid.

---

## **8.2 Default Behavior: Immediate Rejection**

If the client sends an operation request (run/debug/invalidate) for a node that **already has an active request**, the server responds **immediately** with a JSON-RPC error:

```json
{
  "jsonrpc": "2.0",
  "id": <new-request-id>,
  "error": {
    "code": -32001,
    "message": "Operation already in progress for this node"
  }
}
```

### **Why this is correct**

* No ambiguous states
* No overlapping “running” statuses
* No accidental double execution
* Fully consistent with JSON-RPC semantics
* Client retains full control over retrying / cancellation

This is the cleanest, most FP-compliant behavior.

---

## **8.3 Optional (Opt-In): Auto-Cancel the Previous Request**

Some UIs prefer a behavior where **the newest request always wins**.

If enabled by the server, the rule becomes:

### Sequence:

1. **Client sends Request A** → server starts running it
2. **Client sends Request B for the same node**
3. Server **cancels Request A**, emitting:

```json
updateStatus { id: "<node>", status: "cancelled" }
```

4. Server responds to Request A with:

```json
{
  "jsonrpc": "2.0",
  "id": <A>,
  "error": {
    "code": -32800,
    "message": "Request cancelled"
  }
}
```

5. Then server begins executing Request B normally.

### Why this is safe

* At no moment is the node in two states.
* State transitions follow the deterministic order:

```
running… → cancelled → null → building… → running… → …
```

* The “reset to null at start of new operation” rule ensures clean state.

### But: It complicates server logic

Therefore, **this mode should be opt-in**, not default.

---

## **8.4 Duplicate Requests for Parent or Child Nodes**

Important rule:

> A request is only considered a duplicate if it targets the **same node ID**.

Example:

```
run(namespace1) 
run(test-method-1)
```

These are **not** duplicates, even if they affect overlapping nodes.
The server may or may not serialize them internally, depending on project characteristics.

The duplicate detection is strictly:

```
params.id === params.id
```

---

## **8.5 Duplicate Requests That Occur While the Node Is “Idle”**

If the node’s final status is `"success"` or `"failed"` but there is **no in-flight request**, a new request is always accepted.

Valid sequence:

```
success
client sends run
 → reset to null
 → building…
 → running…
 → success
```

---

## **8.6 Summary of Duplicate Request States**

| Situation                                                                    | Response                   | Status Behavior                       |
| ---------------------------------------------------------------------------- | -------------------------- | ------------------------------------- |
| Request arrives for a node with **no active operation**                      | Accept                     | normal sequence                       |
| Request arrives for a node **with an active operation**                      | **Reject** (default)       | none                                  |
| Request arrives for node with active operation, in optional auto-cancel mode | Cancel previous, start new | `"cancelled"` → `null` → new statuses |
| Request arrives for a different node                                         | Always allowed             | may update multiple nodes normally    |

---

# 🔥 Updated RFC Section for Inclusion

Here's the full section ready for copy/paste into your RFC:

---

## **8. Duplicate Requests**

Testrunner can only have one client-initiated "unit of work" performed at each time

e.g discovery should lock UI.

if  a user runs a testnode it should block the UI until completed


There should be a top level testrunner status defined as 


```cs
public record TestRunnerStatus(bool IsLoading, OverallStatusEnum OverallStatus, int TotalPassed, int TotalFailed, int TotalCancelled);
```
