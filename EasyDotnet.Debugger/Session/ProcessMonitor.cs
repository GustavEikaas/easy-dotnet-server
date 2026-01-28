using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.Session;

public class ProcessMonitor : IDisposable
{
  private readonly int _processId;
  private readonly ILogger<ProcessMonitor> _logger;
  private readonly Process? _process;
  private DateTime _lastCpuCheck;
  private TimeSpan _lastTotalProcessorTime;
  private bool _isFirstCheck = true;

  public ProcessMonitor(int processId, ILogger<ProcessMonitor> logger)
  {
    _processId = processId;
    _logger = logger;

    try
    {
      _process = Process.GetProcessById(processId);
      _lastTotalProcessorTime = _process.TotalProcessorTime;
      _lastCpuCheck = DateTime.UtcNow;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get process {ProcessId}", processId);
    }
  }

  public double GetCpuUsage()
  {
    if (_process?.HasExited != false)
      return 0;

    try
    {
      var currentTime = DateTime.UtcNow;
      var currentTotalProcessorTime = _process.TotalProcessorTime;

      if (_isFirstCheck)
      {
        _isFirstCheck = false;
        _lastCpuCheck = currentTime;
        _lastTotalProcessorTime = currentTotalProcessorTime;
        return 0;
      }

      var cpuUsedMs = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
      var totalMsPassed = (currentTime - _lastCpuCheck).TotalMilliseconds;
      var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
      var cpuUsagePercentage = cpuUsageTotal * 100;

      _lastCpuCheck = currentTime;
      _lastTotalProcessorTime = currentTotalProcessorTime;

      return Math.Min(100, Math.Max(0, cpuUsagePercentage));
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error getting CPU usage for process {ProcessId}", _processId);
      return 0;
    }
  }

  public long GetMemoryUsage()
  {
    if (_process == null || _process.HasExited)
      return 0;

    try
    {
      _process.Refresh();
      return _process.WorkingSet64;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error getting memory usage for process {ProcessId}", _processId);
      return 0;
    }
  }

  public void Dispose() => _process?.Dispose();
}