using EasyDotnet.Domain.Models.Client;

namespace EasyDotnet.Controllers.Initialize;

public sealed record InitializeRequest(ClientInfo ClientInfo, ProjectInfo ProjectInfo, Options? Options);

public sealed record Options(bool UseVisualStudio = false);