## VsTestController

### `vstest/discover`
| Parameter | Type | Optional |
|-----------|------|----------|
| vsTestPath | string |   |
| dllPath | string |   |

**Returns:** `FileResultResponse`

### `vstest/run`
| Parameter | Type | Optional |
|-----------|------|----------|
| vsTestPath | string |   |
| dllPath | string |   |
| testIds | Guid[] |   |

**Returns:** `FileResultResponse`

---

## UserSecretsController

### `user-secrets/init`
| Parameter | Type | Optional |
|-----------|------|----------|
| projectPath | string |   |

**Returns:** `ProjectUserSecretsInitResponse`

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

---

## OutdatedController

### `outdated/packages`
| Parameter | Type | Optional |
|-----------|------|----------|
| targetPath | string |   |
| includeTransitive | bool? | ✅  |

**Returns:** `Task<FileResultResponse>`

---

## NugetController

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

**Returns:** `Task<List<string>>`

### `nuget/search-packages`
| Parameter | Type | Optional |
|-----------|------|----------|
| searchTerm | string |   |
| sources | List<string> | ✅  |

**Returns:** `Task<FileResultResponse>`

---

## MtpController

### `mtp/discover`
| Parameter | Type | Optional |
|-----------|------|----------|
| testExecutablePath | string |   |

**Returns:** `Task<FileResultResponse>`

### `mtp/run`
| Parameter | Type | Optional |
|-----------|------|----------|
| testExecutablePath | string |   |
| filter | RunRequestNode[] |   |

**Returns:** `Task<FileResultResponse>`

---

## MsBuildController

### `msbuild/build`
| Parameter | Type | Optional |
|-----------|------|----------|
| request | BuildRequest |   |

**Returns:** `Task<BuildResultResponse>`

---

## InitializeController

### `initialize`
| Parameter | Type | Optional |
|-----------|------|----------|
| request | InitializeRequest |   |

**Returns:** `InitializeResponse`

---

