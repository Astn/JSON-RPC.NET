# Public API surface

One text file per published version: every public and protected member of the four assemblies, sorted, with the
assembly identity (version and public key token) at the top of each section. The files are produced by
`.github/scripts/api_surface` against the packages on nuget.org and are diffed to review what a release changes:

```
dotnet run --project .github/scripts/api_surface -c Release -p:JsonRpcVersion=2.0.0-preview.3 -- docs/api-surface/2.0.0-preview.3.txt
diff docs/api-surface/2.0.0-preview.1.txt docs/api-surface/2.0.0-preview.3.txt
```

`2.0.0-preview.3.txt` is the surface frozen for 2.0.0: the final release must diff against it in the two
version lines of each section only. A later minor may add lines; removing or changing one is a major.
