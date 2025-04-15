using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NerdHub.Models
{
    public class RequirementsConverter : JsonConverter<Requirements?>
    {
        public override Requirements? ReadJson(JsonReader reader, Type objectType, Requirements? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.StartObject)
            {
                // Deserialize as Requirements object
                return serializer.Deserialize<Requirements>(reader);
            }
            else if (reader.TokenType == JsonToken.StartArray)
            {
                // Skip the array and return null
                JArray.Load(reader);
                return null;
            }

            return null;
        }

        public override void WriteJson(JsonWriter writer, Requirements? value, JsonSerializer serializer)
        {
            // Serialize as an object or null
            serializer.Serialize(writer, value);
        }
    }
}