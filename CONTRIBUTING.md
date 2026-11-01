# Contributing to RhinoSpatial

RhinoSpatial brings multiple geospatial layers into one aligned Rhino and Grasshopper study area. Contributions should strengthen that workflow through reproducible compatibility fixes, correctness, clear diagnostics, documentation and practical examples.

## Report a problem

Open a [bug report](https://github.com/PascalNun/RhinoSpatial/issues/new/choose). Include the RhinoSpatial version, Rhino version, operating system, component, selected area/SRS, exact status message and steps to reproduce. A small public source URL or non-private sample is especially helpful. Never publish API keys, authentication links or private project data.

## Propose a change

Check the [project scope](docs/PROJECT_SCOPE.md) and [roadmap](docs/ROADMAP.md). For substantial changes, describe the real workflow and proposed behavior in an issue first. Keep pull requests focused and preserve component names, GUIDs, input/output layouts and existing Grasshopper definitions.

Reusable source readers, clients and coordinate logic belong in `RhinoSpatial.Core`; Grasshopper orchestration and Rhino geometry belong in `RhinoSpatial`. All aligned sources use the shared Spatial Context. Unsupported coordinates, missing coverage and source failures should produce honest diagnostics.

## Build and validate

Install a .NET SDK able to build the solution's .NET 7 and .NET 10 targets, then run:

```bash
dotnet build RhinoSpatial.sln -c Release
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- ogc-request-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- reference-source-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- tiles-selection-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- source-reader-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- terrain-mesh-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- lod2-atom-check
git diff --check
```

Update the relevant documentation and [test-source catalogue](docs/TEST_SOURCES.md). Changes to Rhino geometry, preview, materials, UI or saved definitions also need the relevant [manual checks](examples/VALIDATION.md); console checks do not establish visual correctness. Explain the validation performed and its limits in the pull request.

RhinoSpatial uses the [MIT License](LICENSE). Keep third-party notices current when dependencies change.
