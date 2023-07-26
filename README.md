# SafeHost

A demo of hosting a managed .NET runtime from a NativeAOT dll injection

## Running SafeHost

- Build: `dotnet publish SafeHost/SafeHost.csproj -r win-x64`
- Inject `Build/SafeHost.dll` into target process (preferrably using the builtin injector)