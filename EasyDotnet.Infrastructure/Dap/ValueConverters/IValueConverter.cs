namespace EasyDotnet.Infrastructure.Dap.ValueConverters;

public interface IVariableConverter
{
    bool CanConvert(DAP.Variable variable);

    Task<DAP.Variable> ConvertAsync(
        DAP.Variable variable,
        Func<int> getNextSequence,
        Func<DAP.VariablesRequest, int, CancellationToken, Task<DAP.VariablesResponse>> resolveVariable
    );
     
}