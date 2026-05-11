namespace EasyDotnet.ContainerTests.TestRunner.Fixtures;

/// <summary>
/// Hand-written source snippets for adapter edge cases that can't be expressed
/// through <see cref="TestProjectFixtureBuilder"/>'s namespace/class/method model.
/// </summary>
public static class TestFixtures
{
  /// <summary>
  /// Two block-scoped namespaces in a single file, each with one class and one <c>[TestMethod]</c>.
  /// Covers the namespace-handling discussion at the top of issue #841.
  /// </summary>
  public const string MsTestBlockNamespaces = """
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace Mst.Block.N1
    {
        [TestClass]
        public class C1
        {
            [TestMethod]
            public void M() { }
        }
    }

    namespace Mst.Block.N2
    {
        [TestClass]
        public class C2
        {
            [TestMethod]
            public void M() { }
        }
    }
    """;

  /// <summary>
  /// One <c>[DataTestMethod]</c> with two <c>[DataRow]</c> attributes that set <c>DisplayName</c>.
  /// Covers NicolaiSattler's bug at the bottom of issue #841: DataRows with a filled
  /// <c>DisplayName</c> should surface that name on subcase nodes.
  /// </summary>
  public const string MsTestDataRowDisplayName = """
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace Mst.DataRows
    {
        [TestClass]
        public class Rows
        {
            [DataTestMethod]
            [DataRow(1, DisplayName = "first case")]
            [DataRow(2, DisplayName = "second case")]
            public void M(int x) { }
        }
    }
    """;

  /// <summary>
  /// Two block-scoped namespaces in a single file, each with one class and one TUnit
  /// <c>[Test]</c>. Mirrors <see cref="MsTestBlockNamespaces"/> for the MTP adapter path.
  /// </summary>
  public const string TUnitBlockNamespaces = """
    namespace Sample.Block.N1
    {
        public class C1
        {
            [Test]
            public async Task M() => await Task.CompletedTask;
        }
    }

    namespace Sample.Block.N2
    {
        public class C2
        {
            [Test]
            public async Task M() => await Task.CompletedTask;
        }
    }
    """;

  /// <summary>
  /// One TUnit <c>[Test]</c> with two <c>[Arguments]</c> attributes.
  /// TUnit's canonical way to write parameterised tests — each row should surface
  /// as a Subcase under a TheoryGroup named after the method.
  /// </summary>
  public const string TUnitParameterized = """
    namespace Sample.Parameterized
    {
        public class Rows
        {
            [Test]
            [Arguments(1)]
            [Arguments(2)]
            public async Task M(int x) => await Task.CompletedTask;
        }
    }
    """;

  /// <summary>
  /// Two block-scoped namespaces in a single file, each with one class and one xUnit
  /// <c>[Fact]</c>. Source is shared between xUnit v2 (VSTest) and xUnit v3 (MTP).
  /// </summary>
  public const string XUnitBlockNamespaces = """
    using Xunit;

    namespace Sample.Block.N1
    {
        public class C1
        {
            [Fact]
            public void M() { }
        }
    }

    namespace Sample.Block.N2
    {
        public class C2
        {
            [Fact]
            public void M() { }
        }
    }
    """;

  /// <summary>
  /// One xUnit <c>[Theory]</c> with two <c>[InlineData]</c> rows. Source is shared
  /// between xUnit v2 and v3. Each row should surface as a Subcase under a TheoryGroup
  /// named after the method.
  /// </summary>
  public const string XUnitInlineData = """
    using Xunit;

    namespace Sample.Parameterized
    {
        public class Rows
        {
            [Theory]
            [InlineData(1)]
            [InlineData(2)]
            public void M(int x) { _ = x; }
        }
    }
    """;

  /// <summary>
  /// Two block-scoped namespaces in a single file, each with one class and one NUnit
  /// <c>[Test]</c>. Source is shared between NUnit v3 (VSTest) and v4 (MTP).
  /// </summary>
  public const string NUnitBlockNamespaces = """
    using NUnit.Framework;

    namespace Sample.Block.N1
    {
        public class C1
        {
            [Test]
            public void M() { }
        }
    }

    namespace Sample.Block.N2
    {
        public class C2
        {
            [Test]
            public void M() { }
        }
    }
    """;

  /// <summary>
  /// One NUnit <c>[Test]</c> with two <c>[TestCase]</c> rows. Each row should surface as
  /// a Subcase under a TheoryGroup named after the method. Source shared between v3 and v4.
  /// </summary>
  public const string NUnitTestCase = """
    using NUnit.Framework;

    namespace Sample.Parameterized
    {
        public class Rows
        {
            [TestCase(1)]
            [TestCase(2)]
            public void M(int x) { _ = x; }
        }
    }
    """;

  /// <summary>
  /// Deterministic source layout used to pin TestSourceLocator line-number output.
  /// Source shared between xUnit v2 and v3 (same attribute syntax).
  /// <para>
  /// Expected 0-based line positions (matches <see cref="XUnitLocationLines"/>):
  /// </para>
  /// <code>
  /// line 0: using Xunit;
  /// line 1:
  /// line 2: namespace Sample.Location;
  /// line 3:
  /// line 4: public class C
  /// line 5: {
  /// line 6:     [Fact]
  /// line 7:     public void M() { }
  /// line 8: }
  /// </code>
  /// </summary>
  public const string XUnitLocationMarker = """
    using Xunit;

    namespace Sample.Location;

    public class C
    {
        [Fact]
        public void M() { }
    }
    """;

