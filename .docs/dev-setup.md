# Dev machine setup and headless Core tests

How to get a new Windows machine ready for Unruled, and how to build and test `Unruled.Core`
without the Unity Editor. First written 2026-10-19 while setting up a second machine.

## 1. Prerequisites

| Need | Version / path | Check |
|---|---|---|
| Unity Editor | `6000.5.7f1` (see `ProjectSettings/ProjectVersion.txt`) | `C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe` |
| git + git-lfs | any recent | `git lfs version` |
| Python | 3.14 via the `py` launcher (the bare `python` alias may point at the Store stub) | `py --version` |
| .NET SDK | 10.x (headless Core tests only) | `dotnet --list-sdks` |
| LDPlayer | `D:\LDPlayer\LDPlayer14` (reference game comparison only) | `D:\LDPlayer\LDPlayer14\adb.exe devices` |

Python tools need their pinned packages:

```powershell
py -m pip install -r tools\requirements.txt
```

## 2. First open in Unity

1. Clone, then `git pull --ff-only` on `main`.
2. Open `D:\GameProjects\unruled` in Unity 6000.5.7f1. The first import builds `Library/` and takes a while.
3. `Packages/com.kitwright.unity.mcp` is embedded (patched for 6000.5, see roadmap Exceptions). Once the
   Editor is up, KitWright writes `.mcp.json` (gitignored). Restart Claude Code so it picks up the Unity MCP.
4. Check the agent rule files: `py .agents\sync-rules.py --check` should report everything in sync.

Path or tool differences on one machine go in a local `.env` (gitignored); it overrides `project.config`.

## 3. Headless Core tests (no Unity)

`Unruled.Core` has `noEngineReferences: true`, so it and its EditMode tests compile with plain .NET.
Use this when the Editor is closed, busy importing, or on a machine without Unity. It does **not** cover
`Unruled.View`, `Unruled.Editor`, IL2CPP/AOT behaviour or Play Mode; still run `request_recompile` and
the Unity test runner before calling a task done.

The harness lives in `.temp/coretest/` (not committed). To recreate it, write these two files:

`.temp/coretest/CoreTests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <IsPackable>false</IsPackable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>CS0618;CS0162;CS8981</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../Assets/Scripts/Core/**/*.cs" />
    <Compile Include="../../Assets/Tests/Core/**/*.cs" />
    <Compile Include="UnityShim.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
  </ItemGroup>
</Project>
```

`.temp/coretest/UnityShim.cs`: the one test that touches Unity (`DataSheetConsistencyTests`) only
reads `Application.dataPath` to find `.docs/`.

```csharp
namespace UnityEngine
{
    public static class Application
    {
        public static string dataPath => System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Assets"));
    }
}
```

Run from the repo root:

```powershell
dotnet test .temp\coretest
dotnet test .temp\coretest --filter "FullyQualifiedName~GameSoakTests"   # one class
```

`LangVersion 9.0` matches what Unity compiles, so C# features Unity rejects also fail here.

## 4. Quick rule checks (no Unity)

```bash
grep -rln "UnityEngine" Assets/Scripts/Core                    # expect nothing
grep -rln "System.Linq" Assets/Scripts/Core                    # expect nothing
grep -rnE "\.Current\b|Find(Any)?Object" Assets/Scripts        # expect nothing new
```

## 5. State on 2026-10-19 (second machine)

- Pulled `bbfdf71..4ef7c68` (loans, nuclear strike, relays, research branches, ChaCha8, balance lock).
- Headless build: 0 warnings, 0 errors. Tests: 346 / 346 pass.
- Rule checks above: clean. `sync-rules.py --check`: 18 files in sync.
- Not yet verified on this machine: Unity compile of `Unruled.View`, Unity test runner, Play Mode.
- Only `GameSoakTests` (6 h, seed 1234) is committed; the 5-seed 30-day soaks in the change log were
  run ad hoc and are not in the repo.
