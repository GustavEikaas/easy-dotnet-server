using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using EasyDotnet.Controllers.Solution;
using Microsoft.Build.Construction;

namespace EasyDotnet.Services;

public class SolutionService
{
  public List<SolutionFileProjectResponse> GetProjectsFromSolutionFile(string solutionFilePath)
  {
    var fullSolutionPath = Path.GetFullPath(solutionFilePath);
    return Path.GetExtension(fullSolutionPath) == ".slnx" ? GetProjectsFromSlnx(fullSolutionPath) : GetProjectsFromSln(fullSolutionPath) ?? throw new Exception($"Failed to resolve {fullSolutionPath}");
  }

  private static List<SolutionFileProjectResponse> GetProjectsFromSln(string slnPath) => [.. SolutionFile.Parse(slnPath).ProjectsInOrder
        .Where(p => p.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
        .Select(x => x.ToResponse())];

  private static List<SolutionFileProjectResponse> GetProjectsFromSlnx(string slnxPath)
  {
    var doc = XDocument.Load(slnxPath);
    var solutionDir = Path.GetDirectoryName(slnxPath) ?? Directory.GetCurrentDirectory();

    return [.. GetProjectsRecursive(doc.Root, solutionDir)];
  }

  private static IEnumerable<SolutionFileProjectResponse> GetProjectsRecursive(XElement? element, string solutionDir) =>
      element == null
          ? []
          : element.Elements("Project")
              .Select(p => p.Attribute("Path")?.Value)
              .Where(path => !string.IsNullOrWhiteSpace(path) && path.EndsWith("proj", StringComparison.OrdinalIgnoreCase))
              .Select(relativePath =>
              {
                var absolutePath = Path.GetFullPath(Path.Combine(solutionDir, relativePath!));
                var projectName = Path.GetFileNameWithoutExtension(relativePath);
                return new SolutionFileProjectResponse(projectName ?? "", absolutePath);
              })
              .Concat(
                  element.Elements("Folder")
                      .SelectMany(f => GetProjectsRecursive(f, solutionDir))
              );
}