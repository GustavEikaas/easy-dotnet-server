using EasyDotnet.BuildServer.Contracts;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

public class SolutionHandler
{
  private static readonly string[] ProjectFileTypes = [".csproj", ".fsproj"];

  [JsonRpcMethod("solution/get-projects", UseSingleObjectParameterDeserialization = true)]
  public async Task<GetSolutionProjectsResponse> GetSolutionFileProjects(GetSolutionProjectsRequest request, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(request.SolutionPath))
    {
      throw new ArgumentException("Solution path cannot be empty.", nameof(request));
    }

    var fullPath = request.SolutionPath;

    if (!Path.IsPathRooted(fullPath))
    {
      throw new InvalidOperationException("Solution file path must be a rooted path");
    }

    if (!File.Exists(fullPath))
    {
      throw new FileNotFoundException($"The solution file was not found at: {fullPath}", fullPath);
    }

    var serializer = SolutionSerializers.GetSerializerByMoniker(fullPath) ?? throw new NotSupportedException($"No serializer found for solution file: {fullPath}");
    var model = await serializer.OpenAsync(fullPath, cancellationToken);

    var solutionDir = Path.GetDirectoryName(fullPath) ?? throw new DirectoryNotFoundException("Could not determine solution directory.");

    var projects = model.SolutionProjects
        .Where(p => ProjectFileTypes.Contains(p.Extension))
        .Select(p =>
        {
          var absolutePath = Path.GetFullPath(Path.Combine(solutionDir, p.FilePath));

          return new SolutionProjectItem(
                  ProjectName: p.ActualDisplayName,
                  AbsolutePath: absolutePath,
                  ProjectGuid: p.Id.ToString()
              );
        })
        .ToList();

    return new GetSolutionProjectsResponse(projects);
  }
}