  /// <summary>
  /// Expected 0-based line positions for <see cref="XUnitLocationMarker"/>.
  /// </summary>
  public static class XUnitLocationLines
  {
    public const int ClassSignature = 4;    // "public class C"
    public const int ClassBodyStart = 6;    // first member = [Fact] attribute
    public const int ClassEnd = 8;          // closing "}"
    public const int MethodSignature = 6;   // [Fact] attribute
    public const int MethodBodyStart = 7;   // "{" on same line as M() { }
    public const int MethodEnd = 7;         // "}" on same line
  }

  /// <summary>
  /// Deterministic source layout used to pin TestSourceLocator line-number output
  /// for a TUnit test. Uses file-scoped namespace + single-line method body so line
  /// positions are easy to assert.
  /// <para>
  /// Expected 0-based line positions (matches <see cref="TUnitLocationLines"/>):
  /// </para>
  /// <code>
  /// line 0: namespace Sample.Location;
  /// line 1:
  /// line 2: public class C
  /// line 3: {
  /// line 4:     [Test]
  /// line 5:     public async Task M() { await Task.CompletedTask; }
  /// line 6: }
  /// </code>
  /// </summary>
  public const string TUnitLocationMarker = """
    namespace Sample.Location;

    public class C
    {
        [Test]
        public async Task M() { await Task.CompletedTask; }
    }
    """;

  /// <summary>
  /// Expected 0-based line positions for <see cref="TUnitLocationMarker"/>.
  /// </summary>
  public static class TUnitLocationLines
  {
    public const int ClassSignature = 2;    // "public class C"
    public const int ClassBodyStart = 4;    // first member = [Test] attribute
    public const int ClassEnd = 6;          // closing "}"
    public const int MethodSignature = 4;   // [Test] attribute
    public const int MethodBodyStart = 5;   // first token inside "{" (= "await")
    public const int MethodEnd = 5;         // closing "}" on same line
  }

  /// <summary>
  /// Plain (non-parameterised) test with a custom display name, xUnit form.
  /// Expected: TestMethod node's DisplayName equals "custom fact name".
  /// </summary>
  public const string XUnitCustomDisplayName = """
    using Xunit;

    namespace Sample.CustomName
    {
        public class C
        {
            [Fact(DisplayName = "custom fact name")]
            public void M() { }
        }
    }
    """;

  /// <summary>
  /// Plain test with a custom display name, MSTest form. Uses the
  /// <c>[TestMethod("...")]</c> ctor overload (MSTest.TestFramework 3.x).
  /// </summary>
  public const string MsTestCustomDisplayName = """
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace Sample.CustomName
    {
        [TestClass]
        public class C
        {
            [TestMethod("custom method name")]
            public void M() { }
        }
    }
    """;

  /// <summary>
  /// Plain TUnit test with a custom display name via the <c>[DisplayName]</c> attribute.
  /// </summary>
  public const string TUnitCustomDisplayName = """
    namespace Sample.CustomName
    {
        public class C
        {
            [Test]
            [DisplayName("custom test name")]
            public async Task M() => await Task.CompletedTask;
        }
    }
    """;

  /// <summary>
  /// xUnit parameterised test using a dynamic member data source (<c>[MemberData]</c>).
  /// Adapters often route MemberData through a different code path than InlineData —
  /// the shape should still be TheoryGroup + N Subcases. Source shared between v2 and v3.
  /// </summary>
  public const string XUnitMemberData = """
    using System.Collections.Generic;
    using Xunit;

    namespace Sample.Parameterized
    {
        public class Rows
        {
            public static IEnumerable<object[]> Data() => new[]
            {
                new object[] { 1 },
                new object[] { 2 },
            };

            [Theory]
            [MemberData(nameof(Data))]
            public void M(int x) { _ = x; }
        }
    }
    """;

  /// <summary>
  /// NUnit parameterised test using <c>[TestCaseSource]</c>. Shared between v3 and v4.
  /// </summary>
  public const string NUnitTestCaseSource = """
    using System.Collections.Generic;
    using NUnit.Framework;

    namespace Sample.Parameterized
    {
        public class Rows
        {
            public static IEnumerable<int> Data() { yield return 1; yield return 2; }

            [TestCaseSource(nameof(Data))]
            public void M(int x) { _ = x; }
        }
    }
    """;

  /// <summary>
  /// xUnit parameterised rows with arguments that stress DisplayName parsing:
  /// a null, a dotted string, and a string containing parens. Each row should still
  /// surface as its own Subcase — no FQN mis-split, no dropped rows.
  /// Shared between v2 and v3.
  /// </summary>
  public const string XUnitComplexArgs = """
    using Xunit;

    namespace Sample.ComplexArgs
    {
        public class Rows
        {
            [Theory]
            [InlineData(null)]
            [InlineData("a.b.c")]
            [InlineData("with (parens)")]
            public void M(string? x) { _ = x; }
        }
    }
    """;

  /// <summary>
  /// NUnit parameterised rows with arguments that stress DisplayName parsing,
  /// using <c>[TestCase]</c>. Shared between v3 and v4.
  /// </summary>
  public const string NUnitComplexArgs = """
    using NUnit.Framework;

    namespace Sample.ComplexArgs
    {
        public class Rows
        {
            [TestCase(null)]
            [TestCase("a.b.c")]
            [TestCase("with (parens)")]
            public void M(string? x) { _ = x; }
        }
    }
    """;

  /// <summary>
  /// MSTest parameterised test using <c>[DynamicData]</c>.
  /// </summary>
  public const string MsTestDynamicData = """
    using System.Collections.Generic;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace Sample.Parameterized
    {
        [TestClass]
        public class Rows
        {
            public static IEnumerable<object[]> Data()
            {
                yield return new object[] { 1 };
                yield return new object[] { 2 };
            }

            [DataTestMethod]
            [DynamicData(nameof(Data), DynamicDataSourceType.Method)]
            public void M(int x) { _ = x; }
        }
    }
    """;
}