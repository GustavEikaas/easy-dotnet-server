using EasyDotnet.IDE.Models.Solution;
using Microsoft.VisualStudio.SolutionPersistence.Model;

namespace EasyDotnet.IDE.Interfaces;

public interface ISolutionService
{
  Task<SolutionModel> GetSolutionModelAsync(string solutionFilePath, CancellationToken cancellationToken);
  Task<List<SolutionFileProject>> GetProjectsFromSolutionFile(string solutionFilePath, CancellationToken cancellationToken);
  Task<bool> AddProjectToSolutionAsync(string solutionFilePath, string projectPath, CancellationToken cancellationToken);
  Task<bool> RemoveProjectFromSolutionAsync(string solutionFilePath, string projectPath, CancellationToken cancellationToken);
}