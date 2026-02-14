using Microsoft.TemplateEngine.Abstractions;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public interface IPostActionHandler
{
  Guid ActionId { get; }

  Task Handle(IPostAction postAction, CancellationToken cancellationToken = default);
}
