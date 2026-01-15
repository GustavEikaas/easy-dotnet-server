using EasyDotnet.RoslynLanguageServices.CodeActions;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace EasyDotnet.RoslynLanguageServices.Tests;

public class ImportAllNamespacesCodeFixProviderTests
{
  [Test]
  public async Task ShouldAddMissingNamespaces_ForBuiltInTypes()
  {
    var context = new CSharpCodeFixTest<EmptyDiagnosticAnalyzer, ImportAllNamespacesCodeFixProvider, DefaultVerifier>();
    context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;

    context.TestCode = """
            namespace MyApp;

            public class Program
            {
                public static void Main(string[] args)
                {
                    var list = new {|CS0246:List<string>|}();
                    {|CS0103:Console|}.WriteLine("Hello!");
                    var json = {|CS0103:JsonSerializer|}.Serialize(list);
                    var another = new {|CS0246:ConcurrentDictionary<string, string>|}();
                    var codeFixer = new {|CS0246:EasyDotnetCodeFixer|}();
                }
            }
            """;

    context.FixedCode = """
            using System;
            using System.Collections.Concurrent;
            using System.Collections.Generic;
            using System.Text.Json;

            namespace MyApp;

            public class Program
            {
                public static void Main(string[] args)
                {
                    var list = new List<string>();
                    Console.WriteLine("Hello!");
                    var json = JsonSerializer.Serialize(list);
                    var another = new ConcurrentDictionary<string, string>();
                    var codeFixer = new {|#0:EasyDotnetCodeFixer|}();
                }
            }
            """;

    context.FixedState
        .ExpectedDiagnostics
        .Add(new DiagnosticResult("CS0246", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
        .WithLocation(0)
        .WithArguments("EasyDotnetCodeFixer"));

    await context.RunAsync();
  }

  [Test]
  public async Task ShouldAddMissingNamespaces_ForCustomTypes()
  {
    var context = new CSharpCodeFixTest<EmptyDiagnosticAnalyzer, ImportAllNamespacesCodeFixProvider, DefaultVerifier>();
    context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
    var additionalSources = new string[]
    {
            """
            namespace EasyDotnet;

            public class EasyDotnetCodeFixer {}
            """,
            """
            namespace EasyDotnet.Utilities;

            public class Helper {}
            """
    };
    foreach (var source in additionalSources)
    {
      context.TestState.Sources.Add(source);
      context.FixedState.Sources.Add(source);
    }

    context.TestCode = """
            namespace MyApp;

            public class Program
            {
                public static void Main(string[] args)
                {
                    var codeFixer = new {|CS0246:EasyDotnetCodeFixer|}();
                    var helper = new {|CS0246:Helper|}();
                }
            }
            """;

    context.FixedCode = """
            using EasyDotnet;
            using EasyDotnet.Utilities;

            namespace MyApp;

            public class Program
            {
                public static void Main(string[] args)
                {
                    var codeFixer = new EasyDotnetCodeFixer();
                    var helper = new Helper();
                }
            }
            """;

    await context.RunAsync();
  }

  [Test]
  public async Task ShouldNotApplyAnyFix_IfThereIsOnlyOneMissingType()
  {
    var context = new CSharpCodeFixTest<EmptyDiagnosticAnalyzer, ImportAllNamespacesCodeFixProvider, DefaultVerifier>();
    context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
    context.TestCode = """
            namespace MyApp;

            public class Program
            {
                public static void Main(string[] args)
                {
                    {|CS0103:Console|}.WriteLine("No changes should be made.");
                }
            }
            """;

    context.FixedCode = """
            namespace MyApp;

            public class Program
            {
                public static void Main(string[] args)
                {
                    {|CS0103:Console|}.WriteLine("No changes should be made.");
                }
            }
            """;

    await context.RunAsync();
  }

  [Test]
  public async Task ShouldNotImportNamespaces_ForAmbigiousTypes()
  {
    var context = new CSharpCodeFixTest<EmptyDiagnosticAnalyzer, ImportAllNamespacesCodeFixProvider, DefaultVerifier>();
    context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
    var additionalSources = new string[]
    {
            """
            namespace EasyDotnet.A;

            public static class A
            {
              public static void Hello()
              {

              }
            }
            """,
            """
            namespace EasyDotnet.B;

            public static class A
            {
              public static void Hello()
              {

              }
            }
            """
    };
    foreach (var source in additionalSources)
    {
      context.TestState.Sources.Add(source);
      context.FixedState.Sources.Add(source);
    }

    context.TestCode = """
            namespace MyApp;

            public class Program
            {
                public static void Main(string[] args)
                {
                    {|CS0103:Console|}.WriteLine("Hello");
                    var helper = {|CS0103:A|}.Hello();
                }
            }
            """;

    context.FixedCode = """
            using System;

            namespace MyApp;

            public class Program
            {
                public static void Main(string[] args)
                {
                    Console.WriteLine("Hello");
                    var helper = {|#0:A|}.Hello();
                }
            }
            """;

    context.FixedState.ExpectedDiagnostics
    .Add(new DiagnosticResult("CS0103", Microsoft.CodeAnalysis.DiagnosticSeverity.Error).WithLocation(0).WithArguments("A"));

    await context.RunAsync();
  }
}