using System;
using System.IO;
using System.Text.RegularExpressions;

namespace EasyDotnet.IDE.Utils;

public static partial class PipeUtils
{
  private const int MaxPipeNameLength = 104;
  public static string GeneratePipeName()
  {
    var pipePrefix = "CoreFxPipe_";
    var pipeName = "EasyDotnet_" + Base64SanitizerRegex().Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "");
    var maxNameLength = MaxPipeNameLength - Path.GetTempPath().Length - pipePrefix.Length - 1;
    return pipeName[..Math.Min(pipeName.Length, maxNameLength)];
  }

  [GeneratedRegex("[/+=]")]
  private static partial Regex Base64SanitizerRegex();
}