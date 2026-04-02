namespace EasyDotnet.IDE.Interfaces;

public interface IJsonCodeGenService
{
  /// <summary>
  /// Converts a sample JSON payload into a C# compilation unit by generating
  /// POCO classes from the inferred schema and wrapping them in the appropriate
  /// project namespace.
  /// </summary>
  Task<string> ConvertJsonToCSharpCompilationUnit(string jsonData, string filePath, bool preferFileScopedNamespace);
}
