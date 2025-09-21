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

## TestController

### `test/discover`
| Parameter | Type | Optional |
|-----------|------|----------|
| projectPath | string |   |
| targetFrameworkMoniker | string |   |
| configuration | string |   |

**Returns:** `Task<IAsyncEnumerable<DiscoveredTest>>`

### `test/run`
| Parameter | Type | Optional |
|-----------|------|----------|
| projectPath | string |   |
| targetFrameworkMoniker | string |   |
| configuration | string |   |
| filter | RunRequestNode[] |   |

**Returns:** `Task<IAsyncEnumerable<TestRunResult>>`

---

## TemplateController

### `template/list`
_No parameters_

**Returns:** `Task<IAsyncEnumerable<DotnetNewTemplateResponse>>`

### `template/parameters`
| Parameter | Type | Optional |
|-----------|------|----------|
| identity | string |   |

**Returns:** `Task<IAsyncEnumerable<DotnetNewParameterResponse>>`

### `template/instantiate`
| Parameter | Type | Optional |
|-----------|------|----------|
| identity | string |   |
| name | string |   |
| outputPath | string |   |
| parameters | Dictionary<string, string> |   |

**Returns:** `Task`

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

### `roslyn/scope-variables`
| Parameter | Type | Optional |
|-----------|------|----------|
| sourceFilePath | string |   |
| lineNumber | int |   |

**Returns:** `Task<IAsyncEnumerable<VariableResultResponse>>`

### `roslyn/get-workspace-diagnostics`
| Parameter | Type | Optional |
|-----------|------|----------|
| targetPath | string |   |
| includeWarnings | bool | ✅  |

**Returns:** `IAsyncEnumerable<DiagnosticMessage>`

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

### `nuget/restore`
| Parameter | Type | Optional |
|-----------|------|----------|
| targetPath | string |   |

**Returns:** `Task<RestoreResult>`

### `nuget/list-sources`
_No parameters_

**Returns:** `List<NugetSourceResponse>`

### `nuget/push`
| Parameter | Type | Optional |
|-----------|------|----------|
| packagePaths | List<string> |   |
| source | string |   |
| apiKey | string | ✅  |

**Returns:** `Task<NugetPushResponse>`

### `nuget/get-package-versions`
| Parameter | Type | Optional |
|-----------|------|----------|
| packageId | string |   |
| sources | List<string> | ✅  |
| includePrerelease | bool | ✅  |

**Returns:** `Task<IAsyncEnumerable<string>>`

### `nuget/search-packages`
| Parameter | Type | Optional |
|-----------|------|----------|
| searchTerm | string |   |
| sources | List<string> | ✅  |

**Returns:** `Task<IAsyncEnumerable<NugetPackageMetadata>>`

---

## MsBuildController

### `msbuild/build`
| Parameter | Type | Optional |
|-----------|------|----------|
| request | BuildRequest |   |

**Returns:** `Task<BuildResultResponse>`

### `msbuild/project-properties`
| Parameter | Type | Optional |
|-----------|------|----------|
| request | ProjectPropertiesRequest |   |

**Returns:** `Task<DotnetProject>`

### `msbuild/list-project-reference`
| Parameter | Type | Optional |
|-----------|------|----------|
| projectPath | string |   |

**Returns:** `Task<List<string>>`

### `msbuild/add-project-reference`
| Parameter | Type | Optional |
|-----------|------|----------|
| projectPath | string |   |
| targetPath | string |   |

**Returns:** `Task<bool>`

### `msbuild/remove-project-reference`
| Parameter | Type | Optional |
|-----------|------|----------|
| projectPath | string |   |
| targetPath | string |   |

**Returns:** `Task<bool>`

---

