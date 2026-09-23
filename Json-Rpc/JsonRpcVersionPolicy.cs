namespace AustinHarris.JsonRpc
{
    /// <summary>
    /// How the server treats the <c>jsonrpc</c> member of an incoming request. JSON-RPC 2.0 says it MUST be
    /// exactly <c>"2.0"</c>, but many real clients (tool harnesses, hand-written fetch calls) omit it. Set the
    /// process default with <see cref="Config.VersionPolicy"/> and a per-session override with
    /// <see cref="Config.SetVersionPolicy(string, JsonRpcVersionPolicy?)"/>.
    /// </summary>
    public enum JsonRpcVersionPolicy : byte
    {
        /// <summary>
        /// The default. A missing member is accepted; when present it must be <c>"2.0"</c>, anything else is
        /// answered with <c>-32600 Invalid Request</c>. Sloppy clients work, clients speaking another version
        /// are told so.
        /// </summary>
        Lenient = 0,

        /// <summary>The member is never inspected: missing, <c>"1.0"</c>, a number, anything goes.</summary>
        Ignore,

        /// <summary>The specification: the member must be present and be <c>"2.0"</c>.</summary>
        Strict
    }
}
