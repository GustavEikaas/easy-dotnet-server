namespace EasyDotnet.Infrastructure.Dap.ValueConverters;

public interface IVariableConverter
{
    bool CanConvert(InterceptableVariable variable);

    Task<InterceptableVariable> ConvertAsync(
        InterceptableVariable variable,
        Func<int> getNextSequence,
        Func<InternalVariablesRequest, int, CancellationToken, Task<InterceptableVariablesResponse>> resolveVariable
    );
     
}