using EasyDotnet.Domain.Models.Solution;

namespace EasyDotnet.Application.Interfaces;

public interface ISolutionService
{
  Task<List<SolutionFileProject>> GetProjectsFromSolutionFile(string solutionFilePath, CancellationToken cancellationToken);
  Task<bool> AddProjectToSolutionAsync(string solutionFilePath, string projectPath, CancellationToken cancellationToken);
}