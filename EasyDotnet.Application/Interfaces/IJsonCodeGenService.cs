namespace EasyDotnet.Application.Interfaces;

public interface IJsonCodeGenService
{
  /// <summary>
  /// Converts a sample JSON payload into a C# compilation unit by generating
  /// POCO classes from the inferred schema and wrapping them in the appropriate
  /// project namespace.
  /// </summary>
  /// <param name="jsonData">The sample JSON input to infer the schema from.</param>
  /// <param name="filePath">
  /// The target file path where the generated class will conceptually belong.
  /// Used to determine the class name and namespace.
  /// </param>
  /// <param name="preferFileScopedNamespace">
  /// Whether the generated C# code should use file-scoped namespaces instead of block-scoped namespaces.
  /// </param>
  /// <returns>
  /// A <see cref="string"/> containing the generated C# classes with the proper
  /// namespace and formatting applied.
  /// </returns>
  /// <remarks>
  /// This method:
  /// <list type="number">
  ///   <item>Uses <see cref="NJsonSchema.JsonSchema"/> to infer a schema from the sample JSON.</item>
  ///   <item>Generates C# POCO classes using <see cref="NJsonSchema.CodeGeneration.CSharp.CSharpGenerator"/>.</item>
  ///   <item>Resolves the project’s root namespace from the associated .csproj file.</item>
  ///   <item>Appends a namespace suffix based on the file path relative to the project.</item>
  ///   <item>Extracts only the relevant class declarations into a clean, compilable C# unit.</item>
  /// </list>
  /// </remarks>
  /// <example>
  /// Example <paramref name="jsonData"/>:
  /// <code language="json">
  /// {
  ///   "id": 1,
  ///   "name": "Widget",
  ///   "price": 9.99
  /// }
  /// </code>
  /// Example <paramref name="filePath"/>:
  /// <code>
  /// C:\Projects\MyApp\Models\Product.json
  /// </code>
  /// Example output (simplified):
  /// <code language="csharp">
  /// namespace MyApp.Models
  /// {
  ///     public class Product
  ///     {
  ///         public int Id { get; set; }
  ///         public string Name { get; set; }
  ///         public double Price { get; set; }
  ///     }
  /// }
  /// </code>
  /// </example>
  Task<string> ConvertJsonToCSharpCompilationUnit(string jsonData, string filePath, bool preferFileScopedNamespace);
}