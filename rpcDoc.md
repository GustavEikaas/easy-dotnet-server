## TestController

### `test/discover`
| Parameter | Type | Optional |
|-----------|------|----------|
| projectPath | string |   |
| targetFrameworkMoniker | string | ✅  |
| configuration | string | ✅  |

**Returns:** `Task<IAsyncEnumerable<DiscoveredTest>>`

### `test/run`
| Parameter | Type | Optional |
|-----------|------|----------|
| projectPath | string |   |
| configuration | string |   |
| filter | RunRequestNode[] |   |
| targetFrameworkMoniker | string | ✅  |

**Returns:** `Task<IAsyncEnumerable<TestRunResult>>`

---

## TemplateController

### `template/instantiate`
| Parameter | Type | Optional |
|-----------|------|----------|
| identity | string |   |
| name | string |   |
| outputPath | string |   |
| parameters | Dictionary<string, string> |   |

**Returns:** `Task`

### `template/list`
_No parameters_

**Returns:** `Task<IAsyncEnumerable<DotnetNewTemplateResponse>>`

### `template/parameters`
| Parameter | Type | Optional |
|-----------|------|----------|
| identity | string |   |

**Returns:** `Task<IAsyncEnumerable<DotnetNewParameterResponse>>`

---

## SolutionController

### `solution/list-projects`
| Parameter | Type | Optional |
|-----------|------|----------|
| solutionFilePath | string |   |

**Returns:** `List<SolutionFileProjectResponse>`

---

## RoslynController

### `roslyn/bootstrap-file`
| Parameter | Type | Optional |
|-----------|------|----------|
| filePath | string |   |
| kind | Kind |   |
| preferFileScopedNamespace | bool |   |

**Returns:** `Task<BootstrapFileResultResponse>`

### `roslyn/get-workspace-diagnostics`
| Parameter | Type | Optional |
|-----------|------|----------|
| targetPath | string |   |
| includeWarnings | bool | ✅  |

**Returns:** `IAsyncEnumerable<DiagnosticMessage>`

### `roslyn/scope-variables`
| Parameter | Type | Optional |
|-----------|------|----------|
| sourceFilePath | string |   |
| lineNumber | int |   |

**Returns:** `Task<IAsyncEnumerable<VariableResultResponse>>`

---

## OutdatedController

### `outdated/packages`
| Parameter | Type | Optional |
|-----------|------|----------|
| targetPath | string |   |
| includeTransitive | bool? | ✅  |

**Returns:** `Task<IAsyncEnumerable<OutdatedDependencyInfoResponse>>`

---

## NugetController

### `nuget/get-package-versions`
| Parameter | Type | Optional |
|-----------|------|----------|
| packageId | string |   |
| sources | List<string> | ✅  |
| includePrerelease | bool | ✅  |

**Returns:** `Task<IAsyncEnumerable<string>>`

### `nuget/list-sources`
_No parameters_

**Returns:** `IAsyncEnumerable<NugetSourceResponse>`

### `nuget/push`
| Parameter | Type | Optional |
|-----------|------|----------|
| packagePaths | List<string> |   |
| source | string |   |
| apiKey | string | ✅  |

**Returns:** `Task<NugetPushResponse>`

### `nuget/restore`
| Parameter | Type | Optional |
|-----------|------|----------|
| targetPath | string |   |

**Returns:** `Task<RestoreResult>`

### `nuget/search-packages`
| Parameter | Type | Optional |
|-----------|------|----------|
| searchTerm | string |   |
| sources | List<string> | ✅  |

**Returns:** `Task<IAsyncEnumerable<NugetPackageMetadata>>`

---

## UserSecretsController

### `user-secrets/init`
| Parameter | Type | Optional |
|-----------|------|----------|
| projectPath | string |   |

**Returns:** `Task<ProjectUserSecretsInitResponse>`

---

## NetCoreDbgController

### `debugger/start`
| Parameter | Type | Optional |
|-----------|------|----------|
| request | DebuggerStartRequest |   |

**Returns:** `Task<DebuggerStartResponse>`

---

## MsBuildController

### `msbuild/add-project-reference`
| Parameter | Type | Optional |
|-----------|------|----------|
| projectPath | string |   |
| targetPath | string |   |

**Returns:** `Task<bool>`

### `msbuild/build`
| Parameter | Type | Optional |
|-----------|------|----------|
| request | BuildRequest |   |

**Returns:** `Task<BuildResultResponse>`

### `msbuild/list-project-reference`
| Parameter | Type | Optional |
|-----------|------|----------|
| projectPath | string |   |

**Returns:** `Task<List<string>>`

### `msbuild/project-properties`
| Parameter | Type | Optional |
|-----------|------|----------|
| request | ProjectPropertiesRequest |   |

**Returns:** `Task<DotnetProjectV1>`

### `msbuild/remove-project-reference`
| Parameter | Type | Optional |
|-----------|------|----------|
| projectPath | string |   |
| targetPath | string |   |

**Returns:** `Task<bool>`

---

## LaunchProfileController

### `launch-profiles`
| Parameter | Type | Optional |
|-----------|------|----------|
| targetPath | string |   |

**Returns:** `IAsyncEnumerable<LaunchProfileResponse>`

---

## JsonCodeGen

### `json-code-gen`
| Parameter | Type | Optional |
|-----------|------|----------|
| jsonData | string |   |
| filePath | string |   |
| preferFileScopedNamespace | bool |   |

**Returns:** `Task<BootstrapFileResultResponse>`

---

## InitializeController

### `initialize`
| Parameter | Type | Optional |
|-----------|------|----------|
| request | InitializeRequest |   |

**Returns:** `Task<InitializeResponse>`

---

