# AustinHarris.JsonRpc.Newtonsoft

Json.NET (Newtonsoft.Json) serializer for [JSON-RPC.Net](https://github.com/Astn/JSON-RPC.NET) 2.0.

The core package (`AustinHarris.JsonRpc`) parses the JSON-RPC envelope itself and ships a small dependency-free
serializer for parameters and results. Install this package when you want Json.NET to do the value conversions:
its converters, contract resolvers, `[JsonProperty]` attributes, date/float handling, and its tolerance for
non-strict JSON.

```
dotnet add package AustinHarris.JsonRpc.Newtonsoft
```

## Choosing the serializer

Process-wide (every session that does not override it):

```csharp
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Newtonsoft;
using Newtonsoft.Json;

var settings = new JsonSerializerSettings { DateFormatString = "yyyy-MM-dd" };   // optional
Config.SetSerializer(new NewtonsoftJsonRpcSerializer(settings));
```

Per session:

```csharp
Handler.GetSessionHandler("session-42").Serializer = new NewtonsoftJsonRpcSerializer(settings);
// or: Config.SetSerializer("session-42", new NewtonsoftJsonRpcSerializer(settings));
```

Per call (overrides both):

```csharp
var serializer = new NewtonsoftJsonRpcSerializer(settings);
string response = JsonRpcProcessor.ProcessSync(sessionId, json, context, serializer);
```

Create the serializer once and reuse it: it holds one `JsonSerializer` built from the settings, and the processor
caches an envelope reader per serializer instance.

## Settings-based helpers (1.x compatibility)

The `JsonSerializerSettings` overloads that `JsonRpcProcessor` had in 1.x live here now:

```csharp
string response = NewtonsoftJsonRpc.ProcessSync(sessionId, json, context, settings);
Task<string> task  = NewtonsoftJsonRpc.Process(sessionId, json, context, settings);
NewtonsoftJsonRpc.Process(sessionId, stateAsync, context, settings);
```

Each distinct settings instance is turned into a serializer the first time it is seen and reused afterwards
(`NewtonsoftJsonRpc.SerializerFor(settings)` gives you that instance). `null` settings means Json.NET defaults.

## What you get

* Every conversion honours the settings: params, results, `error.data`, and the `JsonRequest.Params` handed to
  pre/post-process handlers (a `JObject` / `JArray` / primitive, as `JsonConvert.DeserializeObject` returns).
* Json.NET's defaults already match the library's wire conventions: compact output, `3.0` for whole floating
  values, ISO-8601 dates (fraction only when non-zero, trailing zeros trimmed) with the offset, `char` as a one-character string, nulls
  written, members in declaration order, case-insensitive member names on input, numbers coerced to
  `bool`/`char`/floating types.
* Leniency. Json.NET accepts more than RFC 8259, and with this serializer selected so does the envelope reader:
  single-quoted strings, unquoted member names and trailing commas are accepted in the request, e.g.
  `{method:'add',params:[1,2],id:1}`. With the built-in serializer the same request is a `-32700` parse error.
* `JsonConvert.DefaultSettings`, if your process sets it, is the baseline exactly as it is for `JsonConvert`.

Conversion failures throw and are reported to the client as `-32603 Internal Error`.

## Performance notes

Reading decodes the value's UTF-8 bytes once into a pooled `char[]` and hands that to a `JsonTextReader` whose
own buffer is rented from `ArrayPool<char>`; no `string` or `MemoryStream` is created. Writing keeps one
`JsonTextWriter` per thread over a `TextWriter` that UTF-8 encodes straight into the caller's
`IBufferWriter<byte>` (a `PipeWriter`, the HTTP body writer, or the processor's pooled buffer), so the JSON is
never assembled as a string first.
