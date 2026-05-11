using System.Diagnostics;

Console.WriteLine($"Hot loop demo, pid {Environment.ProcessId}. Running for ~8s.");

var sw = Stopwatch.StartNew();
var totalSw = Stopwatch.StartNew();
long iterations = 0;

while (totalSw.Elapsed.TotalSeconds < 120)
{
  HotA();
  HotB();
  Cold();
  iterations++;

  if (sw.Elapsed.TotalSeconds >= 2)
  {
    Console.WriteLine($"iterations={iterations:N0} elapsed={totalSw.Elapsed.TotalSeconds:F1}s");
    sw.Restart();
  }
}

Console.WriteLine($"Done. Total iterations={iterations:N0}");

static double HotA()
{
  double acc = 0;
  for (var i = 0; i < 200_000; i++)
  {
    acc += Math.Sqrt(i) * Math.Sin(i);
  }
  return acc;
}

static long HotB()
{
  long acc = 0;
  for (var i = 0; i < 200_000; i++)
  {
    acc += (i * 1103515245L + 12345L) & 0x7FFFFFFF;
  }
  return acc;
}

static int Cold()
{
  Thread.Sleep(1);
  return 0;
}