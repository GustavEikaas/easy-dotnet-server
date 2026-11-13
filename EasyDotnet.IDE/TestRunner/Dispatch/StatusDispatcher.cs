using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Models.Client.Quickfix;
using EasyDotnet.IDE.TestRunner.Lock;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;

namespace EasyDotnet.IDE.TestRunner.Dispatch;

/// <summary>
/// Single choke point for all outbound testrunner events, delivered through <see cref="ITestRunnerClient"/>.
/// Suppresses duplicate status notifications — if a node's status type hasn't changed,
/// the notification is dropped to avoid spamming the client.
/// </summary>
public class StatusDispatcher(
    ITestRunnerClient client,
    NodeRegistry registry,
    IEditorService editorService,
    GlobalOperationLock operationLock)
{
  private bool ShouldDispatch(long? operationId)
  {
    if (operationId is null) return true;
    return operationId.Value == operationLock.CurrentOperationId;
  }

  /// <summary>Notify the client of a node registration or update.</summary>
  public Task SendRegisterTestAsync(TestNode node, long? operationId = null)
  {
    if (!ShouldDispatch(operationId)) return Task.CompletedTask;
    return client.RegisterTestAsync(node);
  }

  /// <summary>
  /// Tell the client to remove a node that no longer exists after rediscovery.
  /// Emitted after discovery completes — only for nodes that were not re-registered.
  /// </summary>
  public Task SendRemoveTestAsync(string nodeId, long? operationId = null)
  {
    if (!ShouldDispatch(operationId)) return Task.CompletedTask;
    return client.RemoveTestAsync(nodeId);
  }

  /// <summary>
  /// Send a status update for a single node.
  /// No-ops if the status type is identical to the last sent status for this node.
  /// </summary>
  public Task SendStatusAsync(string nodeId, TestNodeStatus? status, List<TestAction>? availableActions = null, long? operationId = null)
  {
    if (!ShouldDispatch(operationId)) return Task.CompletedTask;

    if (!registry.SetLastStatus(nodeId, status))
    {
      return Task.CompletedTask;
    }

    return client.UpdateStatusAsync(nodeId, status, availableActions);
  }

  public Task SendStatusUpdatesAsync(
      IEnumerable<StatusUpdate> statusUpdates,
      long? operationId = null)
  {
    if (!ShouldDispatch(operationId)) return Task.CompletedTask;

    var updates = statusUpdates
        .Where(u => registry.SetLastStatus(u.Id, u.Status))
        .ToList();

    if (updates.Count == 0)
    {
      return Task.CompletedTask;
    }

    return client.UpdateStatusBatchAsync(updates);
  }

  /// <summary>
  /// Sends a single batched status update covering a node and all its descendants.
  /// Filters out nodes whose status type hasn't changed.
  /// </summary>
  public Task SendBatchStatusAsync(
      string rootId,
      TestNodeStatus? status,
      NodeRegistry reg,
      List<TestAction>? availableActions = null,
      long? operationId = null)
  {
    if (!ShouldDispatch(operationId)) return Task.CompletedTask;

    var updates = reg.GetDescendants(rootId)
        .Select(n => n.Id)
        .Prepend(rootId)
        .Where(id => reg.SetLastStatus(id, status))
        .Select(id => new StatusUpdate(id, status, availableActions))
        .ToList();

    if (updates.Count == 0)
    {
      return Task.CompletedTask;
    }

    return client.UpdateStatusBatchAsync(updates);
  }

  /// <summary>Broadcast global runner status (IsLoading, aggregate counts, etc.).</summary>
  public Task SendRunnerStatusAsync(TestRunnerStatus status, long? operationId = null)
  {
    if (!ShouldDispatch(operationId)) return Task.CompletedTask;
    return client.SendRunnerStatusAsync(status);
  }

  public Task<bool> IsTestRunnerVisibleAsync(CancellationToken ct) =>
      client.IsTestRunnerVisibleAsync(ct);

  public Task SendQuickFixAsync(IEnumerable<BuildDiagnostic> diagnostics)
  {
    var items = diagnostics.Select(d => new QuickFixItem(
        FileName: d.File ?? "",
        LineNumber: d.LineNumber,
        ColumnNumber: d.ColumnNumber,
        Text: $"{d.Code}: {d.Message}",
        Type: d.Severity == BuildDiagnosticSeverity.Error ? QuickFixItemType.Error
                    : d.Severity == BuildDiagnosticSeverity.Warning ? QuickFixItemType.Warning
                    : QuickFixItemType.Information
    )).ToArray();

    return editorService.SetQuickFixList(items);
  }
}

public sealed record StatusUpdate(
    string Id,
    TestNodeStatus? Status,
    List<TestAction>? AvailableActions = null);