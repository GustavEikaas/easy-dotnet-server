using EasyDotnet.Application.Interfaces;
using Microsoft.TemplateEngine.Abstractions;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public sealed class PostActionProcessor(
    IEnumerable<IPostActionHandler> handlers,
    IEditorService editorService)
{
  private readonly Dictionary<Guid, IPostActionHandler> _handlers = handlers.ToDictionary(h => h.ActionId);

  public async Task ProcessAsync(
      IReadOnlyList<IPostAction> postActions,
      IReadOnlyList<ICreationPath> primaryOutputs,
      string workingDirectory,
      CancellationToken cancellationToken)
  {
    foreach (var action in postActions)
    {
      var shouldContinue = await ProcessSingleAsync(
          action,
          primaryOutputs,
          workingDirectory,
          cancellationToken);

      if (!shouldContinue)
        return;
    }
  }

  private async Task<bool> ProcessSingleAsync(
      IPostAction action,
      IReadOnlyList<ICreationPath> primaryOutputs,
      string workingDirectory,
      CancellationToken cancellationToken)
  {
    if (!_handlers.TryGetValue(action.ActionId, out var handler))
    {
      return await HandleFailureAsync(action, stopPipeline: !action.ContinueOnError);
    }

    var success = await TryExecuteAsync(handler, action, primaryOutputs, workingDirectory, cancellationToken);

    if (success)
    {
      return true;
    }

    return await HandleFailureAsync(action, stopPipeline: !action.ContinueOnError);
  }

  private async Task<bool> TryExecuteAsync(
      IPostActionHandler handler,
      IPostAction action,
      IReadOnlyList<ICreationPath> primaryOutputs,
      string workingDirectory,
      CancellationToken cancellationToken)
  {
    try
    {
      return await handler.Handle(action, primaryOutputs, workingDirectory, cancellationToken);
    }
    catch (Exception ex)
    {
      await editorService.DisplayError(
          $"Post action '{action.Description}' failed:\n{ex.Message}");

      return false;
    }
  }

  private async Task<bool> HandleFailureAsync(
      IPostAction action,
      bool stopPipeline)
  {
    await ShowManualInstructions(action);
    return !stopPipeline;
  }

  private async Task ShowManualInstructions(IPostAction action)
  {
    if (action.ManualInstructions == null)
    {
      return;
    }
    await editorService.DisplayMessage(action.ManualInstructions);
  }
}