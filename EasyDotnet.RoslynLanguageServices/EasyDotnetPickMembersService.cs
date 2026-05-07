using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.PickMembers;

namespace EasyDotnet.RoslynLanguageServices;

[ExportWorkspaceService(typeof(IPickMembersService), ServiceLayer.Host), Shared]
[method: ImportingConstructor]
[method: Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
internal sealed class EasyDotnetPickMembersService() : IPickMembersService
{
  private const string Method = "easyDotnet/pickMembers";

  public PickMembersResult PickMembers(
      string title,
      ImmutableArray<ISymbol> members,
      ImmutableArray<PickMembersOption> options = default,
      bool selectAll = true)
  {
    Console.Error.WriteLine($"[EasyDotnet] PickMembers called: {title}");
    var opts = options.IsDefault ? [] : options;
    var request = new PickMembersRequest(
        title,
        [.. members.Select(m => new PickMembersRequestMember(
                Name: m.Name,
                Kind: m.Kind.ToString(),
                Display: m.ToDisplayString(SymbolDisplayFormats.Member),
                ContainerDisplay: m.ContainingType?.ToDisplayString() ?? string.Empty))],
        [.. opts.Select(o => new PickMembersRequestOption(o.Id, o.Title, o.Value))],
        selectAll);

    PickMembersResponse? response;
    try
    {
      // IPickMembersService.PickMembers is synchronous, so we must block.
      // The handler runs on a background queue thread, not the LSP main loop, so blocking is safe.
      response = LspClientBridge.SendRequestAsync<PickMembersRequest, PickMembersResponse?>(
          Method, request, CancellationToken.None).GetAwaiter().GetResult();
    }
    catch
    {
      return PickMembersResult.Canceled;
    }

    if (response is null || response.Cancelled)
      return PickMembersResult.Canceled;

    var selected = response.SelectedNames is { Length: > 0 }
        ? [.. members.Where(m => response.SelectedNames.Contains(m.Name))]
        : members;

    var resolvedOptions = opts.Length == 0
        ? ImmutableArray<PickMembersOption>.Empty
        : [.. opts.Select(o =>
            {
                var value = response.OptionValues is { } map && map.TryGetValue(o.Id, out var v) ? v : o.Value;
                return new PickMembersOption(o.Id, o.Title, value);
            })];

    return new PickMembersResult(selected, resolvedOptions, response.SelectedAll);
  }

  private static class SymbolDisplayFormats
  {
    public static readonly SymbolDisplayFormat Member = new(
        memberOptions: SymbolDisplayMemberOptions.IncludeType
            | SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeName,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);
  }
}

internal sealed record PickMembersRequest(
    string Title,
    PickMembersRequestMember[] Members,
    PickMembersRequestOption[] Options,
    bool SelectAll);

internal sealed record PickMembersRequestMember(
    string Name,
    string Kind,
    string Display,
    string ContainerDisplay);

internal sealed record PickMembersRequestOption(string Id, string Title, bool Value);

internal sealed record PickMembersResponse(
    string[]? SelectedNames,
    Dictionary<string, bool>? OptionValues,
    bool SelectedAll,
    bool Cancelled);