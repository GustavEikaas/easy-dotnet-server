using EasyDotnet.Domain.Models.Client;

namespace EasyDotnet.IDE.Controllers.Initialize;

public sealed record InitializeRequest(ClientInfo ClientInfo, ProjectInfo ProjectInfo, ClientOptions? ClientOptions);