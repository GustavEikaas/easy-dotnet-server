using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.TemplateEngine.Abstractions;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public class AddPropertyToExistingJsonFilePostActionHandler : IPostActionHandler
{
  private const string AllowFileCreationArgument = "allowFileCreation";
  private const string AllowPathCreationArgument = "allowPathCreation";
  private const string JsonFileNameArgument = "jsonFileName";
  private const string ParentPropertyPathArgument = "parentPropertyPath";
  private const string NewJsonPropertyNameArgument = "newJsonPropertyName";
  private const string NewJsonPropertyValueArgument = "newJsonPropertyValue";
  private const string DetectRepoRootForFileCreation = "detectRepositoryRootForFileCreation";

  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    WriteIndented = true
  };

  private static readonly JsonDocumentOptions DeserializerOptions = new()
  {
    AllowTrailingCommas = true,
    CommentHandling = JsonCommentHandling.Skip
  };

  public static readonly Guid Id = Guid.Parse("695A3659-EB40-4FF5-A6A6-C9C4E629FCB0");

  public Guid ActionId => Id;

  public async Task<bool> Handle(
      IPostAction postAction,
      IReadOnlyList<ICreationPath> primaryOutputs,
      string workingDirectory,
      CancellationToken cancellationToken)
  {
    try
    {
      if (!postAction.Args.TryGetValue(JsonFileNameArgument, out var jsonFileName) ||
          string.IsNullOrWhiteSpace(jsonFileName))
      {
        return false;
      }

      var jsonFiles = FindFilesInCurrentFolderOrParentFolder(workingDirectory, jsonFileName);

      if (jsonFiles.Count == 0)
      {
        if (!bool.TryParse(postAction.Args.GetValueOrDefault(AllowFileCreationArgument, "false"), out var createFile) ||
            !createFile)
        {
          return false;
        }

        _ = bool.TryParse(postAction.Args.GetValueOrDefault(DetectRepoRootForFileCreation, "false"), out var detectRepoRoot);

        var newJsonFilePath = Path.Combine(
            detectRepoRoot ? GetRootDirectory(workingDirectory) : workingDirectory,
            jsonFileName);

        await File.WriteAllTextAsync(newJsonFilePath, "{}", cancellationToken);
        jsonFiles = [newJsonFilePath];
      }

      if (jsonFiles.Count > 1)
      {
        return false;
      }

      postAction.Args.TryGetValue(ParentPropertyPathArgument, out var parentProperty);

      if (!postAction.Args.TryGetValue(NewJsonPropertyNameArgument, out var newJsonPropertyName) ||
          string.IsNullOrWhiteSpace(newJsonPropertyName))
      {
        return false;
      }

      if (!postAction.Args.TryGetValue(NewJsonPropertyValueArgument, out var newJsonPropertyValue) ||
          string.IsNullOrWhiteSpace(newJsonPropertyValue))
      {
        return false;
      }

      _ = bool.TryParse(postAction.Args.GetValueOrDefault(AllowPathCreationArgument, "false"), out var createPath);

      var newJsonContent = await AddElementToJson(
          jsonFiles[0],
          parentProperty,
          newJsonPropertyName,
          newJsonPropertyValue,
          createPath,
          cancellationToken);

      if (newJsonContent == null)
      {
        return false;
      }

      await File.WriteAllTextAsync(jsonFiles[0], newJsonContent.ToJsonString(SerializerOptions), cancellationToken);
      return true;
    }
    catch
    {
      return false;
    }
  }

  private static async Task<JsonNode?> AddElementToJson(
      string targetJsonFile,
      string? propertyPath,
      string newJsonPropertyName,
      string newJsonPropertyValue,
      bool createPath,
      CancellationToken cancellationToken)
  {
    var jsonText = await File.ReadAllTextAsync(targetJsonFile, cancellationToken);
    var jsonContent = JsonNode.Parse(jsonText, nodeOptions: null, documentOptions: DeserializerOptions);

    if (jsonContent == null)
    {
      return null;
    }

    var parentProperty = FindJsonNode(jsonContent, propertyPath, ":", createPath);

    if (parentProperty == null)
    {
      return null;
    }

    try
    {
      parentProperty[newJsonPropertyName] = JsonNode.Parse(newJsonPropertyValue);
    }
    catch (JsonException)
    {
      parentProperty[newJsonPropertyName] = newJsonPropertyValue;
    }

    return jsonContent;
  }

  private static JsonNode? FindJsonNode(JsonNode content, string? nodePath, string pathSeparator, bool createPath)
  {
    if (nodePath == null)
    {
      return content;
    }

    var properties = nodePath.Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries);
    var node = content;

    foreach (var property in properties)
    {
      if (node == null)
      {
        return null;
      }

      var childNode = node[property];
      if (childNode is null && createPath)
      {
        node[property] = childNode = new JsonObject();
      }

      node = childNode;
    }

    return node;
  }

  private static List<string> FindFilesInCurrentFolderOrParentFolder(string startPath, string matchPattern)
  {
    var directory = Directory.Exists(startPath) ? startPath : Path.GetDirectoryName(startPath);

    if (directory == null)
    {
      return [];
    }

    var numberOfUpLevels = 0;

    do
    {
      var filesInDir = Directory.EnumerateFiles(directory, matchPattern, SearchOption.AllDirectories).ToArray();

      if (filesInDir.Length > 0)
      {
        return [.. filesInDir];
      }

      directory = Path.GetPathRoot(directory) != directory ? Directory.GetParent(directory)?.FullName : null;
      numberOfUpLevels++;
    }
    while (directory != null && numberOfUpLevels <= 1);

    return [];
  }

  private static string GetRootDirectory(string outputBasePath)
  {
    var currentDirectory = outputBasePath;
    string? directoryWithSln = null;

    while (currentDirectory != null)
    {
      if (File.Exists(Path.Combine(currentDirectory, "global.json")) ||
          File.Exists(Path.Combine(currentDirectory, ".git")) ||
          Directory.Exists(Path.Combine(currentDirectory, ".git")))
      {
        return currentDirectory;
      }

      if (Directory.Exists(currentDirectory) &&
          (Directory.EnumerateFiles(currentDirectory, "*.sln", SearchOption.TopDirectoryOnly).Any() ||
           Directory.EnumerateFiles(currentDirectory, "*.slnx", SearchOption.TopDirectoryOnly).Any()))
      {
        directoryWithSln = currentDirectory;
      }

      currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
    }

    return directoryWithSln ?? outputBasePath;
  }
}

