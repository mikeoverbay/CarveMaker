# CarveMaker

Windows desktop app (VB.NET, .NET 8, WinForms) that turns typed text in any
installed font into a **V-carve toolpath** for a V-bit (default 1/4" diameter,
90 degree) and writes G-code. The toolpath is shown live in an OpenGL view.

```
+------------------------------+----------------------------------------+
| Size: Large Medium Small     |                                        |
| Align: Left Center Right     |          OpenGL toolpath view          |
| [ rich text editor ]         |   (pan: left drag, orbit: right drag,  |
|                              |    zoom: wheel, double-click: fit)     |
+------------------------------+                                        |
| [ settings property grid ]   |                                        |
+------------------------------+----------------------------------------+
```

## Using it

1. Type the text in the editor. Each line can have one of three letter sizes
   (**Large / Medium / Small**, inch values editable under *1. Text*) and its own
   alignment (**Left / Center / Right**). Formatting applies to whole lines.
2. Each size has its own font: expand *Size 1 - Large font* (and Medium, Small)
   in the grid to pick family, bold and italic, or put the caret on a line and
   use **Font > Choose Font for Current Line's Size...** (Ctrl+F).
3. Adjust tool, carve and machine settings in the grid. The toolpath regenerates
   automatically (or press **F5**).
4. **File > Save Project** (Ctrl+S) stores everything, settings and formatted
   text, in a `.prj` file (readable JSON); **Open Project** (Ctrl+O) brings it
   back and **New Project** (Ctrl+N) starts over. The title bar shows a `*`
   while there are unsaved changes. **File > Export G-code...** (Ctrl+G) writes
   the program (`.nc`); **Import Text** reads a `.txt` or `.rtf` into the editor.

Blank and placement (*0. Blank* in the grid): give the stock **width X** and
**height Y** and the machine coordinates of its lower-left corner (*Blank
origin*; use `-width/2, -height/2` if you zero at the centre). Each line is
justified across the blank: **Left** starts at the left margin, **Center** is
centred on the blank, **Right** ends at the right margin. The whole block is
placed **Top / Middle / Bottom** between the margins, and *Text offset X/Y*
nudges it. The blank outline is drawn in tan in the view and the status bar
warns when text runs off it. **Z0 is the top of the stock**. Units are inches
internally; G-code can be written in inches (G20) or millimetres (G21).

## How the toolpath is made

For a V-bit with half angle *a* and tip flat *f*, a tip at depth *z* cuts a cone
of surface radius `f/2 + z*tan(a)`. The ideal carved surface is therefore the
distance field of the letter outline, clamped at the flat depth *F*.

1. **Outlines** – GDI+ glyph outlines (`GraphicsPath.AddString`, typographic
   layout, kerning) flattened to the curve tolerance, then a Clipper2 non-zero
   union. This normalises orientation for TrueType and CFF fonts and merges
   overlapping glyphs (script fonts).
2. **Roughing** – inward offset contours (Clipper2 `InflatePaths`) every
   *Roughing depth step*, commanded slightly shallow (*Roughing allowance*) so
   they never touch the finished bevel.
3. **Bevel finish at the flat depth** – the exact contour at the inset where the
   V reaches *Flat depth*, cut at `Z = -F`.
4. **Floor clearing** – further insets at the *Floor stepover*, all at `Z = -F`,
   for strokes wider than the V can reach (scallop height about half the stepover).
5. **Centerline finishing** – the medial axis of each letter traced with maximal
   inscribed circles (the F-Engrave / Vectric method): the tip follows the
   middle of every stroke with `Z = -(r - f/2)/tan(a)`, cutting the ridge, both
   flanks and lifting into sharp corners as the stroke narrows.
6. **Linking** – letters are machined one at a time; inside a letter each lobe
   is finished before the next, links are straight 3D feed moves when a gouge
   check against the distance field allows it, otherwise a short lift to
   *Clearance Z*. Letters are separated by moves at *Safe Z*.

A geometric regression test (see `scratch` harness used during development)
rasterises the ideal surface and the cut envelope: with the finishing pass on,
test letters in Arial, Times, Segoe Script and Gabriola show at most 0.0005"
uncut material and under 0.001" over-cut.

## Simulation

**View > Show Simulation** (Ctrl+M) replaces the line drawing with the carved
board. The blank becomes a float heightmap texture on the GPU (one cell =
*Precision* under *5. Simulation*: 0.003", 0.005" or 0.010"), automatically
coarsened if the blank needs more cells than the graphics card allows (shown
as *Max texture size (hardware)*). Material is removed by rendering the revolved
tool model from above into that texture with MAX blending every half cell along
each move, so the sweep is exact to the cell size and runs in milliseconds.

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
| Tip flat | Measured flat at the bit's point. Features narrower than it cannot be cut to depth. |
| Milling direction | Direction of the closed contours relative to the material still to be removed (inside the loop). |
| Cut order | *LineByLine* (default): finish each line of text, top line first, letters left to right. *Serpentine*: alternate lines run right to left. *LeftToRight*: every letter by X, which hops between lines. |
| Spindle dwell / Dust collection / Park | `G4 P<seconds>` after `M3` (FluidNC and GRBL take P in seconds), `G0 G53 Z0` at the start and before `M5` when *Retract to machine home* is on; the first positioning move is `G0 X Y` while fully up, then `G0 Z` on its own line, and `M8` before the spindle starts / `M9` after `M5` when *Dust collection M8 / M9* is on (M8 runs the dust vacuum on this machine). |
| Centerline finishing pass | Turn off only for quick previews; without it the ridge of every stroke is left up to two depth steps high. |

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
`Setup\Output\CarveMaker-Setup-<version>.exe` (about 48 MB).

The installer offers per-user or all-users install, Start Menu and optional
desktop shortcuts, an uninstaller, in-place upgrades (same AppId) and adds the
app to "Open with" for `.prj` files (it only becomes the default `.prj` handler
when nothing else owns the extension). Opening a `.prj` from Explorer launches
the app with that project. Silent install: `/VERYSILENT /NORESTART`.

The version number lives in `Directory.Build.props` and is used for the
assembly, the installer file name and the Add/Remove Programs entry.
