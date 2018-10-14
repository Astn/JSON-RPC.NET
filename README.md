
![Screenshot](http://i.imgur.com/rxHaXLb.png)

json-rpc.net
============
.Net [![astn-jsonrpc MyGet Build Status](https://www.myget.org/BuildSource/Badge/astn-jsonrpc?identifier=fbc64a4a-f9a7-4306-87ba-de0bb9d23cb7)](https://www.myget.org/feed/Activity/astn-jsonrpc) Mono [![Build Status](https://travis-ci.org/Astn/JSON-RPC.NET.svg?branch=master)](https://travis-ci.org/Astn/JSON-RPC.NET)

JSON-RPC.Net is a high performance Json-Rpc 2.0 server, leveraging the popular JSON.NET library. Host in ASP.NET, also supports sockets and pipes, oh my!

##### Requirements
* dotnet 4.0 or mono

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

##### Performance

Under ideal conditions > 120k rpc/sec (cpu i7-2600, console test server)

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

###### Test machine

i7 920 @ 2.67 GHz
12.0 GB 



##### Getting Started & Documentation

Check our [documentation](https://github.com/Astn/JSON-RPC.NET/wiki).

##### Old Project Site

We have to github lately and host our [issues section](https://github.com/Astn/JSON-RPC.NET/issues) here, though you can still check the previous [issues](https://jsonrpc2.codeplex.com/workitem/list/basic) and [discussions](https://jsonrpc2.codeplex.com/discussions) over our old project site.
