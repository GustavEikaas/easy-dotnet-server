using Spectre.Console;

namespace EasyDotnet.IDE.OutputWindow;

/// <summary>
/// Manages the creation and updating of Spectre.Console layouts for the REPL.
/// </summary>
public sealed class LayoutManager(ReplState state)
{

  const string OutputLayoutName = "output";
  const string InputLayoutName = "input";
  const string WrapperLayoutName = "root";

  /// <summary>
  /// Creates the initial layout based on current state.
  /// </summary>
  public Layout CreateLayout()
  {
    lock (state.SyncLock)
    {
      return state.IsInputVisible
        ? CreateSplitLayout()
        : CreateOutputOnlyLayout();
    }
  }

  /// <summary>
  /// Creates a layout with both output and input panels (when stopped).
  /// </summary>
  private Layout CreateSplitLayout()
  {
    var outputPanel = CreateOutputPanel();
    var inputPanel = CreateInputPanel();

    return new Layout(WrapperLayoutName)
      .SplitRows(
        new Layout(OutputLayoutName, outputPanel).Ratio(9),
        new Layout(InputLayoutName, inputPanel).Ratio(1)
      );
  }

  /// <summary>
  /// Creates a layout with only the output panel (when running).
  /// </summary>
  private Layout CreateOutputOnlyLayout()
  {
    var outputPanel = CreateOutputPanel();
    return new Layout(OutputLayoutName, outputPanel);
  }

  /// <summary>
  /// Creates the output panel with current output text.
  /// </summary>
  public Panel CreateOutputPanel()
  {
    lock (state.SyncLock)
    {
      var outputText = state.GetOutputText();
      var safe = Markup.Escape(outputText);
      var markup = string.IsNullOrEmpty(outputText)
          ? new Markup("Waiting for debugger output...")
          : new Markup(safe);

      return new Panel(markup)
      {
        Header = new PanelHeader($"Debugger Output {state.DebuggerState}"),
        Expand = true,
        Border = BoxBorder.Rounded
      };
    }
  }

  /// <summary>
  /// Creates the input panel with current input and ghost text.
  /// </summary>
  public Panel CreateInputPanel()
  {
    lock (state.SyncLock)
    {
      var input = state.CurrentInput;
      var ghost = state.GetAutocompleteSuggestion();

      var content = string.IsNullOrEmpty(ghost)
        ? $"> {input}"
        : $"> {input} {ghost}";

      return new Panel(content)
      {
        Expand = true,
        Border = BoxBorder.Heavy,
        Padding = new Padding(1, 0, 0, 0)
      };
    }
  }

  /// <summary>
  /// Updates the output panel in an existing layout.
  /// </summary>
  public void UpdateOutputPanel(Layout layout)
  {
    var outputPanel = CreateOutputPanel();
    layout[OutputLayoutName]?.Update(outputPanel);
  }

  /// <summary>
  /// Updates the input panel in an existing layout.
  /// </summary>
  public void UpdateInputPanel(Layout layout)
  {
    lock (state.SyncLock)
    {
      if (!state.IsInputVisible)
        return;

      var inputPanel = CreateInputPanel();
      layout[InputLayoutName]?.Update(inputPanel);
    }
  }

  /// <summary>
  /// Checks if the layout structure needs to be rebuilt.
  /// </summary>
  public bool NeedsLayoutRebuild(bool previousInputVisibility)
  {
    lock (state.SyncLock)
    {
      return previousInputVisibility != state.IsInputVisible;
    }
  }
}