// # Add a property to an existing JSON file
//
// Adds a new JSON property in an existing JSON file.
//
// - **Action ID** : 695A3659-EB40-4FF5-A6A6-C9C4E629FCB0
// - **Specific Configuration** :
//    - `args`:
//       - `jsonFileName (string)`: The path to the JSON file that must be modified.
//       - `parentPropertyPath (string)` (optional): Specifies an existing property in the JSON file for which the new property must be a child property. The complete path must be specified, using a colon (:) as a separator character, for instance, `Person:Address`. If parentPropertyPath is not defined, the new property will be added to the root of the JSON document.
//    - `newJsonPropertyName (string)`: The name that must be given to the new property.
//    - `newJsonPropertyValue (string)`: The value that must be assigned to the new property. This must be a valid JSON.
//    - Starting in .NET 10 SDK, the following are also supported:
//        - `detectRepositoryRoot (bool)`: Whether or not a detection logic to find repo root is enabled. When the JSON file is not found, it will be created in the detected repo root if this option is true. Also when searching for an existing JSON file, the search will stop at the repo root and won't consider any parent directories. The default is `false`.
//        - `includeAllDirectoriesInSearch (bool)`: Whether or not sub-directories are searched for a matching json file (i.e, use `SearchOption.AllDirectories`). The default is `true`.
//        - `includeAllParentDirectories (bool)`: Whether or not all parent directories (up to repo root, if `detectRepositoryRoot` is true) are searched for a matching JSON file. When `false`, only one level up is searched. The default is `false`.
// - **Supported in** :
//    - dotnet new3
//    - dotnet new (2.0.0 or higher)
//
// ## Example
// ```
// "postActions": [{  `
//   `"description": "Adds a new JSON property in an existing JSON file.",`
//   `"manualInstructions": [ { "text": "Add a new property 'LogLevel' with value 'Information' to '.\deployment.json' under existing property 'moduleConfiguration:edgeAgent:properties.desired'" }  ],`
//   `"actionId": "695A3659-EB40-4FF5-A6A6-C9C4E629FCB0",`
//   `"args": {`
//     `"jsonFileName": ".\deployment.json",`
//     `"parentPropertyPath": "moduleConfiguration:edgeAgent:properties.desired",`
//     `"newJsonPropertyName": "LogLevel",`
//     `"newJsonPropertyValue": "Information"`
//   `},`
//   `"continueOnError": true`
// `}]
// ```