using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace EasyDotnet.IDE.OutputWindow;

/// <summary>
/// Manages the rendering loop for updating the UI.
/// </summary>
public sealed class RenderLoop(ReplState state, LayoutManager layoutManager)
{
  /// <summary>
  /// Gets the current layout instance for use by InputHandler.
  /// </summary>
  public Layout CurrentLayout { get; private set; } = layoutManager.CreateLayout();

  /// <summary>
  /// Starts the render loop using Spectre.Console Live display.
  /// </summary>
  public async Task StartAsync(CancellationToken cancellationToken)
  {
    var wasInputVisible = state.IsInputVisible;

    var layout = AnsiConsole.Live(CurrentLayout).AutoClear(false);
    layout.Cropping = VerticalOverflowCropping.Bottom;

    await layout.StartAsync(async ctx =>
    {
      while (!cancellationToken.IsCancellationRequested)
      {
        var needsUpdate = false;

        lock (state.SyncLock)
        {
          // Check if we need to rebuild the entire layout structure
          if (layoutManager.NeedsLayoutRebuild(wasInputVisible))
          {
            CurrentLayout = layoutManager.CreateLayout();
            ctx.UpdateTarget(CurrentLayout);
            wasInputVisible = state.IsInputVisible;
            needsUpdate = true;
          }
          else
          {
            // Just update the content of existing panels
            layoutManager.UpdateOutputPanel(CurrentLayout);

            // Update input panel if it's visible
            if (state.IsInputVisible)
            {
              layoutManager.UpdateInputPanel(CurrentLayout);
            }

            needsUpdate = true;
          }
        }

        if (needsUpdate)
        {
          ctx.Refresh();
        }

        // Run at approximately 60fps
        await Task.Delay(16, cancellationToken);
      }
    });
  }
}