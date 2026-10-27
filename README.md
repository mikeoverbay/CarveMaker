# CarveMaker

Windows desktop app (VB.NET, .NET 8, WinForms) that turns typed text in any
installed font into a **V-carve toolpath** for a V-bit (default 1/4" diameter,
90 degree) and writes G-code. The toolpath is shown live in an OpenGL view.

```
+------------------------------+----------------------------------------+
| [Text] [Drawings]            | [draw tools] [font B I align] [snap]   |
|                              |                     +----------------+ |
| Size: Large Medium Small     |   OpenGL toolpath   |Machine Settings| |
| Align: Left Center Right     |   view (pan: left   | (Ctrl+K, tool  | |
| [ rich text editor ]         |   drag, orbit:      |  window: blank,| |
|------------------------------|   right drag, zoom: |  tool, carve,  | |
| [ text settings: sizes,      |   wheel, double-    |  machine,      | |
|   fonts, placement, text     |   click: fit)       |  simulation)   | |
|   toolpath ]                 |                     +----------------+ |
+------------------------------+----------------------------------------+
```

## User guide

**Help > User Guide** (F1) opens the guide (`Help/CarveMaker Help.html`, also in
the Start menu): getting started, text, drawing, toolbars, toolpaths, the tool
library, machine settings, the simulation, and every shortcut.

## Download

Ready-made installers are on the
[Releases page](https://github.com/mikeoverbay/CarveMaker/releases): download
`CarveMaker-Setup-<version>.exe` and run it. Nothing else is needed; the
installer carries its own .NET runtime. (The exe is unsigned, so Windows
SmartScreen may ask you to confirm the first time.)

## Using it

1. Type the text in the editor. Each line can have one of three letter sizes
   (**Large / Medium / Small**, inch values editable in the settings under the
   editor, *1. Sizes and fonts*) and its own
   alignment (**Left / Center / Right**). Formatting applies to whole lines.
2. Each size has its own font: expand *Size 1 - Large font* (and Medium, Small)
   in the text settings to pick family, bold and italic, or put the caret on a
   line and use **Font > Choose Font for Current Line's Size...** (Ctrl+F).
3. The Text tab holds only text settings: sizes and fonts, placement on the
   blank, and the text's toolpath (operation, tool, spindle speed, feed and
   plunge rate). Each drawing has the same toolpath rows on the Drawings tab.
   Everything else, including the blank (stock), V-carve depths, machine and
   simulation settings, is in the **Machine Settings** window. Open it with **Machine > Machine Settings...** (Ctrl+K). It
   floats over the main window and stays open while you work. Closing it only
   hides it. The toolpath regenerates automatically after every change (or press
   **F5**).
4. **File > Save Project** (Ctrl+S) stores everything, settings and formatted
   text, in a `.prj` file (readable JSON); **Open Project** (Ctrl+O) brings it
   back and **New Project** (Ctrl+N) starts over. The title bar shows a `*`
   while there are unsaved changes. **File > Export G-code...** (Ctrl+G) writes
   the program (`.nc`); **Import Text** reads a `.txt` or `.rtf` into the editor.

Blank (*Machine Settings > 1. Blank*): give the stock **width X**, **height Y**,
**thickness** and **margin**, and the machine coordinates of its lower-left corner (*Blank
origin*; use `-width/2, -height/2` if you zero at the centre). Placement (Text
tab, *2. Placement on the blank*): each line is
justified across the blank: **Left** starts at the left margin, **Center** is
centred on the blank, **Right** ends at the right margin. The whole block is
placed **Top / Middle / Bottom** between the margins, and *Text offset X/Y*
nudges it. The blank outline is drawn in tan in the view and the status bar
warns when text runs off it. **Z0 is the top of the stock**. Units are inches
internally; G-code can be written in inches (G20) or millimetres (G21).

## How the toolpath is made

