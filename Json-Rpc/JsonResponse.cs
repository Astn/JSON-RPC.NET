
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AustinHarris.JsonRpc
{
    /// <summary>
    /// Represents a Json Rpc Response
    /// </summary>

    [JsonConverter(typeof(JsonResponseConverter))]
    public class JsonResponse
    {
        public string Jsonrpc { get; set; } = "2.0";

        public object Result { get; set; }

        public JsonRpcException Error { get; set; }

        public JsonElement Id { get; set; }
    }

    /// <summary>
    /// Represents a Json Rpc Response
    /// </summary>
    public class JsonResponse<T>
    {
        public string Jsonrpc { get; set; } = "2.0";

        public T Result { get; set; }

        public JsonRpcException Error { get; set; }

        public JsonElement Id { get; set; }
    }

    public class JsonResponseConverter : JsonConverter<JsonResponse>
    {
        public override JsonResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var jres = new JsonResponse();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var propname = reader.GetString();
                    if (propname == "jsonrpc")
                    {
                        reader.Read();
                        jres.Jsonrpc = reader.GetString();
                    }

                    if (propname == "result")
                    {
                        reader.Read();
                        jres.Result = JsonDocument.ParseValue(ref reader);
                    }
                    
                    if (propname == "id")
                    {
                        reader.Read();
                        jres.Id = JsonDocument.ParseValue(ref reader).RootElement.Clone();
                    }
                    
                    if (propname == "error")
                    {
                        reader.Read();
                        if (reader.TokenType == JsonTokenType.Null || reader.TokenType == JsonTokenType.None)
                        {
                            
                        }
                        else
                        {
                            JsonRpcExceptionConverter exc = new JsonRpcExceptionConverter();
                            jres.Error = exc.Read(ref reader, typeof(JsonRpcException), options);
                        }
                    }
                }
                
            }

            return jres;
        }

        public override void Write(Utf8JsonWriter writer, JsonResponse value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            if (value.Error != null)
            {
                writer.WritePropertyName("error");
                var spError =JsonSerializer.SerializeToUtf8Bytes(value.Error, options);
                using (JsonDocument document = JsonDocument.Parse(spError))
                {
                    document.RootElement.WriteTo(writer); 
                }
            }
            else
            {
                
                var spResult =JsonSerializer.SerializeToUtf8Bytes(value.Result, options);
                using (JsonDocument document = JsonDocument.Parse(spResult))
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Object
                        || document.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        writer.WritePropertyName("result");
                        document.RootElement.WriteTo(writer);    
                    }else if (document.RootElement.ValueKind == JsonValueKind.Null)
                    {
                        writer.WriteNull("result");
                    }
                    else
                    {
                        writer.WritePropertyName("result");
                        document.RootElement.Clone().WriteTo(writer);   
                    }
                }
            }
            if (value.Id.ValueKind != JsonValueKind.Null && value.Id.ValueKind != JsonValueKind.Undefined)
            {
                writer.WritePropertyName("id");
                value.Id.Clone().WriteTo(writer);
            }
            writer.WriteEndObject();
            
            writer.Flush();
        }
    }
}
