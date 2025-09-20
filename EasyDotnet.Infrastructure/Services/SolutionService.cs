using System.Xml.Linq;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Solution;
using Microsoft.Build.Construction;

namespace EasyDotnet.Infrastructure.Services;


public class SolutionService : ISolutionService
{
  public List<SolutionFileProject> GetProjectsFromSolutionFile(string solutionFilePath)
  {
    var fullSolutionPath = Path.GetFullPath(solutionFilePath);
    return Path.GetExtension(fullSolutionPath) == ".slnx" ? GetProjectsFromSlnx(fullSolutionPath) : GetProjectsFromSln(fullSolutionPath) ?? throw new Exception($"Failed to resolve {fullSolutionPath}");
  }

  private static List<SolutionFileProject> GetProjectsFromSln(string slnPath) => [.. SolutionFile.Parse(slnPath).ProjectsInOrder
        .Where(p => p.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat).Select(x => new SolutionFileProject(x.ProjectName, x.AbsolutePath))];

  private static List<SolutionFileProject> GetProjectsFromSlnx(string slnxPath)
  {
    var doc = XDocument.Load(slnxPath);
    var solutionDir = Path.GetDirectoryName(slnxPath) ?? Directory.GetCurrentDirectory();

    return [.. GetProjectsRecursive(doc.Root, solutionDir)];
  }

  private static IEnumerable<SolutionFileProject> GetProjectsRecursive(XElement? element, string solutionDir) =>
      element == null
          ? []
          : element.Elements("Project")
              .Select(p => p.Attribute("Path")?.Value)
              .Where(path => !string.IsNullOrWhiteSpace(path) && path.EndsWith("proj", StringComparison.OrdinalIgnoreCase))
              .Select(relativePath =>
              {
                var absolutePath = Path.GetFullPath(Path.Combine(solutionDir, relativePath!));
                var projectName = Path.GetFileNameWithoutExtension(relativePath);
                return new SolutionFileProject(projectName ?? "", absolutePath);
              })
              .Concat(
                  element.Elements("Folder")
                      .SelectMany(f => GetProjectsRecursive(f, solutionDir))
              );
}