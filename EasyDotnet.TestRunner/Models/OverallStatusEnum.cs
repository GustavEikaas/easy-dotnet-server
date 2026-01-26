using System.Text.Json.Serialization;
using Newtonsoft.Json.Converters;

namespace EasyDotnet.TestRunner.Models;

[JsonConverter(typeof(StringEnumConverter))]
public enum OverallStatusEnum
{
  Idle = 0,
  Running = 1,
  Passed = 2,
  Failed = 3,
  Cancelled = 4
}