For a V-bit with half angle *a* and tip flat *f* (0 for the sharp bits in the tool
library), a tip at depth *z* cuts a cone
of surface radius `f/2 + z*tan(a)`. The ideal carved surface is therefore the
distance field of the letter outline, clamped at the flat depth *F*.

1. **Outlines** â€“ GDI+ glyph outlines (`GraphicsPath.AddString`, typographic
   layout, kerning) flattened to the curve tolerance, then a Clipper2 non-zero
   union. This normalises orientation for TrueType and CFF fonts and merges
   overlapping glyphs (script fonts).
2. **Roughing** â€“ inward offset contours (Clipper2 `InflatePaths`) every
   *Roughing depth step*, commanded slightly shallow (*Roughing allowance*) so
   they never touch the finished bevel.
3. **Bevel finish at the flat depth** â€“ the exact contour at the inset where the
   V reaches *Flat depth*, cut at `Z = -F`.
4. **Floor clearing** â€“ further insets at the *Floor stepover*, all at `Z = -F`,
   for strokes wider than the V can reach (scallop height about half the stepover).
5. **Centerline finishing** â€“ the medial axis of each letter traced with maximal
   inscribed circles (the F-Engrave / Vectric method): the tip follows the
   middle of every stroke with `Z = -(r - f/2)/tan(a)`, cutting the ridge, both
   flanks and lifting into sharp corners as the stroke narrows.
6. **Linking** â€“ letters are machined one at a time; inside a letter each lobe
   is finished before the next, links are straight 3D feed moves when a gouge
   check against the distance field allows it, otherwise a short lift to
   *Clearance Z*. Letters are separated by moves at *Safe Z*.

A geometric regression test (see `scratch` harness used during development)
rasterises the ideal surface and the cut envelope: with the finishing pass on,
test letters in Arial, Times, Segoe Script and Gabriola show at most 0.0005"
uncut material and under 0.001" over-cut.

## SVG drawings

**Drawings** tab, or **File > Import SVG Drawing...**: logos, clipart and
shapes are carved with the same engine as the text. The importer handles
`<path>` (every command, relative and absolute, arcs), `rect` / `circle` /
`ellipse` / `line` / `polyline` / `polygon`, groups and nested `<svg>` with
transforms, `<use>` of shapes, groups and `<symbol>`s, `<switch>`, `viewBox`,
`preserveAspectRatio` and units (mm, cm, in, pt, px), presentation attributes,
inline styles and simple `<style>` sheets (tag, `.class`, `#id` selectors),
`display` / `visibility`, `fill-rule` (nonzero / evenodd) per element, and
stroke-only line art, which becomes an outline as wide as the stroke with the
file's caps and joins (butt / round / square, miter / round / bevel, miter
limit). `<text>` is skipped with a warning: convert text to paths in your
drawing program first. Clip paths, masks, gradients and opacity are ignored:
every painted shape is carved. Overlapping shapes merge.

Each imported drawing is an object with X/Y, width/height (aspect locked by
default), rotation, mirror and visibility, editable in the tab or directly in
the view (see *Drawing on the blank* below for the handles). The text block can
be Shift+dragged the same way (it updates *Text offset X/Y*). Drawings are embedded in
the `.prj` file.

Sample files live in the `Samples` solution folder (basic shapes, Bézier
curves, Inkscape-style groups and `<use>`, stroke line art, a mm plate without
viewBox, and a path syntax torture test). They are copied next to the exe and
into the installer, and the Import SVG dialog opens there the first time.

## Drawing on the blank

The toolbars over the view and the **Draw** menu draw shapes and text straight
onto the blank. Each one becomes a drawing in the Drawings list with its own
toolpath (V-carve, pocket or profile), just like an imported SVG.

