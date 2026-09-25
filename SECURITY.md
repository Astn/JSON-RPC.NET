# Security

## Reporting a vulnerability

Report vulnerabilities privately through GitHub's private vulnerability reporting for this repository:
[github.com/Astn/JSON-RPC.NET/security/advisories/new](https://github.com/Astn/JSON-RPC.NET/security/advisories/new).
Do not open a public issue for a vulnerability.

Reports are read and handled by the maintainer as time allows. There is no guaranteed response time and no bug
bounty. A fix, when one is needed, ships as a new version of the affected package with the advisory published
alongside it.

## Supported versions

| Version | Supported |
| --- | --- |
| 2.x | yes, on the tested targets (`net8.0` and `net10.0`; the `netstandard` assets are untested) |
| 1.x | no: no further 1.x releases are planned |

## What the library does and does not do

The README's [Security](README.md#security) section lists what the library does by default (exception
redaction, nesting limit, request-size limit on the Kestrel host) and what it leaves to the host: authentication,
authorisation, transport security, rate limiting and deadlines.
