# Contributing to RhinoSpatial

RhinoSpatial helps people assemble maps, terrain, imagery and geometry in one aligned Rhino study. Contributions are most useful when they improve a real workflow: a reproducible source problem, clearer output, a performance issue, better documentation or a practical example.

## Report a problem

Open a [bug report](https://github.com/PascalNun/RhinoSpatial/issues/new/choose) and describe what you expected and what happened. Include:

- the RhinoSpatial and Rhino versions, operating system and affected component;
- the exact `Status` message and steps to reproduce;
- the selected area and coordinate system, if known;
- a public URL or small shareable file that demonstrates the problem.

Keep API keys, authentication links and private project data out of reports and example definitions. A small reproducible case is more helpful than a large unrelated project.

## Propose a change

Read the [project scope](docs/PROJECT_SCOPE.md) and [roadmap](docs/ROADMAP.md). For substantial changes, explain the design task and proposed behavior before expanding the interface. Keep changes focused and preserve component names, GUIDs, parameter layouts and existing definitions.

Reusable readers, source clients and coordinate logic belong in `RhinoSpatial.Core`. Grasshopper orchestration and Rhino geometry belong in `RhinoSpatial`. All aligned sources use the shared Spatial Context. Missing coverage, unsupported coordinates and source failures should produce useful diagnostics.

## Write documentation for the user

Documentation is part of a change, not a separate finishing task. Review the relevant overview, workflow, component reference, examples, source catalogue, validation notes and changelog whenever behavior changes. Update package-facing copy when release or installation guidance changes.

Write for architects and designers who need enough technical detail to choose and use their data:

- Lead with the task and the resulting output, then explain how to connect it.
- Use plain English and the actual component labels. Explain terms such as CRS, layer or COG when they first become useful.
- Keep general workflows applicable to different providers and places. Put exact URLs, coordinates and source quirks in linked examples or the test catalogue.
- Present the toolkit as a whole. Give each source category useful coverage; avoid making the latest fix, one city or one provider the organizing idea of the documentation.
- Describe current behavior in guides and references. Keep development history, individual bug reproductions and dated test results in the changelog and maintainer records.
- Keep documentation about the software and its design decisions. Leave task administration, permission reminders and temporary progress logs out of repository documentation.
- Keep request parameters, implementation details, budgets and probe commands in the technical reference or maintainer notes.
- Distinguish features in the public release from additions on `main`, and automated checks from results actually inspected in Rhino.
- Preserve technical facts, useful examples and license notices when making an introduction shorter.

Use [Working with project data](docs/WORKING_WITH_PROJECT_DATA.md) as the general source guide. Keep links and sample paths usable. Avoid adding another overlapping guide when an existing page can explain the workflow clearly.

## Build and validate

Use .NET SDKs that can build the solution's .NET 7 and .NET 10 targets:

```sh
dotnet build RhinoSpatial.sln -c Release
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- ogc-request-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- reference-source-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- tiles-selection-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- source-reader-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- terrain-mesh-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- correctness-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- reliability-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- lod2-atom-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- source-compatibility-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- medium-compatibility-check
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- low-compatibility-check
git diff --check
```

[CI](.github/workflows/validate.yml) runs the build and suites on macOS and Windows. Changes to geometry, preview, materials, UI or saved definitions also need the relevant [Rhino checks](examples/VALIDATION.md). Describe the checks actually performed and any remaining limits when submitting work.

For documentation-only changes, review wording, release labels, links, anchors and example paths. Do not present a documentation check as a runtime test.

RhinoSpatial uses the [MIT License](LICENSE). Keep [third-party notices](THIRD-PARTY-NOTICES.md) current when dependencies change.
