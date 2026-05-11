using EasyDotnet.IDE.Services;

namespace EasyDotnet.IDE.Tests.Editor;

public class OpenBufferServiceTests
{
  [Test]
  public async Task OnBufferOpened_MakesIsOpenTrue()
  {
    var svc = new OpenBufferService();
    var path = Path.Combine(Path.GetTempPath(), "Foo.cs");
    svc.OnBufferOpened(path);
    await Assert.That(svc.IsOpen(path)).IsTrue();
  }

  [Test]
  public async Task OnBufferClosed_RemovesEntry()
  {
    var svc = new OpenBufferService();
    var path = Path.Combine(Path.GetTempPath(), "Foo.cs");
    svc.OnBufferOpened(path);
    svc.OnBufferClosed(path);
    await Assert.That(svc.IsOpen(path)).IsFalse();
  }

  [Test]
  public async Task OnBufferOpened_NormalizesRelativePaths()
  {
    var svc = new OpenBufferService();
    var tmp = Path.GetTempPath();
    var absolute = Path.Combine(tmp, "Sub", "Foo.cs");
    Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
    try
    {
      // Opening via a non-canonical form (extra `..` segment) should match the canonical lookup.
      var nonCanonical = Path.Combine(tmp, "Sub", "..", "Sub", "Foo.cs");
      svc.OnBufferOpened(nonCanonical);
      await Assert.That(svc.IsOpen(absolute)).IsTrue();
    }
    finally
    {
      try { Directory.Delete(Path.GetDirectoryName(absolute)!, recursive: true); } catch { }
    }
  }

  [Test]
  public async Task Open_TwiceFires_Only_OneEvent()
  {
    var svc = new OpenBufferService();
    var path = Path.Combine(Path.GetTempPath(), "Foo.cs");
    var count = 0;
    svc.BufferOpened += _ => Interlocked.Increment(ref count);
    svc.OnBufferOpened(path);
    svc.OnBufferOpened(path);
    await Assert.That(count).IsEqualTo(1);
  }

  [Test]
  public async Task Close_OnUnknown_Fires_NoEvent()
  {
    var svc = new OpenBufferService();
    var fired = 0;
    svc.BufferClosed += _ => Interlocked.Increment(ref fired);
    svc.OnBufferClosed(Path.Combine(Path.GetTempPath(), "Nope.cs"));
    await Assert.That(fired).IsEqualTo(0);
  }

  [Test]
  public async Task Snapshot_ReflectsCurrentState()
  {
    var svc = new OpenBufferService();
    var a = Path.Combine(Path.GetTempPath(), "A.cs");
    var b = Path.Combine(Path.GetTempPath(), "B.cs");
    svc.OnBufferOpened(a);
    svc.OnBufferOpened(b);
    svc.OnBufferClosed(a);
    var snap = svc.Snapshot();
    await Assert.That(snap.Count).IsEqualTo(1);
    await Assert.That(snap.Contains(Path.GetFullPath(b))).IsTrue();
  }
}
