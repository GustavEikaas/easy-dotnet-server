using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.PickMembers;

namespace EasyDotnet.RoslynLanguageServices.PickMembers;

[ExportWorkspaceService(typeof(IPickMembersService), ServiceLayer.Host), Shared]
internal sealed class EasyDotnetPickMembersService : IPickMembersService
{
  public PickMembersResult PickMembers(
    string title,
    ImmutableArray<ISymbol> members,
    ImmutableArray<PickMembersOption> options = default,
    bool selectAll = true)
    => new(
      members,
      options.IsDefault ? ImmutableArray<PickMembersOption>.Empty : options,
      selectedAll: true);
}
