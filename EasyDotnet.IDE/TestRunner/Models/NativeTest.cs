namespace EasyDotnet.IDE.TestRunner.Models;

/// <summary>
/// How the framework identifies a test, as stored in the registry and handed back to the adapter to run it.
/// </summary>
/// <param name="Id">VSTest GUID string or MTP Uid. Results and start events are matched on it.</param>
/// <param name="RunHandle">
/// Object the adapter produced during discovery and needs to run the test again without rediscovering
/// (VSTest's TestCase). Opaque to everything but that adapter; null for MTP and for probable tests.
/// Lives exactly as long as the registry node, so it is replaced or dropped whenever the node is.
/// </param>
public sealed record NativeTest(string Id, object? RunHandle = null);