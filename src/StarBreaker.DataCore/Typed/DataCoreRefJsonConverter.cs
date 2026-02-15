using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarBreaker.DataCore;

/// <summary>
/// JSON converter factory for DataCoreRef that serializes the reference appropriately:
/// - External file references: serializes as { "$ref": "path/to/file" }
/// - Internal references: serializes the Value directly (resolved lazily)
/// - Null references: serializes as null
/// </summary>
public class DataCoreRefJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
            return false;

        return typeToConvert.GetGenericTypeDefinition() == typeof(DataCoreRef<>);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(DataCoreRefJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
/// JSON converter for DataCoreRef
/// </summary>
public class DataCoreRefJsonConverter<T> : JsonConverter<DataCoreRef<T>>
    where T : class, IDataCoreTypedReadable
{
    public override DataCoreRef<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotSupportedException("Deserializing DataCoreRef is not supported. Use DataCoreTypedReader to read from the database.");
    }

    public override void Write(Utf8JsonWriter writer, DataCoreRef<T>? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.IsExternalFile)
        {
            // External file reference - write as a reference object
            writer.WriteStartObject();
            writer.WriteString("$ref", value.ExternalFilePath);
            if (value.RecordId != default)
                writer.WriteString("RecordId", value.RecordId.ToString());
            writer.WriteEndObject();
        }
        else
        {
            // Internal reference - resolve and write the value
            var resolved = value.Value;
            if (resolved == null)
            {
                // Could be a circular reference or unresolvable
                writer.WriteStartObject();
                writer.WriteString("$circularRef", $"({value.StructIndex}, {value.InstanceIndex})");
                writer.WriteEndObject();
            }
            else
            {
                JsonSerializer.Serialize(writer, resolved, resolved.GetType(), options);
            }
        }
    }
}
