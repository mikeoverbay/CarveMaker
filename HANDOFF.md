# CarveMaker handoff (updated 2026-10-10: tool library, pocket and profile, settings layout)

Written so any session (or person) can pick the project up without the chat
history. Read this, then `README.md` for the user-facing description.

## State of the repo

- Folder `C:\Text_to_CNC_path`, branch `master`, remote `github.com/mikeoverbay/CarveMaker`.
- Everything up to the handoff commit `e23cc0c` is pushed. Check `git status`
  and `git log origin/master..master` for anything newer; push with
  `git push origin master` only when the user asks.
  `Samples\00717 Mandala Floral Pattern` is a git-ignored stock-art test pack.
- Releases: GitHub Actions (`.github/workflows/release.yml`) builds Release on
  every push and publishes `CarveMaker-Setup-<ver>.exe` on tags `v*`. To ship:
  bump `<Version>` in `Directory.Build.props`, commit, `git tag v1.1.0`,
  `git push --tags`. v1.0.0 is published.

## Build and run

```
taskkill /IM CarveMaker.exe /F      (the running exe locks the build output)
dotnet build CarveMaker.vbproj -c Debug
bin\Debug\net8.0-windows\CarveMaker.exe
```

`dotnet build CarveMaker.sln -c Release` also builds the installer (project
`Setup`: self-contained publish + Inno Setup from NuGet) into the solution root.
Never put extra `.vb` files anywhere under the repo: the SDK globs them into
the app. Do not run `python` from Bash on this machine (Store alias hangs).

## Files and what they do

