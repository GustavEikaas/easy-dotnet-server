using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Solution;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace EasyDotnet.Infrastructure.Services;

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
        })];
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
}