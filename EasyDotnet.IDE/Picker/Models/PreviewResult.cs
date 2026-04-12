namespace EasyDotnet.IDE.Picker.Models;

public abstract record PreviewResult
{
  public string Type => GetType().Name;

  public sealed record File(string Path) : PreviewResult;

  public sealed record Text(string[] Lines, string? Filetype = null) : PreviewResult;
}