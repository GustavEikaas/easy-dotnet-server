using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Solution;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace EasyDotnet.IDE.Services;

public class SolutionService : ISolutionService
{
  public async Task<List<SolutionFileProject>> GetProjectsFromSolutionFile(string solutionFilePath, CancellationToken cancellationToken)
  {
    var fullSolutionPath = Path.GetFullPath(solutionFilePath);
    var solutionDirectory = Path.GetDirectoryName(solutionFilePath) ?? throw new Exception("Solution dir cannot be null");

    var serializer = SolutionSerializers.GetSerializerByMoniker(fullSolutionPath) ?? throw new InvalidOperationException($"No serializer found for solution file: {fullSolutionPath}");
    var solutionModel = await serializer.OpenAsync(fullSolutionPath, cancellationToken);
    var solutionFolderType = Guid.Parse("2150E333-8FDC-42A3-9474-1A3956D46DE8");

    return [.. solutionModel.SolutionProjects
        .Where(p => p.TypeId != solutionFolderType)
        .Select(p =>
        {
          var absolutePath = Path.GetFullPath(Path.Combine(solutionDirectory, p.FilePath));

          return new SolutionFileProject(
                ProjectName: p.ActualDisplayName,
                AbsolutePath: absolutePath
            );
        })
        .OnlyDotnetProjects()];
  }

  public async Task<bool> AddProjectToSolutionAsync(string solutionFilePath, string projectPath, CancellationToken cancellationToken)
  {
    var serializer = SolutionSerializers.GetSerializerByMoniker(solutionFilePath);
    if (serializer == null) return false;

    var solutionModel = await serializer.OpenAsync(solutionFilePath, cancellationToken);

    var solutionDirectory = Path.GetDirectoryName(solutionFilePath) ?? throw new Exception("Solution dir cannot be null");
    var relativePath = Path.GetRelativePath(solutionDirectory, projectPath);

    solutionModel.AddProject(relativePath);

    await serializer.SaveAsync(solutionFilePath, solutionModel, cancellationToken);

    return true;
  }

  public async Task<bool> RemoveProjectFromSolutionAsync(string solutionFilePath, string projectPath, CancellationToken cancellationToken)
  {
    var fullSolutionPath = Path.GetFullPath(solutionFilePath);
    var serializer = SolutionSerializers.GetSerializerByMoniker(fullSolutionPath);
    if (serializer == null) return false;

    var solutionModel = await serializer.OpenAsync(fullSolutionPath, cancellationToken);
    var solutionDirectory = Path.GetDirectoryName(fullSolutionPath) ?? throw new Exception("Solution dir cannot be null");

    var project = solutionModel.SolutionProjects
        .FirstOrDefault(p => string.Equals(
            Path.GetFullPath(Path.Combine(solutionDirectory, p.FilePath)),
            Path.GetFullPath(projectPath),
            StringComparison.OrdinalIgnoreCase));

    if (project == null) return false;

    solutionModel.RemoveProject(project);
    await serializer.SaveAsync(fullSolutionPath, solutionModel, cancellationToken);

    return true;
  }
}