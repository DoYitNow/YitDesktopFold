# Native Windows application

`native/` contains the production .NET 8 WPF application. Repository overview, safety model, build commands and installer instructions are maintained in the root [README](../README.md), [architecture notes](../docs/ARCHITECTURE.md), and [release guide](../docs/RELEASE.md).

Quick verification:

```powershell
dotnet build .\native\YitDesktopFold.Native.csproj -c Release
dotnet run --project .\native.tests\YitDesktopFold.Native.Tests.csproj -c Release
```

Do not implement classification by moving files out of the user or public Desktop directories. See the architecture document before changing catalog, visibility, drag/drop, Shell, startup, or persistence code.
