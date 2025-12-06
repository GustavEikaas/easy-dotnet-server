using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Aspire.Server;
using EasyDotnet.Aspire.Services;
using EasyDotnet.Controllers;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Aspire;

/// <summary>
/// Controller for managing Aspire AppHost debugging sessions
/// </summary>
public class AspireController(
    IMsBuildService msBuildService,
    IAspireService aspireService,
    IDcpServer dcpServer,
    ILogger<AspireController> logger) : BaseController
{

  /// <summary>
  /// Starts an Aspire debugging session for the specified AppHost project
  /// </summary>
  [JsonRpcMethod("aspire/startDebugSession")]
  public async Task<StartDebugSessionResponse> StartDebugSession(
      string projectPath,
      CancellationToken cancellationToken)
  {
    logger.LogInformation("Starting Aspire debug session for {ProjectPath}", projectPath);

    // Validate that this is an Aspire AppHost project
    var project = await msBuildService.GetOrSetProjectPropertiesAsync(
        projectPath,
        cancellationToken: cancellationToken);

    if (!project.IsAspireHost)
    {
      var projectName = Path.GetFileNameWithoutExtension(projectPath);
      throw new InvalidOperationException(
          $"{projectName} is not an Aspire AppHost project");
    }

    // Ensure DCP server is running
    await dcpServer.EnsureStartedAsync(cancellationToken);

    // Start the Aspire session
    var session = await aspireService.StartAsync(projectPath, cancellationToken);

    logger.LogInformation(
        "Aspire debug session started successfully: ProjectPath={ProjectPath}, Token={Token}, Port={Port}",
        projectPath,
        session.Token,
        dcpServer.Port);

    return new StartDebugSessionResponse
    {
      Token = session.Token,
      DcpId = session.DcpId ?? "pending",
      DcpPort = dcpServer.Port,
      ProjectPath = projectPath,
      StartedAt = session.StartedAt
    };
  }

  /// <summary>
  /// Stops an Aspire debugging session
  /// </summary>
  [JsonRpcMethod("aspire/stopDebugSession")]
  public async Task StopDebugSession(
      string projectPath,
      CancellationToken cancellationToken)
  {
    logger.LogInformation("Stopping Aspire debug session for {ProjectPath}", projectPath);

    await aspireService.StopAsync(projectPath, cancellationToken);

    logger.LogInformation("Aspire debug session stopped for {ProjectPath}", projectPath);
  }

  /// <summary>
  /// Gets the status of an Aspire debugging session
  /// </summary>
  [JsonRpcMethod("aspire/getSessionStatus")]
  public Task<SessionStatusResponse> GetSessionStatus(string projectPath)
  {
    var status = aspireService.GetSessionStatus(projectPath);

    return Task.FromResult(new SessionStatusResponse
    {
      ProjectPath = projectPath,
      Status = status?.ToString() ?? "NotFound",
      IsActive = status == AspireSessionStatus.Running
    });
  }
}

// Response models
public class StartDebugSessionResponse
{
  public required string Token { get; init; }
  public required string DcpId { get; init; }
  public required int DcpPort { get; init; }
  public required string ProjectPath { get; init; }
  public required DateTime StartedAt { get; init; }
}

public class SessionStatusResponse
{
  public required string ProjectPath { get; init; }
  public required string Status { get; init; }
  public required bool IsActive { get; init; }
}