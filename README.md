
![Screenshot](http://i.imgur.com/rxHaXLb.png)

json-rpc.net
============
![.github/workflows/buildtest.yml](https://github.com/Astn/JSON-RPC.NET/workflows/.github/workflows/buildtest.yml/badge.svg)

JSON-RPC.Net is a high performance Json-Rpc 2.0 server, leveraging the popular JSON.NET library. Host in ASP.NET, also supports sockets and pipes, oh my!

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

## Performance

These are results from running the TestServer_Console project.

##### Xeon E-2176M @ 2.70GHz 64.0 GB (Date: Thu Apr 30 17:34:22 2020 -0600)

```
Starting benchmark
processed 50 rpc in 137ms for 364.963503649635 rpc/sec
processed 100 rpc in 0ms for ∞ rpc/sec
processed 300 rpc in 1ms for 300000 rpc/sec
processed 1200 rpc in 7ms for 171428.57142857142 rpc/sec
processed 6000 rpc in 25ms for 240000 rpc/sec
processed 36000 rpc in 159ms for 226415.09433962265 rpc/sec
processed 252000 rpc in 1075ms for 234418.60465116278 rpc/sec
Finished benchmark...

```

##### Intel i7 920 @ 2.67GHz 12.0 GB (Date: Maybe in 2015?)
> 
```
Starting benchmark
processed        50 rpc in      0ms for       ∞ rpc/sec
processed       100 rpc in      2ms for  50,000 rpc/sec
processed       300 rpc in      1ms for 300,000 rpc/sec
processed     1,200 rpc in      6ms for 200,000 rpc/sec
processed     6,000 rpc in     37ms for 162,162 rpc/sec
processed    36,000 rpc in    228ms for 157,894 rpc/sec
processed   252,000 rpc in  1,688ms for 149,289 rpc/sec
processed 2,016,000 rpc in 13,930ms for 144,723 rpc/sec
Finished benchmark...
```





##### Getting Started & Documentation

Check our [documentation](https://github.com/Astn/JSON-RPC.NET/wiki).

##### Old Project Site

We have to github lately and host our [issues section](https://github.com/Astn/JSON-RPC.NET/issues) here, though you can still check the previous [issues](https://jsonrpc2.codeplex.com/workitem/list/basic) and [discussions](https://jsonrpc2.codeplex.com/discussions) over our old project site.
