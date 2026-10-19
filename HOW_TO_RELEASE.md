## How to create a release

To release, set `<VersionPrefix>` in `HwinfoMcp/HwinfoMcp.csproj` to the new version and push a tag like `v1.2.3`;
the workflow refuses a tag that doesn't match. It publishes a release with `HwinfoMcp-win-x64.exe`,
`HwinfoMcp-win-arm64.exe`, the tool package `HwinfoMcp.<version>.nupkg` and `SHA256SUMS`, with build provenance
attestations for the executables and the package. A tag like `v1.2.3-beta.1` publishes a pre-release.
