using System;

namespace EasyDotnet.Notifications;

[AttributeUsage(AttributeTargets.Method)]
public class RpcNotificationAttribute(string name) : Attribute
{
  public string Name { get; } = name;
}