| Tool | Key | How |
|---|---|---|
| Select | V, Esc | Shift+click to pick, Shift+drag to move; a plain drag pans the view |
| Rectangle | R | drag a box; *Corner radius* rounds it |
| Ellipse | E | drag a box |
| Polygon | P | drag a box; *Sides* (3 to 64) |
| Star | S | drag a box; *Points* and *Inner radius (%)* |
| Free-form area | A | click the corners (three or more); click the first corner, double-click or press Enter to close; Backspace or Delete removes the last corner |
| Text | T | drag a box as tall as a capital letter (or click), then type |

While dragging out a shape, **Shift** keeps its own proportions (square,
circle, regular polygon) and **Ctrl** draws from the centre; **Esc** cancels. A
plain click makes a 1" shape. The keys work when the view has the focus.

**Shift works on drawings; a plain drag moves the view.** With the select tool
a plain left drag always pans, even over a drawing, so panning never moves
anything by accident. Hold **Shift** when pressing the button to select
(Shift+click), move (Shift+drag) or use the handles; Shift+click on empty space
or **Esc** deselects. A plain click on a drawing only shows this hint in the
status bar. Double-clicking a text drawing still opens it for typing.

**Handles** of the selected drawing (Shift+drag them): the corner squares scale
it evenly about the opposite corner, the side squares stretch it one way (shown
when *Lock aspect ratio* is off; drawn shapes start unlocked), and the round
knob above the top edge rotates it about its centre in steps of the rotation
snap (**Alt** turns it freely). **Draw > Scale** sets an exact size or
percentage.
Arrow keys nudge by 0.01" (0.1" with Shift), **Delete** removes the drawing.
**Draw** also has Rotate 90 degrees left/right, Mirror, Bring to Front and Send
to Back, **Hide** (Ctrl+H: not shown, picked or carved) and **Show All**
(Ctrl+Shift+H); both are also on the *View* toolbar.

