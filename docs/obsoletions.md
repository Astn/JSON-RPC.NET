# Obsoletions

These members are obsolete at warning level in 2.0.0 and stay at warning level through 2.x. They will be removed in 3.0. Follow the diagnostic link for the replacement before upgrading. To suppress one warning temporarily, use `#pragma warning disable JSONRPC0001` around the call (with the matching `#pragma warning restore JSONRPC0001`), or add `JSONRPC0001` to your project's `<NoWarn>` property. Replace the ID for the member you use.

## JSONRPC0001

`Config.SetBeforeProcessHandler` was obsoleted in 2.0.0. Use the session-specific pre-process setter:

```csharp
Config.SetPreProcessHandler(sessionId, handler);
```

The obsolete alias remains at warning level through 2.x and will be removed in 3.0.

## JSONRPC0002

`Handler.RegisterFuction` was obsoleted in 2.0.0. Bind the delegate through `ServiceBinder`:

```csharp
ServiceBinder.BindMethod(sessionId, name, implementation);
```

Unlike `RegisterFuction`, `BindMethod` throws if the name is already registered instead of replacing it. The obsolete method remains at warning level through 2.x and will be removed in 3.0.

## JSONRPC0003

`Handler.UnRegisterFunction` was obsoleted in 2.0.0. Unbind the method through `ServiceBinder`:

```csharp
ServiceBinder.UnbindMethod(sessionId, name);
```

The obsolete method remains at warning level through 2.x and will be removed in 3.0.

## Reserved ranges

`JSONRPC0xxx` is reserved for obsoletions and `JSONRPC1xxx` for generator diagnostics. Diagnostic IDs are never reused.
