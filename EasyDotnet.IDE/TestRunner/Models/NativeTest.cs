namespace EasyDotnet.IDE.TestRunner.Models;

/// <summary>
/// A test's framework id (VSTest GUID or MTP Uid) plus an optional adapter-specific handle used to
/// run it without rediscovering (VSTest's TestCase).
/// </summary>
public sealed record NativeTest(string Id, object? RunHandle = null);