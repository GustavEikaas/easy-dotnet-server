using Microsoft.TemplateEngine.Abstractions;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public interface IPostActionHandler
{
  Guid ActionId { get; }

  Task<bool> Handle(IPostAction postAction, IReadOnlyList<ICreationPath> primaryOutputs, string workingDirectory, CancellationToken cancellationToken);
}