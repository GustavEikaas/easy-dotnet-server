using EasyDotnet.TestRunner.Models;

namespace EasyDotnet.TestRunner.Notifications;

public sealed record TestNodeStatusUpdateNotification(
  string NodeId,
  TestNodeStatus Status
);