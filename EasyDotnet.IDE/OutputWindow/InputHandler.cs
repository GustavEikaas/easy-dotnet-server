using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using StreamJsonRpc;

namespace EasyDotnet.IDE.OutputWindow;

public sealed record EvaluateRequest(string Expression);
/// <summary>
/// Handles keyboard input and expression evaluation.
/// </summary>
public sealed class InputHandler(ReplState state, JsonRpc rpc, LayoutManager layoutManager)
{

  /// <summary>
  /// Starts the input processing loop.
  /// </summary>
  public async Task StartAsync(RenderLoop renderLoop, CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      if (!Console.KeyAvailable)
      {
        await Task.Delay(10, cancellationToken);
        continue;
      }

      var key = Console.ReadKey(intercept: true);

      lock (state.SyncLock)
      {
        // Only process input if visible
        if (!state.IsInputVisible)
          continue;

        // Get the current layout from render loop
        ProcessKey(key, renderLoop.CurrentLayout);
      }

      await Task.Delay(10, cancellationToken);
    }
  }

  /// <summary>
  /// Processes a single key press.
  /// </summary>
  private void ProcessKey(ConsoleKeyInfo key, Layout layout)
  {
    switch (key.Key)
    {
      case ConsoleKey.Enter:
        HandleEnter();
        break;

      case ConsoleKey.Backspace:
        HandleBackspace(layout);
        break;

      case ConsoleKey.Tab:
        HandleTab(layout);
        break;

      case ConsoleKey.Escape:
        HandleEscape(layout);
        break;

      default:
        if (!char.IsControl(key.KeyChar))
        {
          HandleCharacter(key.KeyChar, layout);
        }
        break;
    }
  }

  /// <summary>
  /// Handles Enter key - submits expression for evaluation.
  /// </summary>
  private void HandleEnter()
  {
    var expression = state.ConsumeInput().Trim();

    if (string.IsNullOrEmpty(expression))
      return;

    // Echo the expression to output
    // state.AppendOutput($"[cyan]> {expression}[/]");

    // Evaluate asynchronously
    _ = Task.Run(async () =>
    {
      try
      {
        var result = await rpc.InvokeAsync<EvaluateRequest>("debugger/evaluate", new { expression = expression });
        // state.AppendOutput($"[green]{result}[/]");
      }
      catch (RemoteInvocationException ex)
      {
        state.AppendOutput($"[red]Error: {ex.Message}[/]");
      }
      catch (Exception ex)
      {
        state.AppendOutput($"[red]Unexpected error: {ex.Message}[/]");
      }
    });
  }

  /// <summary>
  /// Handles Backspace key - removes last character.
  /// </summary>
  private void HandleBackspace(Layout layout)
  {
    state.Backspace();
    layoutManager.UpdateInputPanel(layout);
  }

  /// <summary>
  /// Handles Tab key - accepts autocomplete suggestion.
  /// </summary>
  private void HandleTab(Layout layout)
  {
    state.AcceptAutocomplete();
    layoutManager.UpdateInputPanel(layout);
  }

  /// <summary>
  /// Handles Escape key - clears current input.
  /// </summary>
  private void HandleEscape(Layout layout)
  {
    state.CurrentInput = "";
    layoutManager.UpdateInputPanel(layout);
  }

  /// <summary>
  /// Handles regular character input.
  /// </summary>
  private void HandleCharacter(char c, Layout layout)
  {
    state.AppendToInput(c);
    layoutManager.UpdateInputPanel(layout);
  }
}