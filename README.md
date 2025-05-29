# <img src="assets/NSS-128x128.png" align="left" />Nefarius.Utilities.ETW

[![.NET](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml/badge.svg)](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml)
![Requirements](https://img.shields.io/badge/Requires-.NET%208%2F9-blue.svg)
![Windows only](https://img.shields.io/badge/Windows-8.0+-red)
[![NuGet Version](https://img.shields.io/nuget/v/Nefarius.Utilities.ETW)](https://www.nuget.org/packages/Nefarius.Utilities.ETW/)
[![NuGet](https://img.shields.io/nuget/dt/Nefarius.Utilities.ETW)](https://www.nuget.org/packages/Nefarius.Utilities.ETW/)

[ETW Log Files (.ETL)](https://learn.microsoft.com/en-us/windows-hardware/test/weg/instrumenting-your-code-with-etw) to
JSON parser/converter library.

> *This is a fork of the fantastic [`ETW2JSON`](https://github.com/microsoft/ETW2JSON) project by Microsoft and
contributors.*

## Changes of this fork

- Converted console tool into a reusable class library
- Replaced P/Invoke code with [source generators](https://github.com/microsoft/CsWin32)
- Changed namespace to `Nefarius.Utilities.ETW` to avoid conflicts with the origin library
- Added support for [WPP Software Tracing](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/wpp-software-tracing) decoding
  - Supports `.PDB` files as a decoding source
  - Supports `.TMF` files as a decoding source

## Known limitations

- Currently relies on **Windows-only** APIs so no support for other platforms
- Not all WPP extended format specification strings are implemented

## Documentation

[Link to API docs](docs/index.md).

## Sources & 3rd party credits

- [Microsoft/ETW2JSON](https://github.com/microsoft/ETW2JSON)
- [Microsoft/ETW](https://github.com/microsoft/ETW)
- [WPP Software Tracing](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/wpp-software-tracing)
- [microsoftarchive/bcl/Tools/ETW/traceEvent/SymbolEventParser.cs](https://github.com/microsoftarchive/bcl/blob/d646329371acaf696529a85e2aeb7c54639f9e70/Tools/ETW/traceEvent/SymbolEventParser.cs)
- [`enum _TDH_CONTEXT_TYPE`](https://github.com/cheolw00myung/cross-compile_for_Windows/blob/08935f0864f497ee7fc6f13aba1b598701a04be1/SDK10/include/um/tdh.h#L798-L816)
