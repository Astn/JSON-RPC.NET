using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace AustinHarris.JsonRpc.Newtonsoft
{
    /// <summary>
    /// Settings-based entry points for callers that used to pass a <see cref="JsonSerializerSettings"/> to
    /// <see cref="JsonRpcProcessor"/> in 1.x. Each distinct settings instance is turned into one
    /// <see cref="NewtonsoftJsonRpcSerializer"/> the first time it is seen and reused afterwards (the settings are
    /// snapshotted at that point, as <see cref="JsonSerializer.Create(JsonSerializerSettings)"/> does); null means
    /// Json.NET defaults.
    /// </summary>
    public static class NewtonsoftJsonRpc
    {
        private static readonly NewtonsoftJsonRpcSerializer Default = new NewtonsoftJsonRpcSerializer();
        private static readonly ConditionalWeakTable<JsonSerializerSettings, NewtonsoftJsonRpcSerializer> Cache =
            new ConditionalWeakTable<JsonSerializerSettings, NewtonsoftJsonRpcSerializer>();

        /// <summary>The serializer for <paramref name="settings"/> (cached per settings instance; null = defaults).</summary>
        public static NewtonsoftJsonRpcSerializer SerializerFor(JsonSerializerSettings settings)
        {
            if (settings == null) return Default;
            return Cache.GetValue(settings, s => new NewtonsoftJsonRpcSerializer(s));
        }

        /// <summary>Processes a request on the default session with Json.NET configured by <paramref name="settings"/>.</summary>
        public static string ProcessSync(string jsonRpc, object context, JsonSerializerSettings settings)
        {
            return JsonRpcProcessor.ProcessSync(Handler.DefaultSessionId(), jsonRpc, context, SerializerFor(settings));
        }

        /// <summary>Processes a request on <paramref name="sessionId"/> with Json.NET configured by <paramref name="settings"/>.</summary>
        public static string ProcessSync(string sessionId, string jsonRpc, object context, JsonSerializerSettings settings)
        {
            return JsonRpcProcessor.ProcessSync(sessionId, jsonRpc, context, SerializerFor(settings));
        }

        public static Task<string> Process(string jsonRpc, object context, JsonSerializerSettings settings)
        {
            return JsonRpcProcessor.Process(Handler.DefaultSessionId(), jsonRpc, context, SerializerFor(settings));
        }

        public static Task<string> Process(string sessionId, string jsonRpc, object context, JsonSerializerSettings settings)
        {
            return JsonRpcProcessor.Process(sessionId, jsonRpc, context, SerializerFor(settings));
        }

        public static void Process(JsonRpcStateAsync async, object context, JsonSerializerSettings settings)
        {
            JsonRpcProcessor.Process(Handler.DefaultSessionId(), async, context, SerializerFor(settings));
        }

        public static void Process(string sessionId, JsonRpcStateAsync async, object context, JsonSerializerSettings settings)
        {
            JsonRpcProcessor.Process(sessionId, async, context, SerializerFor(settings));
        }
    }
}
