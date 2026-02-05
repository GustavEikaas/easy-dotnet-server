using EasyDotnet.Domain.Models.Solution;

namespace EasyDotnet.Application.Interfaces;

public interface ISolutionService
{
  List<SolutionFileProject> GetProjectsFromSolutionFile(string solutionFilePath);
  Task<bool> AddProjectToSolutionAsync(string solutionFilePath, string projectPath, CancellationToken cancellationToken);
}