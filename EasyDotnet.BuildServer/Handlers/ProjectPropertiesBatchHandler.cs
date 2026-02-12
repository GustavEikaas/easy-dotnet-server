using System.Runtime.CompilerServices;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.BuildServer.MsBuildProject;
using Microsoft.Build.Evaluation;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

public class ProjectPropertiesBatchHandler
{
  [JsonRpcMethod("project/get-properties-batch", UseSingleObjectParameterDeserialization = true)]
  public async IAsyncEnumerable<ProjectEvaluationResult> GetProjectPropertiesBatch(
      GetProjectPropertiesBatchRequest request,
      [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    ValidateRequest(request);

    using var projectCollection = CreateProjectCollection(request);

    foreach (var projectPath in request.ProjectPaths)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var projectResults = EvaluateProject(
          projectCollection,
          projectPath,
          request,
          cancellationToken);

      foreach (var result in projectResults)
      {
        yield return result;
      }
    }
  }

  private IReadOnlyList<ProjectEvaluationResult> EvaluateProject(
      ProjectCollection projectCollection,
      string projectPath,
      GetProjectPropertiesBatchRequest request,
      CancellationToken cancellationToken)
  {
    var results = new List<ProjectEvaluationResult>();

    cancellationToken.ThrowIfCancellationRequested();

    if (!File.Exists(projectPath))
    {
      results.Add(new ProjectEvaluationResult(
          projectPath,
          request.Configuration,
          null,
          false,
          null,
          new ProjectEvaluationError($"Project file not found: {projectPath}", null, null)));

      return results;
    }

    Project? baseProject = null;

    try
    {
      baseProject = projectCollection.LoadProject(projectPath);

      foreach (var tfm in DetermineTargetFrameworks(baseProject))
      {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
          var tfmResult = EvaluateForTargetFramework(
              projectCollection,
              projectPath,
              request,
              tfm);

          results.Add(tfmResult);
        }
        catch (Exception ex)
        {

          results.Add(new ProjectEvaluationResult(
              projectPath,
              request.Configuration,
              tfm,
              false,
              null,
              new ProjectEvaluationError(ex.Message, ex.StackTrace, null)));
        }
      }
    }
    catch (Exception ex)
    {

      results.Add(new ProjectEvaluationResult(
          projectPath,
          request.Configuration,
          null,
          false,
          null,
          new ProjectEvaluationError(ex.Message, ex.StackTrace, null)));
    }
    finally
    {
      if (baseProject != null)
      {
        projectCollection.UnloadProject(baseProject);
      }
    }

    return results;
  }


  private ProjectEvaluationResult EvaluateForTargetFramework(
      ProjectCollection projectCollection,
      string projectPath,
      GetProjectPropertiesBatchRequest request,
      string? targetFramework)
  {
    var globalProps = new Dictionary<string, string>(
        projectCollection.GlobalProperties,
        StringComparer.OrdinalIgnoreCase);

    if (!string.IsNullOrWhiteSpace(targetFramework))
    {
      globalProps["TargetFramework"] = targetFramework!;
    }

    var project = projectCollection.LoadProject(projectPath, globalProps, toolsVersion: null);

    try
    {


      var propertyDictionary = ExtractProperties(project);
      var bag = new MsBuildPropertyBag(propertyDictionary);
      var dotnetProject = DotnetProjectDeserializer.FromBag(bag);

      return new ProjectEvaluationResult(projectPath, request.Configuration, targetFramework, true, dotnetProject, null);
    }
    finally
    {
      projectCollection.UnloadProject(project);
    }
  }

  private static IReadOnlyList<string> DetermineTargetFrameworks(Project project)
  {
    var singleTfm = project.GetPropertyValue(MsBuildProperties.TargetFramework.Name);
    var multiTfms = project.GetPropertyValue(MsBuildProperties.TargetFrameworks.Name);

    if (!string.IsNullOrWhiteSpace(multiTfms))
    {
      return [.. multiTfms
             .Split([';'], StringSplitOptions.RemoveEmptyEntries)
             .Select(tfm => tfm.Trim())];
    }

    if (!string.IsNullOrWhiteSpace(singleTfm))
    {
      return [singleTfm];
    }

    throw new Exception($"{project.FullPath} has no {MsBuildProperties.TargetFramework.Name} or {MsBuildProperties.TargetFrameworks.Name} property set");
  }

  private static ProjectCollection CreateProjectCollection(GetProjectPropertiesBatchRequest request)
  {
    var globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["Configuration"] = request.Configuration ?? "Debug",
      ["DesignTimeBuild"] = "true",
      ["BuildProjectReferences"] = "false",
      ["SkipCompilerExecution"] = "true",
      ["ProvideCommandLineArgs"] = "false",
      ["ResolveAssemblyReferencesDesignTime"] = "true",
      ["GeneratePackageOnBuild"] = "false"
    };

    return new ProjectCollection(
        globalProperties,
        loggers: null,
        toolsetDefinitionLocations: ToolsetDefinitionLocations.Default);
  }

  private static void ValidateRequest(GetProjectPropertiesBatchRequest request)
  {
    if (request.ProjectPaths == null || request.ProjectPaths.Length == 0)
    {
      throw new ArgumentException("At least one project path required");
    }

    if (request.ProjectPaths.Any(string.IsNullOrWhiteSpace))
    {
      throw new ArgumentException("Project paths cannot be null or empty");
    }

    if (request.ProjectPaths.Any(x => !Path.IsPathRooted(x)))
    {
      throw new ArgumentException("All project paths must be absolute");
    }

    if (request.ProjectPaths.Distinct().Count() != request.ProjectPaths.Length)
    {
      throw new ArgumentException("All project paths must be unique");
    }
  }

  private static IReadOnlyDictionary<string, string?> ExtractProperties(Project project)
  {
    var propertyNames = MsBuildProperties.GetAllPropertyNames();
    var dictionary = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    foreach (var propertyName in propertyNames)
    {
      var value = project.GetPropertyValue(propertyName);
      dictionary[propertyName] = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    return dictionary;
  }

}