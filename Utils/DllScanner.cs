using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

Console.WriteLine("hello");

// Ensure we have at least the DLL path
if (args.Length < 1)
{
  Console.WriteLine("Usage: DllScanner <path-to-dll> [optional-search-term]");
  Console.WriteLine("Example: DllScanner ./MyProject.dll Microsoft.CodeAnalysis");
  return;
}

string dllPath = args[0];
string? searchTarget = args.Length > 1 ? args[1] : null;

if (!File.Exists(dllPath))
{
  Console.ForegroundColor = ConsoleColor.Red;
  Console.WriteLine($"Error: File not found at '{dllPath}'");
  Console.ResetColor();
  return;
}

try
{
  // Open the DLL as a safe, read-only file stream
  using var stream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
  using var peReader = new PEReader(stream);

  if (!peReader.HasMetadata)
  {
    Console.WriteLine("This file does not contain .NET metadata (it might be a native C++ DLL).");
    return;
  }

  MetadataReader mdReader = peReader.GetMetadataReader();

  // 1. Output the DLL's OWN identity
  var assemblyDefinition = mdReader.GetAssemblyDefinition();
  string assemblyName = mdReader.GetString(assemblyDefinition.Name);
  Version assemblyVersion = assemblyDefinition.Version;

  Console.ForegroundColor = ConsoleColor.Cyan;
  Console.WriteLine($"=== Analyzing: {assemblyName} (Version: {assemblyVersion}) ===");
  Console.ResetColor();

  // 2. Scan its references
  Console.WriteLine("\n--- Dependencies ---");
  bool foundMatch = false;

  foreach (var refHandle in mdReader.AssemblyReferences)
  {
    var reference = mdReader.GetAssemblyReference(refHandle);
    string refName = mdReader.GetString(reference.Name);
    Version refVersion = reference.Version;

    // If we provided a search term, filter by it. Otherwise, print everything.
    if (searchTarget == null || refName.Contains(searchTarget, StringComparison.OrdinalIgnoreCase))
    {
      Console.WriteLine($"- {refName}, Version={refVersion}");
      foundMatch = true;
    }
  }

  // 3. Handle search results
  if (searchTarget != null)
  {
    Console.WriteLine("--------------------");
    if (foundMatch)
    {
      Console.ForegroundColor = ConsoleColor.Green;
      Console.WriteLine($"[✓] Found references matching '{searchTarget}'.");
    }
    else
    {
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine($"[X] No references found matching '{searchTarget}'.");
    }
    Console.ResetColor();
  }
}
catch (Exception ex)
{
  Console.ForegroundColor = ConsoleColor.Red;
  Console.WriteLine($"Failed to read DLL: {ex.Message}");
  Console.ResetColor();
}