using Newtonsoft.Json;

namespace EasyDotnet.TestRunner;

public class UnionTypeNameConverter<T> : JsonConverter<T> where T : class
{
  public override void WriteJson(JsonWriter writer, T? value, JsonSerializer serializer)
  {
    if (value == null)
    {
      writer.WriteNull();
      return;
    }

    writer.WriteValue(value.GetType().Name);
  }

  public override T? ReadJson(JsonReader reader, Type objectType, T? existingValue, bool hasExistingValue, JsonSerializer serializer) => throw new NotImplementedException("Deserialization not implemented for union types");
}