**Toolbars**: *Draw Tools*, *Text Style*, *Snap* and *View* start side by side over the
view. Drag one by its grip to any edge of the view (top, bottom, left or right,
in rows), or off the view to float it in its own small window (double-click
that window's title to dock it again). Right-click a toolbar to dock it on an
edge, float it or hide it; **View > Toolbars** shows hidden ones again and
*Reset Toolbars* puts them all back along the top. The arrangement is
remembered for the next start.

**Snapping** (Snap toolbar, or **Draw > Snap to Grid / Grid Spacing /
Rotation Snap**):

- *Snap to grid* (off by default; picking a spacing turns it on): 1", 1/2",
  1/4", 1/8", 1/16", 1/32", 0.1" or 0.05". A finer grid is drawn at that
  spacing. Moving a drawing (or the main text) lines up whichever of its box
  corners or centre is nearest a grid line, separately in X and Y; a side
  handle puts that side on the grid, a corner handle scales until the corner
  reaches a grid line; new shapes, text starting points and area corners land
  on grid points. Hold **Alt** while dragging to place something freely.
- *Rotation snap* (15 degrees by default; Off, 1, 5, 15, 30, 45 or 90): the knob
  turns in those steps, and close to the centre it holds still instead of
  spinning. Hold **Alt** to turn freely.

Both are remembered for the next start (`%AppData%\CarveMaker\Preferences.json`);
they are not part of the project.

**Text drawings**: after drawing the box an editor opens under the text: type
(Enter starts a new line), then **Ctrl+Enter** or click elsewhere; **Esc**
cancels. Double-click a text drawing (or **Draw > Edit Text**) to change it.
Its size is the *Letter height* (height of a capital letter): Shift+dragging a corner
of the text resizes the lettering and updates the letter height. The **Style**
menu and the toolbar set the font, **bold**, *italic* and the alignment of the
lines; with no text selected they set the style for the next text. Text
drawings are separate from the main text on the Text tab.

**Picking**: Shift+clicks are resolved on the GPU from an offscreen ID buffer in
which every drawing is its (rotated) box, so a click anywhere in a drawing's box
takes it, and edges are caught within 4 pixels. Where boxes overlap, the
smallest one under the mouse wins (text inside a frame picks the text). The selected drawing keeps the click wherever
it is under the mouse, so it can be moved, scaled or rotated where others
overlap it; **Shift+Ctrl+click** steps to the next drawing under the mouse. The drawing under the mouse
lights up, outline and box, before any click, so it is clear what a Shift+click will take. Without a working OpenGL view picking falls back
to bounding boxes.

A project file also stores each drawn shape's outline as SVG. Older CarveMaker
versions, which only know imported drawings, carve that outline; and a text
drawing opened on a PC without its font carves the stored outline too (the
status bar says so) until its words, font or style are changed. A drawing
that cannot be carved at all is named in the status bar.

## 3D models

**File > Import 3D Model / Depth Image** (Ctrl+Shift+I) or **Import 3D** on the
Drawings tab reads STL (binary or ASCII), OBJ, PLY (ASCII, binary little and big
endian), 3MF (units, components and build transforms) and OFF models, and
grayscale depth images (light is high). The model is kept as a **relief**: its
surface seen from one side, as a heightmap embedded in the project (about 12 MB
for a 4096 x 4096 heightmap). A 3-axis router reaches only what it sees from
above, so overhangs are filled in.

- **Carved side** is detected (the non-flat face of a thin relief; else the top,
  or the side with the most relief when the top is flat) and can be changed in
  the Relief rows (the model file is read again).
- Each heightmap cell holds the **highest** point of the model over the whole
  cell (triangle corners in the cell, cell corners in the triangle and edge /
  grid-line crossings give the exact maximum), so a tool kept clear of these
  columns is clear of the model.
- Size by Width / Height / **Scale (%)**, **Draw > Scale** or the handles;
  **Depth** follows the size until typed; **Top below stock** lowers it.
- Two toolpaths, each with its own tool and feeds: **3D roughing** (flat levels,
  raster runs plus each level's boundary, leaving an allowance) and **3D
  finishing** (parallel passes at a raster angle). Any tool but a drill; exact
  tool shapes for flat, ball, bull nose, V-bit and tapered ball nose.
- **No gouging**: drop-cutter against the cell columns; finishing refines between
  points where the surface bends or rises steeply, turns the finest steep steps
  into staircases, simplifies one-sidedly (a line may replace points only if it
  stays above all of them) and sits 0.0001" above the computed surface; roughing
  widens every drop by the allowance and a node diagonal so straight runs,
  boundary loops and links between nodes stay clear.
- The toolpaths are computed in the model's frame and cached, so moving or
  rotating the model does not compute them again. Generation runs on a worker
  with a busy bar and **Abort** (Shift+F5).

## Pocket and profile toolpaths

Every drawing has a **Toolpath** section (Drawings tab) and the text has one too
(Text tab, *3. Toolpath*). Each starts with the same rows: **Operation**,
**Tool** (the **...** button opens the tool library filtered to the tools that
can do the operation), **Spindle speed**, **Feed rate** and **Plunge rate**.
Then come the rows of the chosen operation:

| Operation | What it does | Settings |
|---|---|---|
| V-carve (default) | Carved with its V-bit; items with the same V-bit are carved together as one shape, each at its own feed (give them the same spindle speed; the first one's is used) | *Max usable depth* of the bit (read only); depths and passes under *Machine Settings > 2. Carve* |
| Pocket | Clears the area to a flat floor with an end mill: offset rings from the centre out to the wall, level by level | tool, cut depth, pass depth, stepover % |
| Profile | The tool follows the outline: **Outside** (cut the shape out at its true size), **Inside** (cut an opening) or **On the line** (engrave; a V-bit is allowed here) | tool, side, cut depth or *Cut through the stock*, pass depth, tabs (count, width, thickness) |

The grid shows only the settings the chosen operation uses. Changing the
operation swaps in the library's standard tool for it (1/4" 90 degree V-bit, or
1/4" flat end mill) when the current one can't do the job. When toolpaths share
a tool but run at different spindle speeds, the program changes the speed (an
`S` line, then the spindle dwell) between them. Climb or conventional milling
follows *Milling direction*. Through cuts go
0.02" below the stock thickness (*Machine Settings > 1. Blank*). Tabs leave bridges of material
so the part stays put, and the simulation shows the cut-out see-through.
Pocket clean-up passes cover the stripes a stepover above 50% would leave.
Areas narrower than the tool are reported in the status bar.

The job runs in this order: V-carve first, then pockets, then profiles, with
outside cut-outs last because they free the part. Items sharing a tool are cut
together, and a pocket tool that also profiles goes last among the pockets so
the profile follows without a tool change. When the job needs more than one
tool, **Export G-code** writes one numbered program per tool, such as
`Sign-1-1-4in_90_deg_V_bit.nc` and `Sign-2-1-4in_flat_end_mill.nc`, and lists
the order. Before each program, fit its tool and re-zero Z on the top of the
stock. Each program has the usual FluidNC start and end.

## Tool library

**Toolpath > Tool Library** (Ctrl+L), or the **...** button on a toolpath's
*Tool* row. Tools are inch only and grouped by type; each is four numbers, shown on a
scale drawing with its dimensions:

| Type | Diameter | Flute length | End radius | Angle (included) |
|---|---|---|---|---|
| Flat end mill | yes | yes | | |
| Ball nose | yes | yes | always half the diameter | |
| Bull nose | yes | yes | corner radius | |
| V-bit | yes | yes | | yes |
| Tapered ball nose | yes | yes | tip radius | taper |
| Drill | yes | yes | | point angle |

Each tool also stores a **spindle speed, feed rate and plunge rate**. Picking the
tool for a toolpath (the **...** button on its Tool row) fills those three in;
you can still change them per toolpath. Changing a tool's speeds in the library
updates the toolpaths that still use that tool's defaults, and leaves alone the
ones you gave their own values. The standard tools start with conservative
values for wood (for example 1/4" V-bit S16000 F40 plunge 15, 1/4" end mill
S16000 F60 plunge 20, 1/8" end mill S18000 F40 plunge 15); tune them to your
machine and material. Libraries saved before speeds existed get these starting
values by tool type and size.

Numbers accept decimals or fractions (`0.25`, `1/4`, `1-1/4`). Names follow the
numbers unless you type your own. **New** adds a tool of a type, **Duplicate**
copies one, **Delete** removes it; **Save** writes the library to
`%AppData%CarveMakerToolLibrary.json` (the standard tools are used until then).
V-carving takes a V-bit. Pockets take end mills (flat, ball or bull nose).
Profiles take end mills, or a V-bit on the line. The simulation draws whichever bit carves the job.
Projects saved before the library open with an equivalent V-bit.

## Simulation

The switch at the start of the *View* toolbar (it reads *Toolpath* or
*Simulation*, whichever is shown) and **View > Show Simulation** (Ctrl+M) flip
between the toolpath lines and the carved board.

**View > Show Simulation** (Ctrl+M) replaces the line drawing with the carved
board. The blank becomes a float heightmap texture on the GPU (one cell =
*Precision* under *Machine Settings > 4. Simulation*: 0.003", 0.005" or 0.010"), automatically
coarsened if the blank needs more cells than the graphics card allows (shown
as *Max texture size (hardware)*). Material is removed by stamping the tool into
that texture every half cell along each move: one small square per stamp whose
shader works out the tool's exact shape (flat, ball, bull nose, V-bit, tapered ball
nose) under each cell, MAX-blended, so the sweep is exact to the cell size and even
the millions of moves of a 3D finishing pass simulate in seconds. The
carved surface is drawn with one vertex per cell, so the chosen precision is
what you see when you zoom into a letter; *Display mesh* (Full / Half / Low)
caps that drawing at four million, one million or 250 thousand cells for
weaker graphics hardware. Changing either setting rebuilds the heightmap
immediately without regenerating the toolpath, and the simulation bar shows
the resulting cell count, cell size and GPU memory (with a note when the
hardware forced a coarser cell or the mesh is lighter than the heightmap).

The bar under the view has **Play / Pause**, **Reset**, a **Speed** list (1x to
100x or Instant) and a scrub slider; the clock uses the same feed, plunge and
rapid rates as the time estimate, so plunges deepen in real time. Toggling the
simulation on shows the finished part first; Reset and Play animate it.

## Settings worth knowing

| Setting | Meaning |
|---|---|
| Size means | *CapHeight*: a capital H is exactly the given size. *EmHeight*: the font's design size (capitals are ~70% of it). |
| Flat depth | Maximum depth. Clamped to the bit's usable depth `(D/2 - f/2)/tan(a)` = 0.125" for a sharp 1/4" 90 degree bit; default 0.12" keeps a margin. |
| Roughing depth step / allowance | Coarser steps are fine because the centerline pass finishes the surface. |
| Floor stepover | Scallop height on flat floors is about half of this on straight runs. |
| Tool, speed and feeds | Per toolpath (text and each drawing). The tool is a copy from the library stored in the project; editing that library entry later updates the toolpaths that use it. Projects from before per-toolpath feeds open with their old feed, plunge and spindle speed in every toolpath and their old V-bit in every V-carve toolpath. A new drawing starts with the text's V-bit and feeds when the text is V-carved. |
| Milling direction | Direction of the closed contours relative to the material still to be removed (inside the loop). |
| Cut order | *LineByLine* (default): finish each line of text, top line first, letters left to right. *Serpentine*: alternate lines run right to left. *LeftToRight*: every letter by X, which hops between lines. |
| Spindle dwell / Dust collection / Park | `G4 P<seconds>` after `M3` (FluidNC and GRBL take P in seconds), `G0 G53 Z0` at the start and before `M5` when *Retract to machine home* is on; the first positioning move is `G0 X Y` while fully up, then `G0 Z` on its own line, and `M8` before the spindle starts / `M9` after `M5` when *Dust collection M8 / M9* is on (M8 runs the dust vacuum on this machine). |
| Centerline finishing pass | Turn off only for quick previews; without it the ridge of every stroke is left up to two depth steps high. |
| Precision / Display mesh | Simulation only. *Precision* is the heightmap cell (0.003" / 0.005" / 0.010"); *Display mesh* caps how many cells are drawn (Full 4 M, Half 1 M, Low 250 k): pick Low on a laptop with integrated graphics. Neither changes the toolpath. |

## Building

Open `CarveMaker.sln` in Visual Studio 2022/2026 or run

```bash
dotnet build -c Release
```

NuGet packages: `OpenTK.GLControl` 4.0.2 (brings OpenTK 4.9.3) and `Clipper2`
2.0.0. The OpenGL view needs an OpenGL 3.3 capable driver; if the context
cannot be created the view shows the error text instead of crashing.

## Installer

The `Setup` project in the solution builds a Windows installer, **Release
configuration only** (it is skipped in Debug): it publishes the app
self-contained for x64 into `Setup\publish\` (no .NET install needed on the
target PC) and compiles `Setup\Setup.iss` with the Inno Setup compiler that
comes from the `Tools.InnoSetup` NuGet package, so no extension or separate
download is required. The result is
`CarveMaker-Setup-<version>.exe` in the solution root (about 48 MB).

The installer offers per-user or all-users install, Start Menu and optional
desktop shortcuts, an uninstaller, in-place upgrades (same AppId) and adds the
app to "Open with" for `.prj` files (it only becomes the default `.prj` handler
when nothing else owns the extension). Opening a `.prj` from Explorer launches
the app with that project. Silent install: `/VERYSILENT /NORESTART`.

The version number lives in `Directory.Build.props` and is used for the
assembly, the installer file name and the Add/Remove Programs entry.
