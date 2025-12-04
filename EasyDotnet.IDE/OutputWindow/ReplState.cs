using System.Collections.Generic;
using System.Linq;

namespace EasyDotnet.IDE.OutputWindow;

/// <summary>
/// Represents the current state of the REPL window.
/// Thread-safe through internal locking mechanism.
/// </summary>
public sealed class ReplState
{
  private readonly List<string> _outputLines = [];
  private string[] _autocompleteVariables = [];
  private string _currentInput = "";
  private bool _isInputVisible;
  private DebuggerState _debuggerState = DebuggerState.Connecting;

  /// <summary>
  /// Lock object for synchronizing access to state across threads.
  /// </summary>
  public object SyncLock { get; } = new();

  /// <summary>
  /// Gets or sets whether the input bar should be visible.
  /// Thread-safe.
  /// </summary>
  public bool IsInputVisible
  {
    get
    {
      lock (SyncLock)
      {
        return _isInputVisible;
      }
    }
    set
    {
      lock (SyncLock)
      {
        _isInputVisible = value;
      }
    }
  }

  /// <summary>
  /// Gets or sets the current debugger state.
  /// Thread-safe.
  /// </summary>
  public DebuggerState DebuggerState
  {
    get
    {
      lock (SyncLock)
      {
        return _debuggerState;
      }
    }
    set
    {
      lock (SyncLock)
      {
        _debuggerState = value;
      }
    }
  }

  /// <summary>
  /// Gets or sets the current input buffer.
  /// Thread-safe.
  /// </summary>
  public string CurrentInput
  {
    get
    {
      lock (SyncLock)
      {
        return _currentInput;
      }
    }
    set
    {
      lock (SyncLock)
      {
        _currentInput = value ?? "";
      }
    }
  }

  /// <summary>
  /// Gets or sets the available variables for autocomplete.
  /// Thread-safe.
  /// </summary>
  public string[] AutocompleteVariables
  {
    get
    {
      lock (SyncLock)
      {
        return _autocompleteVariables;
      }
    }
    set
    {
      lock (SyncLock)
      {
        _autocompleteVariables = value ?? [];
      }
    }
  }

  /// <summary>
  /// Appends a line to the output buffer.
  /// Thread-safe.
  /// </summary>
  public void AppendOutput(string line)
  {
    lock (SyncLock)
    {
      _outputLines.Add(line);
    }
  }

  /// <summary>
  /// Gets a copy of all output lines.
  /// Thread-safe.
  /// </summary>
  public List<string> GetOutputLines()
  {
    lock (SyncLock)
    {
      return [.. _outputLines];
    }
  }

  /// <summary>
  /// Gets the output as a single joined string.
  /// Thread-safe.
  /// </summary>
  public string GetOutputText()
  {
    lock (SyncLock)
    {
      return string.Join("\n", _outputLines);
    }
  }

  /// <summary>
  /// Clears all output lines.
  /// Thread-safe.
  /// </summary>
  public void ClearOutput()
  {
    lock (SyncLock)
    {
      _outputLines.Clear();
    }
  }

  /// <summary>
  /// Gets autocomplete suggestion for the current input.
  /// Thread-safe.
  /// </summary>
  public string GetAutocompleteSuggestion()
  {
    lock (SyncLock)
    {
      if (string.IsNullOrWhiteSpace(_currentInput))
        return "";

      var match = _autocompleteVariables.FirstOrDefault(v =>
          v.StartsWith(_currentInput, System.StringComparison.OrdinalIgnoreCase));

      return match != null ? match[_currentInput.Length..] : "";
    }
  }

  /// <summary>
  /// Appends a character to the current input.
  /// Thread-safe.
  /// </summary>
  public void AppendToInput(char c)
  {
    lock (SyncLock)
    {
      _currentInput += c;
    }
  }

  /// <summary>
  /// Removes the last character from the current input.
  /// Thread-safe.
  /// </summary>
  public void Backspace()
  {
    lock (SyncLock)
    {
      if (_currentInput.Length > 0)
        _currentInput = _currentInput[..^1];
    }
  }

  /// <summary>
  /// Accepts the current autocomplete suggestion.
  /// Thread-safe.
  /// </summary>
  public void AcceptAutocomplete()
  {
    lock (SyncLock)
    {
      var suggestion = GetAutocompleteSuggestionInternal();
      if (!string.IsNullOrEmpty(suggestion))
        _currentInput += suggestion;
    }
  }

  /// <summary>
  /// Clears the current input and returns what was there.
  /// Thread-safe.
  /// </summary>
  public string ConsumeInput()
  {
    lock (SyncLock)
    {
      var input = _currentInput;
      _currentInput = "";
      return input;
    }
  }

  /// <summary>
  /// Resets state when debugger starts running.
  /// Thread-safe.
  /// </summary>
  public void OnStarted()
  {
    lock (SyncLock)
    {
      _isInputVisible = false;
      _currentInput = "";
      _autocompleteVariables = [];
      _debuggerState = DebuggerState.Running;
    }
  }

  /// <summary>
  /// Updates state when debugger stops (breakpoint hit).
  /// Thread-safe.
  /// </summary>
  public void OnStopped()
  {
    lock (SyncLock)
    {
      _isInputVisible = true;
      _debuggerState = DebuggerState.Stopped;
    }
  }

  // Internal helper that assumes lock is already held
  private string GetAutocompleteSuggestionInternal()
  {
    if (string.IsNullOrWhiteSpace(_currentInput))
      return "";

    var match = _autocompleteVariables.FirstOrDefault(v =>
        v.StartsWith(_currentInput, System.StringComparison.OrdinalIgnoreCase));

    return match != null ? match[_currentInput.Length..] : "";
  }
}

/// <summary>
/// Represents the current state of the debugger connection.
/// </summary>
public enum DebuggerState
{
  /// <summary>
  /// Connecting to the named pipe.
  /// </summary>
  Connecting,

  /// <summary>
  /// Debugger is running (not stopped at breakpoint).
  /// </summary>
  Running,

  /// <summary>
  /// Debugger is stopped at a breakpoint.
  /// </summary>
  Stopped,

  /// <summary>
  /// Disconnected from debugger.
  /// </summary>
  Disconnected
}