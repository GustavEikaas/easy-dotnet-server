# Project View Redesign

## Summary

Move project-view ownership to the C# server while keeping a persistent Neovim tree buffer as the UI. Model it like the TestRunner slice: controller delegates
to a service, the service owns state, a registry stores nodes, and a dispatcher sends structured updates to Lua.

Lua becomes a thin renderer/action dispatcher. It no longer parses .sln/.csproj, calls msbuild/*, restores on open, or owns project/package/reference state.

## Key Changes

- Add a server-side ProjectViewController, ProjectViewService, ProjectViewRegistry, ProjectViewDispatcher, and project-view node/action/status models.
- Use WorkspaceBuildHostManager as the MSBuild source of truth; if ProjectGraphService is still absent in this branch, create it or keep the graph logic
  inside ProjectViewService as the M2-local slice.
- Follow the TestRunner architecture:
    - registry owns stable node IDs and current tree state
    - dispatcher is the single outbound JSON-RPC notification path
    - service handles initialize/refresh/action execution
    - controller stays thin and maps RPC methods to service calls
    - operations are cancellable and avoid overlapping refresh/action mutations
- Add RPCs:
    - project-view/initialize
    - project-view/refresh
    - project-view/action
    - project-view/cancel
- Add notifications:
    - project-view/registerNode
    - project-view/removeNode
    - project-view/updateNode
    - project-view/statusUpdate
- Represent project-view actions as server-defined action IDs:
    - select project
    - select target framework
    - add/remove package
    - add/remove project reference
    - refresh
    - open project file/package URL where applicable

## Behavior

- Persistent floating tree remains the primary UI.
- Dotnet view initializes the server-owned project view.
- Default project source is DefaultStartupProject, not DefaultViewProject.
- Switching project from the view updates DefaultStartupProject only when the selected evaluated project is runnable, using the existing
  ValidatedDotnetProject.IsRunnable property.
- Switching to a non-runnable project still changes the visible project-view target, but does not replace the startup project.
- Switching TFM updates SettingsService.SetProjectTargetFramework(projectPath, tfm) for that project.
- Multi-target projects expose TFM nodes/actions directly in the view.
- Opening the view must not run restore automatically; restore/package changes are explicit actions.
- Package/project-reference mutations run server-side, invalidate/re-evaluate the affected project, and dispatch updated nodes.

## Client Changes

- Replace current project-view/init.lua flow with calls to project-view/initialize, project-view/refresh, and project-view/action.
- Replace project-view/render.lua state with server-fed nodes and statuses.
- Remove project-view imports of sln-parse, csproj-parse, default-manager, and client.msbuild.
- Lua keymaps dispatch { nodeId, actionId }; Lua does not decide what an action means.

## Test Plan

- Server tests:
    - initialize single-project and multi-project solution
    - default startup project selected when valid
    - stale default startup project falls back to picker/first valid target
    - switching runnable project updates DefaultStartupProject
    - switching non-runnable/test/library project does not update DefaultStartupProject
    - switching TFM persists project TFM
    - package and project-reference actions refresh affected nodes
- Client checks:
    - :Dotnet view renders server nodes
    - keymaps dispatch only server-provided actions
    - refresh updates the existing buffer without rebuilding domain state in Lua
    - grep confirms project view no longer uses parser modules, default manager, or client.msbuild

## Assumptions

- The redesigned view remains a persistent Neovim tree buffer, not picker-only.
- Project-view state should be server-owned in the same style as TestRunner.
- DefaultStartupProject is updated only for runnable projects as defined by ValidatedDotnetProject.IsRunnable.
- This milestone removes project-view dependencies on legacy Lua/MSBuild paths, while broader deletion of legacy parsers/controllers remains in later #399 milestones.
