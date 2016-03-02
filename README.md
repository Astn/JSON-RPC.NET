![Screenshot](http://i.imgur.com/rxHaXLb.png)

json-rpc.net
============
.Net [![astn-jsonrpc MyGet Build Status](https://www.myget.org/BuildSource/Badge/astn-jsonrpc?identifier=e8ccb637-ccd4-4940-b62c-bc1283cd9ddc)](https://www.myget.org/feed/Activity/astn-jsonrpc) Mono [![Build Status](https://travis-ci.org/Astn/JSON-RPC.NET.svg?branch=master)](https://travis-ci.org/Astn/JSON-RPC.NET)

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
PM> Install-Package AustinHarris.AspNet
```

##### Performance

Under ideal conditions > 120k rpc/sec (cpu i7-2600,console server, no IO bottleneck)

##### Getting Started & Documentation

Check our [documentation](https://github.com/Astn/JSON-RPC.NET/wiki).

##### Old Project Site

We have to github lately and host our [issues section](https://github.com/Astn/JSON-RPC.NET/issues) here, though you can still check the previous [issues](https://jsonrpc2.codeplex.com/workitem/list/basic) and [discussions](https://jsonrpc2.codeplex.com/discussions) over our old project site.
