namespace EasyDotnet.Debugger.Services;

public sealed record VariableLocation(string Path, int Line, int Column);

public interface IVariableLocationResolver
{
  IReadOnlyDictionary<string, VariableLocation> Resolve(string filePath, int line);
}