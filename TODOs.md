## TODOs

- **Publish to nuget.org**, so `dotnet tool install -g HwinfoMcp` and `dnx HwinfoMcp` work.
  - a release job with [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
    (`NuGet/login`, no long-lived API key)
  - [RID-specific tool packages](https://learn.microsoft.com/en-us/dotnet/core/tools/rid-specific-tools) that carry
    the Native AOT builds for `win-x64` and `win-arm64`, plus a framework-dependent `any` package as fallback
  - the MCP server package type and `.mcp/server.json`, so nuget.org lists it as MCP server, see
    [Create a minimal MCP server and publish to NuGet](https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-mcp-server)
- **Sign the executables** with a code signing certificate or signing service, so Windows SmartScreen doesn't warn
  about them.
