# Hello Foundry sample

This is the smallest buildable Creators Forge Foundry project. It declares one
C# source file and produces a .NET Framework 4.8.1 managed library, a
`CPHInline` bridge, and a verified stable-v23 Streamer.bot import package.

From the repository root:

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  build .\samples\HelloFoundry\HelloFoundry.foundryproj
```

Expected output:

```text
Build succeeded: Hello Foundry 0.1.0
Managed assembly: build/managed/CreatorsForge.Samples.HelloFoundry.dll
CPHInline bridge: build/bridge/CPHInline.cs
Streamer.bot package: build/streamerbot/com.creatorsforge.samples.hello.streamerbot
Streamer.bot package report: build/streamerbot/package-report.json
Package IR: build/package-ir.json
```

The generated `build` directory is intentionally ignored by Git.
