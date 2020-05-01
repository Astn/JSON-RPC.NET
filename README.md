
![Screenshot](http://i.imgur.com/rxHaXLb.png)

json-rpc.net
============
![Build Master](https://github.com/Astn/JSON-RPC.NET/workflows/Build%20Master/badge.svg) ![NuGet Badge](https://buildstats.info/nuget/AustinHarris.JsonRpc)

JSON-RPC.Net is a high performance Json-Rpc 2.0 server, leveraging the popular JSON.NET library. Host in ASP.NET, also supports sockets and pipes, oh my!

## Performance

These are results from running the TestServer_Console project.

##### Xeon E-2176M @ 2.70GHz 64.0 GB (Date: Thu Apr 30 17:34:22 2020 -0600)

```
Starting benchmark
processed      50 rpc in   137ms for     364.96 rpc/sec
processed     100 rpc in     0ms for          ∞ rpc/sec
processed     300 rpc in     1ms for 300,000.00 rpc/sec
processed   1,200 rpc in     7ms for 171,428.57 rpc/sec
processed   6,000 rpc in    26ms for 230,769.23 rpc/sec
processed  36,000 rpc in   166ms for 216,867.47 rpc/sec
processed 252,000 rpc in 1,121ms for 224,799.29 rpc/sec
Finished benchmark...
```

## Do you like this?

[![https://www.buymeacoffee.com/Ekati](https://cdn.buymeacoffee.com/buttons/default-blue.png)](https://www.buymeacoffee.com/Ekati)


##### Requirements
* dotnet-standard (dotnet core | mono | .net framework)

##### License
JSON-RPC.net is licensed under The MIT License (MIT), check the [LICENSE](https://github.com/CoiniumServ/JSON-RPC.NET/blob/master/LICENSE) file for details.

##### Installation

You can start using JSON-RPC.Net with our [nuget](https://www.nuget.org/packages/AustinHarris.JsonRpc/) package.

To install JSON-RPC.NET Core, run the following command in the Package Manager Console;

```
PM> Install-Package AustinHarris.JsonRpc
```

To install JSON-RPC.NET AspNet, run the following command in the Package Manager Console

```
PM> Install-Package AustinHarris.JsonRpc.AspNet
```






##### Getting Started & Documentation

Check our [documentation](https://github.com/Astn/JSON-RPC.NET/wiki).

##### Old Project Site

We have to github lately and host our [issues section](https://github.com/Astn/JSON-RPC.NET/issues) here, though you can still check the previous [issues](https://jsonrpc2.codeplex.com/workitem/list/basic) and [discussions](https://jsonrpc2.codeplex.com/discussions) over our old project site.