| File | Role |
|---|---|
| `Model.vb` | Pt2/Pt3, Toolpath/contours/moves, enums, `TextLine`, `DesignObject` (placed SVG), `CarveSettings` (grid categories: Text tab 1..3, Machine Settings 1..4, split in `SettingsView.vb`; `Clone`, `Validate`) |
| `GlyphOutline.vb` | Text to outlines: GDI+ `AddString` per line, cap-height sizing, justification across the blank, Clipper2 union |
| `VCarve.vb` | `TextToToolpath.Generate`, `RegionShape` (distance field), `VCarveEngine` (inset roughing, bevel at flat depth, floor clearing, medial-axis centerline finish), `ToolpathLinker` (ordering, links, gouge checks, time) |
| `GCodeWriter.vb` | FluidNC-style program: `G0 G53 Z0`, `G54`, `M8`, `S M3`, `G4 P`, first move `G0 X Y` then `G0 Z`, footer `G0 G53 Z0`, `M5`, `M9`, `M30` |
| `ToolpathView.vb` | OpenTK GLControl: layers, camera, object selection/drag/resize, simulation playback API (`SetToolpath`, `UpdateSimulationSettings`, `SimulationInfo`) |
| `CarveSimulation.vb` | GPU material removal: R32F heightmap FBO, instanced tool stamps with MAX blend every half cell, surface mesh (one vertex per cell up to the Display mesh budget, triangle strips, texel-centre sampling), board walls |
| `ToolModel.vb` | Revolved mesh of any library tool (`ToolDefinition.FullProfile`: flutes + one-diameter shank stub) |
| `ToolLibrary.vb` | `ToolType`, `ToolDefinition` (diameter, flute length, end radius, included angle; profile, validation, auto names), `ToolLibrary` (JSON in %AppData%\CarveMaker, standard inch tools), `InchFormat` (fractions), `ToolPickerEditor` (V-bit-only picker, no longer on any grid row; toolpath rows use `MachiningToolEditor` in Machining.vb) |
| `ToolDrawing.vb` | Scale side view of a tool with dimensions; tree icons |
| `Machining.vb` | `Machining` (per-item operation: V-carve / Pocket / Profile, tool, spindle speed, feed and plunge rate (`ApplyTo` copies them into a settings clone for the engines), depths, side, through cut, tabs; `VisibleProperties` picks the rows per operation; remembers the last V-bit and end mill across operation switches), `MachiningItem`, `MillingEngine` (pocket rings + residual clean-up, profile loops + tabs, `LinkPasses`), `JobBuilder.Generate` (V-carve, pockets, profiles in order, one `ToolpathSegment` per tool, `FinishStats`) |
| `frmToolLibrary.vb` / `.Designer.vb` | Tool Library dialog: manage mode (Toolpath > Tool Library, Ctrl+L) and pick mode (filter: V-carving needs a V-bit) |
| `SvgImport.vb` | SVG to Clipper polygons (inches, Y up): paths/shapes, groups, nested svg, use/symbol, CSS style sheets, preserveAspectRatio, strokes with caps/joins, fill rules |
| `ProjectFile.vb` | `.prj` JSON (settings, lines, drawings), version check |
| `SettingsView.vb` | `FilteredView` (ICustomTypeDescriptor: a chosen subset of an object's properties plus its `Machining` rows flat in one category; `GetPropertyOwner` returns the real owner of each row, `Machining.FromContext` resolves it for editors), `SettingsView` (Text tab / Machine Settings) and `DrawingView` (Drawings tab) and `SettingsGroups.TextPropertyNames` (what the Text tab shows; everything else goes to the Machine Settings window) |
| `frmMachineSettings.vb` / `.Designer.vb` | Machine Settings tool window (Machine > Machine Settings, Ctrl+K), owned by the main form, hides on close |
| `frmMain.vb` / `.Designer.vb` | UI: Text tab (editor + `pgText` text settings), Drawings tab, binds the Machine Settings window, menus, simulation bar, regenerate pipeline (`RequestRegenerate` -> 400 ms timer -> `GenerateAsync` on a worker) |
| `Setup\` | Installer project and `Setup.iss` |
| `Samples\` | Six sample SVGs shipped next to the exe |

## Recent work (this session)

1. **SVG importer hardened** against 126 torture files (cyclic `<use>`, symbols,
   CSS classes, preserveAspectRatio, stroke units/caps/joins, non-uniform
   transforms, huge coordinates). Root cause of one bug worth remembering:
   `StyleState` is a VB `Structure`, so "unset" doubles were 0, not NaN; a
   miter limit of 0 made Clipper square every corner. `StyleState.Initial` fixes it.
2. **Simulation Precision had no visible effect.** The heightmap was rebuilt
   correctly, but the drawn mesh was capped at 1000 cells per side and the
   cell readout never reached the status bar. Now: mesh follows the heightmap,
   heightmap samples at texel centres, Precision and Display mesh apply
   instantly without regenerating the toolpath (also safe during an in-flight
   generation), and the simulation bar shows `W x H cells at 0.00xx" (MB)`.
3. **Display mesh** (Full 4 M / Half 1 M / Low 250 k cells) for weak GPUs.
6. **Settings layout.** The shared settings grid is gone. The Text tab has
   `pgText` under the editor (sizes and fonts, placement on the blank, text
   toolpath); the Machine Settings window (`_machineWindow.Grid`) has 1. Blank
   (now with stock thickness and margin, which drawings use too), 2. Carve (with
   curve tolerance), 3. Machine, 4. Simulation. Both grids raise `Settings_PropertyValueChanged`;
   `BindSettingsGrids` / `RefreshSettingsGrids` keep them on the current
   `_settings` (call after loading a project). Property names are unchanged, so
   .prj files are unaffected. The tool window forwards the main menu's shortcut
   keys (`ProcessCmdKey`), because menu shortcuts only fire while focus is in
   the window that owns the menu.
5. **Pocket and profile toolpaths.** `DesignObject.Machining` and
   `CarveSettings.TextMachining`; `JobBuilder.Generate` replaces the old
   single V-carve call (`TextToToolpath.Generate` still works and goes through
   it; `TextToToolpath.CarveShape` is the V-carve core). `Toolpath.Segments`
   records tool + move range; the simulation keeps one tool mesh per tool and
   `GCodeWriter.ToolGroups` / `Write(tp, s, text, group, i, n)` write one
   program per tool. Offsets add a 0.0003" gap (Clipper arc chords sit inside
   the true arc).
7. **Per-toolpath tool, speed and feeds.** `Machining` has Tool (V-carve too),
   SpindleRpm, FeedRate, PlungeRate; `Machining.ApplyTo(s)` makes a settings copy
   with that tool and feeds, which is what the engines read (`CarveTool`,
   `FeedRate`, `PlungeRate`, `SpindleRpm` on `CarveSettings` are hidden and only
   kept for that and for old projects; `ProjectFile.MigrateToolpaths` copies the
   old job-wide values into toolpaths that lack them). The Text and Drawings grids
   show the toolpath rows flat (`FilteredView` + `Machining.VisibleProperties`).
   V-carve items are grouped by tool + speed + feeds; segments carry SpindleRpm
   and the writer emits `S` changes inside a program.
4. **Tool library** (inch only). Six types from four numbers; one profile
   function drives the dialog drawing and the simulation tool.
   `CarveSettings.CarveTool` replaces the old diameter / angle / tip flat
   settings; `ToolDiameterIn` and `IncludedAngleDeg` are now read-only views
   that are still written to the .prj for older versions, and
   `ProjectFile.MigrateTool` turns pre-library projects into a V-bit. Tip
   flat is gone from the UI (the user asked for exactly four numbers);
   `TipFlatIn` returns 0 and the engine still supports a flat.

