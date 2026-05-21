using System.Collections.Immutable;
using EasyDotnet.IDE.Interfaces;
using Microsoft.TemplateEngine.Abstractions;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public class EasyDotnetAddToSolutionPostActionHandler(ISolutionService solutionService, IClientService clientService) : IPostActionHandler
{
  public static readonly Guid Id = Guid.Parse("76F7B4BE-5570-4E66-A13F-D22A05FA5F1D");
  public static readonly string ParameterKey = "addToSolution";

  public Guid ActionId => Id;

  public static readonly IPostAction SyntheticPostAction = new SyntheticAction();

  public async Task<bool> Handle(
      IPostAction postAction,
      IReadOnlyList<ICreationPath> primaryOutputs,
      string workingDirectory,
      CancellationToken cancellationToken)
  {
    var slnFilePath = clientService?.ProjectInfo?.SolutionFile;
    if (string.IsNullOrWhiteSpace(slnFilePath))
    {
      return true;
    }

    foreach (var projectFile in FindProjectFiles(primaryOutputs, workingDirectory))
    {
      await solutionService.AddProjectToSolutionAsync(slnFilePath, projectFile, cancellationToken);
    }

    return true;
  }

  private static List<string> FindProjectFiles(IReadOnlyList<ICreationPath> primaryOutputs, string workingDirectory)
  {
    var fromOutputs = primaryOutputs
        .Select(o => Path.Combine(workingDirectory, o.Path))
        .Where(IsProjectFile)
        .Where(File.Exists)
        .ToList();

    if (fromOutputs.Count > 0)
    {
      return fromOutputs;
    }

    if (!Directory.Exists(workingDirectory))
    {
      return [];
    }

    return [.. Directory.EnumerateFiles(workingDirectory, "*.*", SearchOption.AllDirectories)
        .Where(IsProjectFile)];
  }

  private static bool IsProjectFile(string path) =>
      path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
      path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
      path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);

  private sealed record SyntheticAction : IPostAction
  {
    public string? Description => "Add project(s) to solution";
    public Guid ActionId => Id;
    public bool ContinueOnError => true;
    public IReadOnlyDictionary<string, string> Args => ImmutableDictionary<string, string>.Empty;
    public string? ManualInstructions => null;
  }
}