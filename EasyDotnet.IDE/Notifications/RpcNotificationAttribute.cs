using System;

namespace EasyDotnet.IDE.Notifications;

[AttributeUsage(AttributeTargets.Method)]
public class RpcNotificationAttribute(string name) : Attribute
{
  public string Name { get; } = name;
}