## Test harness (not in the repo)

Durable copy: `C:\Users\theco\.claude\projects\C--Text-to-CNC-path\harness\`
(`smoke\` project referencing `bin\Debug\net8.0-windows\CarveMaker.dll`, plus the
torture SVG folders and their expectation scripts). Build with
`dotnet build smoke.vbproj -c Debug` inside `smoke\`, then
`bin\Debug\net8.0-windows\smoke.exe <mode>`:

| Mode | What it checks |
|---|---|
| (none) | Generates toolpaths for several fonts and texts, prints stats and bad-rapid count |
| `svg <dir>` | Imports every SVG in a folder, prints elements/outers/holes/size/area; Samples folder gets exact checks |
| `miter <file>` | Dumps imported polygon points (used to debug stroke joins) |
| `envelope` | Rasterises ideal surface vs cut envelope: uncut/gouge limits (0.0005" / 0.001") |
| `project` | `.prj` save/load round trip and newer-version rejection |
| `order` | Cut order modes |
| `gui` | Drives the real form: types text, imports heart sample, drags/resizes, runs the simulation, saves screenshots |
| `simres` | Changes Precision and Display mesh through the real PropertyGrid path, reads heightmap/mesh sizes, pixel-diffs screenshots, times frames |
| `layout` | Settings layout: text grid inside the Text tab and hidden on the Drawings tab, Machine Settings categories, every setting in exactly one grid, edits from either grid regenerate and mark the project changed, window hides/reopens, project load rebinds both grids, screenshots |
| `mill` | Pocket and profile: wall gouge and floor coverage, island clearance, 90% stepover clean-up, profile offsets and direction, through cut + tabs, refused tools, job order and tool groups, per-tool G-code, project round trip, and the form + multi-tool simulation on screen. `mill nogui` skips the screen part |
| `tools` | Tool library: profiles and meshes of all standard tools, inch parsing, validation, settings/project migration, library file, the dialog (edits, type switch, save, pick mode) and the simulation tool, with screenshots. `tools nogui` skips the screen part |

The torture expectations (`svg-torture-transforms-expect.js`,
`fillrules-expect.js`) are independent of the importer; run them with `node`.

## Verified numbers (RTX 2070, 6 x 3 in blank)

- Heightmap Fine/Medium/Coarse: 2000x1000 / 1200x600 / 600x300 cells.
- Frame time at fit zoom with the full mesh: 3.6 / 4.1 / 1.6 ms.
- V-carve accuracy (envelope test): <= 0.0005" uncut, < 0.001" over-cut.

## Gotchas collected so far

- Clipper2: `Union(PathsD, FillRule)` silently uses precision 2, use the 4-arg
  form with precision 5; `RectD.top` is min Y; `InflatePaths` arcTolerance is
  in source units; `miterLimit <= 1` squares every corner.
- OpenTK/GL: `MakeCurrent()` in Paint; sampler uniforms need an Integer; never
  import `System.Drawing.Imaging` next to OpenGL4 (PixelFormat clash);
  `GL.Uniform3(loc, Vector3)` does not compile in VB.
- VB: a local named like a type (`joinType` vs `JoinType`) shadows the type;
  Structure doubles default to 0.
- GUI harness modes: clear `frmMain._dirty` before `f.Close()` or the save prompt blocks the run; run GUI modes one at a time (they share the desktop and SendKeys); never `taskkill smoke.exe` while a background harness run is going.
- Bash tool mangles backslashes in heredocs; write edit scripts with the Write
  tool and run them with `node`.
- PropertyGrid edits write straight into `_settings`; toolpath-relevant ones go
  through `RequestRegenerate`, simulation-only ones (`SimResolution`,
  `SimMeshDetail`) through `UpdateSimulationSettings` and must also be copied
  into `_lastSettings` / the in-flight snapshot (`CopySimulationOnlySettings`).

## Ideas not started (VCarve-Pro direction)

- Ramped or helical entry for end mills (plunges are straight today).
- V-carving inside a pocket (Z relative to the pocket floor).
- Default feeds and speeds stored per tool in the library (today a new toolpath
  starts at 16000 RPM, 40 / 15 in/min whatever the tool).
- Raised carving (pocket the blank around the letters), inlay (male/female).
- Shapes (rectangles, circles, stars), text on an arc, DXF import (the mandala
  pack has DXF too).
- Post-processor selection (GRBL, Mach, LinuxCNC flavours).
- Simulation: size the mesh from on-screen pixels with a `gl_VertexID` grid (no
  VBO/EBO rebuild on every regeneration; today ~20 ms at Fine per regen).
