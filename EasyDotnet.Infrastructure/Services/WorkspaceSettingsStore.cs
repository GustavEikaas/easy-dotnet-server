using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.IDE;

namespace EasyDotnet.Infrastructure.Services;


public sealed class WorkspaceSettingsStore(IClientService clientService) : IWorkspaceSettingsStore
{
  private const string AppFolderName = "easy-dotnet";
  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    WriteIndented = true,
    Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
  };

  public WorkspaceProjectReference? GetDefaultBuildProject() => GetOrCreate().Defaults.DefaultBuild;

  public void SetDefaultBuildProject(WorkspaceProjectReference? value)
      => Modify(d => d with { Defaults = d.Defaults with { DefaultBuild = value } });

  public WorkspaceProjectReference? GetDefaultDebugProject() => GetOrCreate().Defaults.DefaultDebug;

  public void SetDefaultDebugProject(WorkspaceProjectReference? value)
      => Modify(d => d with { Defaults = d.Defaults with { DefaultDebug = value } });

  public WorkspaceProjectReference? GetDefaultRunProject() => GetOrCreate().Defaults.DefaultRun;

  public void SetDefaultRunProject(WorkspaceProjectReference? value)
      => Modify(d => d with { Defaults = d.Defaults with { DefaultRun = value } });


  public WorkspaceProjectReference? GetDefaultTestProject() => GetOrCreate().Defaults.DefaultTest;

  public void SetDefaultTestProject(WorkspaceProjectReference? value)
      => Modify(d => d with { Defaults = d.Defaults with { DefaultTest = value } });


  public WorkspaceProjectReference? GetDefaultViewProject() => GetOrCreate().Defaults.DefaultTest;

  public void SetDefaultViewProject(WorkspaceProjectReference? value)
      => Modify(d => d with { Defaults = d.Defaults with { DefaultView = value } });

  public void DeleteCurrentWorkspace()
  {
    var path = GetSettingsFilePath();
    if (File.Exists(path))
      File.Delete(path);
  }

  public static void ResetAll()
  {
    var root = GetRootFolderPath();
    if (Directory.Exists(root))
      Directory.Delete(root, recursive: true);
  }

  private WorkspaceSettingsDocumentV1 GetOrCreate()
  {
    var path = GetSettingsFilePath();

    if (!File.Exists(path))
    {
      var created = CreateDefaultV1();
      Save(created, path);
      return created;
    }

    return Load(path);
  }

  private static WorkspaceSettingsDocumentV1 Load(string path)
  {
    var json = File.ReadAllText(path);
    using var doc = JsonDocument.Parse(json);

    var version = doc.RootElement.GetProperty("version").GetInt32();

    return version switch
    {
      1 => JsonSerializer.Deserialize<WorkspaceSettingsDocumentV1>(
              json, SerializerOptions)!
        ,
      _ => throw new NotSupportedException(
              $"Unsupported workspace settings version: {version}")
    };
  }

  private static void Save(WorkspaceSettingsDocumentV1 document, string path)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path,
        JsonSerializer.Serialize(document, SerializerOptions));
  }

  private void Modify(
      Func<WorkspaceSettingsDocumentV1, WorkspaceSettingsDocumentV1> update)
  {
    var path = GetSettingsFilePath();
    var current = GetOrCreate();
    var updated = update(current);
    Save(updated, path);
  }

  private string GetSettingsFilePath()
  {
    var solutionFile = clientService.ProjectInfo?.SolutionFile
        ?? throw new InvalidOperationException("Solution file not available.");

    var fullPath = Path.GetFullPath(solutionFile);
    var hash = ComputeMd5(fullPath);

    return Path.Combine(
        GetRootFolderPath(),
        $"workspace_{hash}.json");
  }

  private static string GetRootFolderPath()
      => Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          AppFolderName);

  private static string ComputeMd5(string input)
  {
    var bytes = Encoding.UTF8.GetBytes(input);
    var hash = MD5.HashData(bytes);

    return Convert.ToHexString(hash).ToLowerInvariant();
  }

  private static WorkspaceSettingsDocumentV1 CreateDefaultV1()
      => new(
          Version: WorkspaceSettingsDocumentV1.CurrentVersion,
          Defaults: new WorkspaceDefaultSettings(
              DefaultBuild: null,
              DefaultDebug: null,
              DefaultRun: null,
              DefaultTest: null,
              DefaultView: null
          )
      );
}