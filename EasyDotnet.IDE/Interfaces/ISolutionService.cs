using EasyDotnet.IDE.Models.Solution;

namespace EasyDotnet.IDE.Interfaces;

public interface ISolutionService
{
  Task<List<SolutionFileProject>> GetProjectsFromSolutionFile(string solutionFilePath, CancellationToken cancellationToken);
  Task<bool> AddProjectToSolutionAsync(string solutionFilePath, string projectPath, CancellationToken cancellationToken);
}