namespace EasyDotnet.MsBuild;

public static class MsBuildPropertyQueryBuilder
{
  public static string BuildQueryString()
  {
    var properties = MsBuildProperties.GetAllProperties().Where(p => !p.IsComputed).Select(p => p.Name);

    return string.Join(" ", properties.Select(p => $"-getProperty:{p}"));
  }
}