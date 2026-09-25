# `AustinHarris.JsonRpc.Newtonsoft`

`AustinHarris.JsonRpc.Newtonsoft` adds Json.NET serialization to
[`AustinHarris.JsonRpc`](https://www.nuget.org/packages/AustinHarris.JsonRpc) 2.0.
Choose it when your models rely on Json.NET converters, contract resolvers or `[JsonProperty]`,
or when clients send non-strict JSON.

Configure parameter and result conversion with `JsonSerializerSettings`.

## Install

```sh
dotnet add package AustinHarris.JsonRpc.Newtonsoft --prerelease
```

Targets `netstandard2.0`, `netstandard2.1`, `net8.0` and `net10.0`; depends on Newtonsoft.Json 13.0.4 and the
`AustinHarris.JsonRpc` core package.

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
Config.SetSerializer("session-42", new NewtonsoftJsonRpcSerializer(settings));
```

Per call (overrides both):

```csharp
var serializer = new NewtonsoftJsonRpcSerializer(settings);
string response = JsonRpcProcessor.ProcessSync(sessionId, json, context, serializer);
```

When each level is the right one is covered in
[docs/serializers.md](https://github.com/Astn/JSON-RPC.NET/blob/master/docs/serializers.md).

Create the serializer once and reuse it: it holds one `JsonSerializer` built from the settings, and the processor's
synchronous path keeps an envelope reader in per-thread scratch storage for as long as the serializer instance
stays the same, so a new serializer per call throws that reuse away. That `JsonSerializer`, its converters,
contract resolver and callbacks are used concurrently by unrelated requests: configure the instance before serving
traffic, do not mutate it while requests are in flight, and make custom components thread-safe.

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
  values, ISO-8601 dates (fraction only when non-zero, trailing zeros trimmed; `Z` for UTC, the offset for Local,
  nothing for Unspecified), `char` as a one-character string, nulls written, members in declaration order,
  case-insensitive member names on input, numbers coerced to `bool`/`char`/floating types.
* Leniency. Json.NET accepts more than RFC 8259, and with this serializer selected so does the envelope reader:
  single-quoted strings, unquoted member names and trailing commas are accepted in the request, e.g.
  `{method:'add',params:[1,2],id:1}`. With the built-in serializer the same request is a `-32700` parse error.
  Leniency applies to the HTTP endpoint and to in-process calls; on a raw Kestrel connection the framer that
  splits the stream into documents accepts strict JSON only.
* `JsonConvert.DefaultSettings`, if your process sets it, is the baseline exactly as it is for `JsonConvert`.

A value Json.NET cannot convert to the parameter's type (a `JsonException`, or a format, overflow or cast
exception while reading an argument) is reported as `-32602 Invalid params`, with `data` naming the parameter and
the expected type; the value sent is never echoed. A type the serializer cannot handle at all, or an exception
inside your method, is `-32603 Internal error`.

## Performance notes

Reading decodes the value's UTF-8 bytes once into a pooled `char[]` and hands that to a `JsonTextReader` whose
own buffer is rented from `ArrayPool<char>`; no `string` or `MemoryStream` is created. Writing keeps one
`JsonTextWriter` per thread over a `TextWriter` that UTF-8 encodes straight into the caller's
`IBufferWriter<byte>` (a `PipeWriter`, the HTTP body writer, or the processor's pooled buffer), so the JSON is
never assembled as a string first.
