using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.Domain.Models.MsBuild.Project;
using EasyDotnet.Infrastructure.Dap;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Services;

public partial class NetcoreDbgService(ILogger<NetcoreDbgService> logger, ILogger<DebuggerProxy> debuggerProxyLogger) : INetcoreDbgService
{
  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  private static readonly JsonSerializerOptions LoggingSerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
  };

  private readonly TaskCompletionSource<bool> _completionSource = new();
  private CancellationTokenSource? _cancellationTokenSource;
  private Task? _sessionTask;
  private TcpListener? _listener;
  private System.Diagnostics.Process? _process;
  private TcpClient? _client;
  private DebuggerProxy? _debuggerProxy;
  private (System.Diagnostics.Process, int)? _vsTestAttach;
  private readonly Dictionary<int, InterceptableVariablesResponse> _pendingVariablesRequests = new();
  private Task? _disposeTask;
  private int _clientSeq;

  private int GetNextSequence()
  {
    _clientSeq++;
    return _clientSeq;
  }

  public Task Completion => _completionSource.Task;

  public async Task Start(string binaryPath, DotnetProject project, string projectPath, LaunchProfile? launchProfile, (System.Diagnostics.Process, int)? vsTestAttach)
  {
    if (_disposeTask != null)
    {
      logger.LogInformation("Waiting for previous debugger session to fully dispose...");
      try
      {
        await _disposeTask;
      }
      catch
      {
      }
    }
    _vsTestAttach = vsTestAttach;
    _cancellationTokenSource = new CancellationTokenSource();

    _sessionTask = Task.Run(async () =>
    {
      try
      {
        logger.LogInformation("Waiting for client...");
        _listener = new TcpListener(IPAddress.Any, 8086);
        _listener.Start();
        logger.LogInformation("Listening for client on port 8086.");

        try
        {
          _client = await _listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(30), _cancellationTokenSource.Token);
          logger.LogInformation("Client connected.");
        }
        catch (TimeoutException)
        {
          logger.LogWarning("No client connected within 30 seconds. Triggering cleanup.");
          TriggerCleanup();
          _completionSource.SetCanceled();
          return;
        }

        var tcpStream = _client.GetStream();
        var clientDap = new Client(tcpStream, tcpStream, async (msg, stream) =>
        {
          msg.Seq = GetNextSequence();
          try
          {
            switch (msg)
            {
              case InterceptableAttachRequest attachReq:
                var modified = await InitializeRequestRewriter.CreateInitRequestBasedOnProjectType(
                    project,
                    launchProfile,
                    attachReq,
                    Path.GetDirectoryName(projectPath)!,
                    attachReq.Seq,
                    vsTestAttach?.Item2
                );
                logger.LogInformation("[TCP] Intercepted attach request: {modified}", JsonSerializer.Serialize(modified, LoggingSerializerOptions));
                await DapMessageWriter.WriteDapMessageAsync(modified, stream, CancellationToken.None);
                break;

              case Request req:
                logger.LogInformation("[TCP] request: {message}", JsonSerializer.Serialize(req, LoggingSerializerOptions));
                Console.WriteLine($"Request command: {req.Command}");
                await DapMessageWriter.WriteDapMessageAsync(req, stream, CancellationToken.None);
                break;

              default:
                throw new Exception($"Unsupported DAP message from client: {msg}");
            }
          }
          catch (Exception e)
          {
            logger.LogError("Exception {e}", e);
            throw;
          }
        });

        _process = new System.Diagnostics.Process
        {
          StartInfo = new ProcessStartInfo
          {
            FileName = binaryPath,
            Arguments = "--interpreter=vscode",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
          },
          EnableRaisingEvents = true,
        };

        _process.Exited += (sender, args) =>
        {
          logger.LogInformation("Debugger process exited.");
          TriggerCleanup();
        };

        _process.Start();

        var debuggerDap = new Dap.Debugger(_process.StandardInput.BaseStream, _process.StandardOutput.BaseStream, async (msg, stream) =>
        {
          try
          {
            switch (msg)
            {
              case InterceptableVariablesResponse varsRes:
                var modifiedResponse = await TryExpandListVariablesAsync(varsRes);
                await DapMessageWriter.WriteDapMessageAsync(modifiedResponse, stream, CancellationToken.None);
                break;
              case Response res:
                logger.LogInformation("[DBG] response: {message}", JsonSerializer.Serialize(res, LoggingSerializerOptions));
                await DapMessageWriter.WriteDapMessageAsync(res, stream, CancellationToken.None);
                break;
              case Event e:
                logger.LogInformation("[DBG] event: {message}", JsonSerializer.Serialize(e, LoggingSerializerOptions));
                await DapMessageWriter.WriteDapMessageAsync(e, stream, CancellationToken.None);
                break;
              default:
                throw new Exception($"Unsupported DAP message from debugger: {msg}");
            }
          }
          catch (Exception e)
          {
            logger.LogError("Exception {e}", e);
            throw;
          }
        });

        _debuggerProxy = new DebuggerProxy(clientDap, debuggerDap, debuggerProxyLogger);

        _debuggerProxy.Start(_cancellationTokenSource.Token, TriggerCleanup);

        await _debuggerProxy.Completion;

        _completionSource.SetResult(true);
      }
      catch (OperationCanceledException)
      {
        logger.LogInformation("Operation was canceled.");
        _completionSource.SetCanceled();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "An unhandled exception occurred in the debugging session.");
        _completionSource.SetException(ex);
        throw;
      }
    }, _cancellationTokenSource.Token);
  }

  private static readonly Regex ListTypePattern = new(@"System\.Collections\.Generic\.List(?:<.*?>|\\u003C.*?\\u003E)");

  private static bool IsExpandableListType(InterceptableVariable variable) => variable.VariablesReference != 0 &&
           !string.IsNullOrEmpty(variable.Type) &&
           ListTypePattern.IsMatch(variable.Type);

  private async Task<InterceptableVariablesResponse?> GetVariableExpansionAsync(int variablesReference)
  {
    var seq = GetNextSequence();
    var req = new InternalVariablesRequest
    {
      Seq = seq,
      Command = "variables",
      Type = "request",
      Arguments = new InternalVariablesArguments { VariablesReference = variablesReference }
    };

    try
    {
      return await _debuggerProxy!.RunInternalDebuggerRequestAsync<InterceptableVariablesResponse>(
          JsonSerializer.Serialize(req, SerializerOptions), seq, CancellationToken.None);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to expand variables reference {reference}", variablesReference);
      return null;
    }
  }

  private async Task<List<InterceptableVariable>?> ExtractListItemsAsync(InterceptableVariable listVariable, InterceptableVariablesResponse listExpansion)
  {
    // Find the _items array
    var itemsVar = listExpansion.Body.Variables.Find(x => x.Name == "_items" && x.VariablesReference != 0);
    if (itemsVar == null)
    {
      logger.LogWarning("Could not find _items in List expansion for {name}", listVariable.Name);
      return null;
    }

    // Get the actual array contents
    var itemsExpansion = await GetVariableExpansionAsync(itemsVar.VariablesReference);
    if (itemsExpansion == null)
    {
      return null;
    }

    // Get the actual count of items (not array capacity)
    var countVar = listExpansion.Body.Variables.Find(x => x.Name == "_size" || x.Name == "Count");
    var count = countVar != null && int.TryParse(countVar.Value, out var c) ? c : itemsExpansion.Body.Variables.Count;

    var listItems = new List<InterceptableVariable>();

    // Take only the actual items (not empty slots)
    for (var i = 0; i < Math.Min(count, itemsExpansion.Body.Variables.Count); i++)
    {
      var item = itemsExpansion.Body.Variables[i];
      listItems.Add(new InterceptableVariable
      {
        Name = $"[{i}]",
        Value = item.Value,
        Type = item.Type,
        EvaluateName = $"{listVariable.EvaluateName}[{i}]",
        VariablesReference = item.VariablesReference,
        NamedVariables = item.NamedVariables
      });
    }

    return listItems;
  }

  private InterceptableVariablesResponse CreateModifiedResponse(InterceptableVariablesResponse original,
      InterceptableVariable originalListVar, List<InterceptableVariable> expandedItems)
  {
    var modifiedVariables = original.Body.Variables.ToList();
    var listIndex = modifiedVariables.FindIndex(x => x == originalListVar);

    if (listIndex != -1)
    {
      // Remove the original List variable and insert expanded items
      modifiedVariables.RemoveAt(listIndex);
      modifiedVariables.InsertRange(listIndex, expandedItems);
    }

    return new InterceptableVariablesResponse
    {
      Seq = original.Seq,
      Type = original.Type,
      RequestSeq = original.RequestSeq,
      Success = original.Success,
      Command = original.Command,
      Message = original.Message,
      Body = new InterceptableVariablesResponseBody
      {
        Variables = modifiedVariables
      }
    };
  }

  private async Task<InterceptableVariablesResponse> TryExpandListVariablesAsync(InterceptableVariablesResponse varsRes)
  {
    var listVar = varsRes.Body.Variables.Find(IsExpandableListType);
    if (listVar == null)
    {
      return varsRes; // No expandable lists found
    }

    logger.LogInformation("[TCP] Found expandable List: {name} ({type})", listVar.Name, listVar.Type);

    // Get the List internals (_items, _size, etc.)
    var listExpansion = await GetVariableExpansionAsync(listVar.VariablesReference);
    if (listExpansion == null)
    {
      return varsRes; // Couldn't expand, return original
    }

    // Extract the actual list items
    var listItems = await ExtractListItemsAsync(listVar, listExpansion);
    if (listItems == null || listItems.Count == 0)
    {
      return varsRes; // Couldn't get items, return original
    }

    logger.LogInformation("[TCP] Expanded List<T> '{name}' with {count} items", listVar.Name, listItems.Count);

    return CreateModifiedResponse(varsRes, listVar, listItems);
  }

  private void TriggerCleanup()
  {
    logger.LogInformation("Triggering cleanup due to disconnection or process exit.");

    if (_cancellationTokenSource?.IsCancellationRequested != false)
    {
      return;
    }

    _ = Task.Run(async () =>
    {
      try
      {
        await DisposeAsync();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error during cleanup");
      }
    });
  }

  public async ValueTask DisposeAsync()
  {
    if (_cancellationTokenSource?.IsCancellationRequested == false)
    {
      _cancellationTokenSource.Cancel();
    }

    _disposeTask = Task.Run(async () =>
    {
      if (_sessionTask != null)
      {
        try
        {
          await _sessionTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
          logger.LogWarning("Graceful shutdown timed out. Forcing cleanup.");
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "Exception during graceful shutdown.");
        }
      }

      _client?.Close();
      _listener?.Stop();
      SafeDisposeProcess(_process, "Debugger");

      if (_vsTestAttach.HasValue)
      {
        var (process, pid) = _vsTestAttach.Value;
        SafeDisposeProcess(process, "VsTest");
        SafeDisposeProcessById(pid, "VsTestHost");
      }

      _cancellationTokenSource?.Dispose();
    });

    await _disposeTask;
  }
  private void SafeDisposeProcess(System.Diagnostics.Process? process, string processName)
  {
    if (process == null) return;

    try
    {
      if (!process.HasExited)
      {
        process.Kill();
        logger.LogInformation("Killed {processName} process", processName);
      }
      else
      {
        logger.LogInformation("{processName} process already exited", processName);
      }
    }
    catch (InvalidOperationException)
    {
      logger.LogInformation("{processName} process already exited", processName);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to kill {processName} process", processName);
    }
    finally
    {
      try
      {
        process.Dispose();
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Failed to dispose {processName} process", processName);
      }
    }
  }

  private void SafeDisposeProcessById(int pid, string processName)
  {
    try
    {
      var process = System.Diagnostics.Process.GetProcessById(pid);
      SafeDisposeProcess(process, $"{processName} (PID: {pid})");
    }
    catch (ArgumentException)
    {
      logger.LogInformation("{processName} (PID: {pid}) not found", processName, pid);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to get {processName} process by PID: {pid}", processName, pid);
    }
  }

  [GeneratedRegex(@"""select_project"":\s*""REWRITE_ATTACH""")]
  private static partial Regex AttachRequestPattern();
}