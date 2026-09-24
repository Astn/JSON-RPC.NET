# AustinHarris.JsonRpc.SystemTextJson

System.Text.Json serializer for [JSON-RPC.Net](https://github.com/Astn/JSON-RPC.NET) 2.0. The core parses the
JSON-RPC envelope (method / params / id) itself and asks the serializer only to convert values: request
parameters arrive as the raw UTF-8 bytes of one JSON value and go straight into `JsonSerializer.Deserialize`
(no transcoding, no copies); results are written with a per-thread cached `Utf8JsonWriter` directly into the
response buffer.

## Install

```
dotnet add package AustinHarris.JsonRpc.SystemTextJson
```

Targets `netstandard2.0`, `netstandard2.1`, `net8.0` and `net10.0`; depends on System.Text.Json 10.0.3 and the
`AustinHarris.JsonRpc` core package.

## Use

Process-wide default:

```csharp
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.SystemTextJson;

Config.SetSerializer(new SystemTextJsonRpcSerializer());
// or with your own options
Config.SetSerializer(new SystemTextJsonRpcSerializer(options));
```

Per session:

```csharp
Config.SetSerializer(sessionId, new SystemTextJsonRpcSerializer(options));
```

Per call (overrides both the session and the process-wide default):

```csharp
var serializer = new SystemTextJsonRpcSerializer(options);
string response = JsonRpcProcessor.ProcessSync(sessionId, json, context, serializer);
// the byte-based overloads take the same trailing argument:
JsonRpcProcessor.Process(sessionId, requestBytes, outputWriter, context, serializer);
```

When each level is the right one is covered in
[docs/serializers.md](https://github.com/Astn/JSON-RPC.NET/blob/master/docs/serializers.md).

Create one instance and reuse it. The options it actually uses (`EffectiveOptions`) are read-only; the object you
passed is never changed.

## Default options

`new SystemTextJsonRpcSerializer()` uses `SystemTextJsonRpcSerializer.DefaultOptions`, a single immutable
`JsonSerializerOptions` that reproduces the wire conventions of the built-in and Json.NET serializers for the
envelope, primitives, dates and plain objects. The serializers are still not interchangeable for every request:
System.Text.Json refuses some coercions the other two accept (a JSON number sent for a `string` parameter, for
example), the CLR types each supports differ, and so does the object model handed to handlers. Test client-visible
requests and responses before switching.

| Setting | Value |
| --- | --- |
| `WriteIndented` | `false` (compact output) |
| `DefaultIgnoreCondition` | `Never` (nulls are written) |
| `PropertyNamingPolicy` | `null` (member names as declared, in declaration order) |
| `PropertyNameCaseInsensitive` | `true` |
| `IncludeFields` | `true` (public fields bind like properties) |
| `Encoder` | `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` (`+`, `<`, `>`, `&` and non-ASCII text are written as they are, not Unicode-escaped) |
| `NumberHandling` | `AllowReadingFromString` |
| `Converters` | the `JsonRpcConverters` below |

### Converters

Registered by `JsonRpcConverters.AddMissing(options)`; each is public so it can be used on its own.

| Converter | Writes | Reads |
| --- | --- | --- |
| `JsonRpcNumberConverterFactory` (double, float, decimal, and the 8 integer types) | whole float/double/decimal values with `.0` (`3.0`, `71.0`, `0.0`), otherwise shortest round-trip (`1.2345`, `3.14159`); NaN and the infinities as the quoted strings `"NaN"`, `"Infinity"`, `"-Infinity"`, as Json.NET and the built-in serializer write them | numbers, numeric strings, `true`/`false` as 1/0; fractional input for integer types is rounded to even |
| `JsonRpcBooleanConverter` | `true`/`false` | booleans, numbers (non-zero is true), `"true"`/`"false"`/numeric strings |
| `JsonRpcCharConverter` | a one-character string | a one-character string or a number (`98` reads as `'b'`) |
| `JsonRpcDateTimeConverter` | `yyyy-MM-ddTHH:mm:ss[.fffffff]K`, the fraction only when non-zero and with trailing zeros trimmed, exactly as Json.NET and the built-in serializer write it | any ISO-8601 text via `DateTime.Parse(..., InvariantCulture, RoundtripKind)`: an offset in the input yields a Local `DateTime` |
| `JsonRpcDateTimeOffsetConverter` | `yyyy-MM-ddTHH:mm:ss[.fffffff]zzz` | ISO-8601 text |

Nullable variants (`double?`, `DateTime?`, ...) are covered automatically by System.Text.Json's nullable
wrapping. Because these converters replace the built-in numeric ones, `JsonNumberHandling.WriteAsString` and
`AllowNamedFloatingPointLiterals` are not applied to the primitive numeric types.

## Supplying your own options

`new SystemTextJsonRpcSerializer(options)` honours the options as given (naming policy, encoder, extra
converters, `TypeInfoResolver`, ...). The only adjustment is the converter set:

- If `options.Converters` already contains every `JsonRpcConverters` type, the instance is used as-is (it is
  made read-only, as System.Text.Json would do on first use anyway).
- Otherwise the options are copied with `new JsonSerializerOptions(options)` and the missing converters are
  appended to the copy, which is then made read-only. The object you passed is never mutated, so it is safe to
  share with other code. The converters are appended *after* yours, so a converter you registered for the same
  type keeps precedence.

`Options` returns what you passed (null for the defaults); `EffectiveOptions` returns the instance actually used.
To start from the library defaults and tweak them:

```csharp
var options = SystemTextJsonRpcSerializer.CreateDefaultOptions();   // a mutable copy of DefaultOptions
options.Converters.Add(new JsonStringEnumConverter());
options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
Config.SetSerializer(new SystemTextJsonRpcSerializer(options));
```

If you build options from scratch, remember that the wire conventions above (`IncludeFields`,
`UnsafeRelaxedJsonEscaping`, `PropertyNameCaseInsensitive`) are then up to you; only the converters are added.

## Object model

Pre/post-process handlers receive `JsonRequest.Params` as a `JsonElement` (the result of deserializing the
params to `object`), and `Handler.Handle(JsonRequest)` accepts a `JsonElement` back.

A client value that cannot be converted to the parameter's type throws `JsonException`, which the core reports
as `-32602 Invalid params` with `data` naming the parameter and the expected type; the value sent is never echoed.
System.Text.Json is stricter than the other two serializers here: a JSON number sent for a `string` parameter is
refused. An unsupported CLR type or an internal serialization failure remains `-32603 Internal error`.
