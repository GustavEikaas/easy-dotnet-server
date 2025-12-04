using System.Threading.Tasks;
using StreamJsonRpc;

namespace EasyDotnet.IDE.OutputWindow;

public class Controller(ReplState state)
{

  [JsonRpcMethod("debugger/output")]
  public async Task OnOutputAsync(string output)
  {
    lock (state.SyncLock)
    {
      state.AppendOutput(output);
    }
  }

  [JsonRpcMethod("debugger/variables")]
  public async Task OnVariablesAsync(string[] variableNames)
  {
    lock (state.SyncLock)
    {
      state.AutocompleteVariables = variableNames;
    }
  }

  [JsonRpcMethod("debugger/stopped")]
  public async Task OnStoppedAsync()
  {
    lock (state.SyncLock)
    {

      state.IsInputVisible = true;
    }
  }

  [JsonRpcMethod("debugger/started")]
  public async Task OnStartedAsync()
  {
    lock (state.SyncLock)
    {
      state.IsInputVisible = false;
      state.AutocompleteVariables = [];
      state.CurrentInput = "";
    }
  }
}
