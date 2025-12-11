using EasyDotnet.MsBuild.ProjectModel;

namespace EasyDotnet.MsBuild;

public static class MsBuildPropertyQueryBuilder
{
  public static string BuildQueryString()
  {
    var properties = MsBuildProperties.GetAllPropertyNames();

    return string.Join(" ", properties.Select(p => $"-getProperty:{p}"));
  }
}