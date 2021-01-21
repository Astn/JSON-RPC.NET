using System;
using System.Buffers;
using System.IO;
using System.Text.Json;

namespace AustinHarris.JsonRpc
{
    public static class JsonExtensions
    {
        // public static T ToObject<T>(this JsonElement element, JsonSerializerOptions options = null)
        // {
        //     var bufferWriter = new System.Buffers.ArrayBufferWriter<byte>();
        //     using (var writer = new Utf8JsonWriter(bufferWriter))
        //     {
        //         element.WriteTo(writer);
        //     }
        //
        //     return JsonSerializer.Deserialize<T>(bufferWriter.WrittenSpan, options);
        // }

        // public static T ToObject<T>(this JsonDocument document, JsonSerializerOptions options = null)
        // {
        //     if (document == null)
        //     {
        //         throw new ArgumentNullException(nameof(document));
        //     }
        //
        //     return document.RootElement.ToObject<T>(options);
        // }       
        //
        public static object ToObject(this JsonElement element, Type returnType, JsonSerializerOptions options = null)
        {
            //var bw = new ArrayBufferWriter<byte>();
            var ms = new MemoryStream();
            
            using (var w = new Utf8JsonWriter(ms))
            {
                element.WriteTo(w);
            }

            return JsonSerializer.Deserialize(ms.GetBuffer().AsSpan(0,(int)ms.Position), returnType, options);
        }
        //
        // public static object ToObject(this JsonDocument document, Type returnType, JsonSerializerOptions options = null)
        // {
        //     if (document == null)
        //     {
        //         throw new ArgumentNullException(nameof(document));
        //     }
        //
        //     return document.RootElement.ToObject(returnType, options);
        // }       
    }
}