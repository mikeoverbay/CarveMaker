## CarveMakerGG Gcode Generator 1.4.0

CarveMaker has a new name, **CarveMakerGG Gcode Generator**, a new dark look, and carves 3D models. Your projects, tool library and preferences carry over.

**3D models (reliefs).** *File > Import 3D Model* reads STL, OBJ, PLY, 3MF and OFF files, and depth images. A model gets two toolpaths, **roughing** and **finishing**, each with its own tool, speeds and feeds, and each cut in a **Raster** or a **Spiral** (inside out or outside in). Finishing passes follow the surface and never cut below it; links between them stay on the surface, so the tool lifts far less often.

**V-carving.**

- Wide strokes are cut from the middle: the bit goes down in the middle of the flat floor, spirals out to its edge, then traces the edge once.
- Along the middle of a stroke the bit stays down; it only climbs where it runs into a sharp corner (no more zigzag at rounded ends).
- A rare over-cut where the bit could reach a little past the outline (for example on the outer wall of a ring) is fixed.

**The window.**

- A dark look after Code Chop: glossy buttons that glow under the mouse and show a pressed face, round switches that turn yellow while on.
- Toolbars across the top: *Project* (New, Open, Save, Generate, Stop, Export, Tool Library, Machine Settings) next to the drawing and style toolbars, and the *View* toolbar below.
- A **Status** button: green when the job is fine, red and blinking when something is wrong; click it for a table of every problem and the job's numbers.
- A **Font...** button on the Text tab.
- Yes/no settings are **check boxes**.
- Each toolpath has a **Show in view** box; *Hide* and *Show All* now hide and show toolpaths, never drawings. **Cuts** and **Rapids** switches show or hide the cutting moves and the rapid moves.
- A tilted view is a true perspective.
- With *Toolpath > Auto-regenerate* off, nothing is generated until you press F5; a new blank size is drawn at once.
- *Remove* asks before deleting a drawing.

**Machining.**

- **Export G-code** can write one program per tool or one per toolpath.
- **Extra stock on top**: material over the finished surface that you face off first; drawn over the blank and in the simulation.
- The simulation setting is now called *Rendering precision* (it only changes the picture).

### Earlier in 1.x

- **1.3**: the user guide (*Help > User Guide*, F1); picking what you mean where drawings overlap (Ctrl+click steps to the next one).
- **1.2**: movable toolbars (Draw Tools, Text Style, Snap): dock on any edge, float, hide; View > Toolbars.
- **1.1**: tool library with speeds and feeds; pocket and profile toolpaths; drawing shapes, areas and text on the blank; handles to move, scale and rotate; grid and rotation snapping; Machine Settings window.

Install with `CarveMakerGG-Gcode-Generator-Setup-1.4.0.exe` below. It upgrades an existing installation; projects, the tool library and preferences are kept.
