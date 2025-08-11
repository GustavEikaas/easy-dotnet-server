using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using EasyDotnet.Utils;
using StreamJsonRpc;

namespace EasyDotnet;

public static class RpcDocGenerator
{
  public static string GenerateMarkdownDoc()
  {
    var docs = GenerateDocStructure();

    var sb = new StringBuilder();

    foreach (var controller in docs!)
    {
      sb.AppendLine($"## {controller.ClassName}\n");

      foreach (var method in controller.Methods)
      {
        sb.AppendLine($"### `{method.RpcPath}`");
        if (method.Parameters.Count != 0)
        {
          sb.AppendLine("| Parameter | Type | Optional |");
          sb.AppendLine("|-----------|------|----------|");

          method.Parameters.Where(x => x.Type != "CancellationToken").ToList().ForEach(x => sb.AppendLine($"| {x.Name} | {x.Type} | {(x.IsOptional ? "✅" : "")}  |"));

          sb.AppendLine();
        }
        else
        {
          sb.AppendLine("_No parameters_\n");
        }

        sb.AppendLine($"**Returns:** `{method.ReturnType}`\n");
      }

      sb.AppendLine("---\n");
    }

    return sb.ToString();
  }

  public static string GenerateJsonDoc() => JsonSerializer.Serialize(GenerateDocStructure(), SerializerOptions);

  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private static string GetFriendlyTypeName(Type type)
  {
    if (type.IsGenericType)
    {
      var typeDef = type.GetGenericTypeDefinition();
      var genericArgs = type.GetGenericArguments().Select(GetFriendlyTypeName);

      // Handle common wrappers
      if (typeDef == typeof(Nullable<>))
        return $"{genericArgs.First()}?";

      var baseName = typeDef.Name.Split('`')[0]; // Strip `1, `2, etc.
      return $"{baseName}<{string.Join(", ", genericArgs)}>";
    }

    return type.Name switch
    {
      "String" => "string",
      "Int32" => "int",
      "Boolean" => "bool",
      "Object" => "object",
      _ => type.Name
    };
  }

  private static List<RpcApiDoc> GenerateDocStructure() => [.. AssemblyScanner.GetControllerTypes()
        .Select(rpcType =>
        {
          var methods = rpcType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
              .Where(m => m.GetCustomAttribute<JsonRpcMethodAttribute>() is not null)
              .Select(m =>
              {
                var attr = m.GetCustomAttribute<JsonRpcMethodAttribute>();
                return new RpcMethodInfo
                {
                  Name = m.Name,
                  RpcPath = attr?.Name ?? m.Name,
                  Parameters = [.. m.GetParameters()
                          .Select(p => new RpcParameter
                          {
                            Name = p.Name ?? "",
                            Type = GetFriendlyTypeName(p.ParameterType),
                            IsOptional = p.IsOptional
                          })],
                  ReturnType = GetFriendlyTypeName(m.ReturnType)
                };
              })
              .ToList();

          return new RpcApiDoc
          {
            ClassName = rpcType.Name,
            Methods = methods
          };
        })
        .Where(doc => doc.Methods.Count > 0)];

  private class RpcApiDoc
  {
    public string ClassName { get; set; } = "";
    public List<RpcMethodInfo> Methods { get; set; } = [];
  }

  private class RpcMethodInfo
  {
    public string Name { get; set; } = "";
    public string RpcPath { get; set; } = "";
    public List<RpcParameter> Parameters { get; set; } = [];
    public string ReturnType { get; set; } = "";
  }

  private class RpcParameter
  {
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsOptional { get; set; }
  }
}