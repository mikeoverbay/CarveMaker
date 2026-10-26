## CarveMaker 1.2.0

**Movable toolbars.** The toolbar over the view is now three toolbars: *Draw Tools*, *Text Style* and *Snap*.

- Drag a toolbar by its grip to any edge of the view (top, bottom, left or right, in rows).
- Drag it off the view to float it in its own small window; double-click that window's title to dock it again.
- Right-click a toolbar to dock it on an edge, float it or hide it. **View > Toolbars** shows hidden toolbars again; *Reset Toolbars* puts them all back along the top.
- The arrangement is remembered for the next start.
- Floating toolbars work on the first click and leave the keyboard with the view or the text being typed.

### Still new since 1.0

- **Tool library** (Toolpath > Tool Library, Ctrl+L): standard inch V-bits, end mills, ball and bull nose, tapered ball nose and drills, each with its own spindle speed, feed and plunge rate.
- **Pocket and profile toolpaths** next to V-carving; the text and every drawing have their own operation, tool, speed and feeds. G-code is written as one program per tool.
- **Drawing on the blank** (Draw menu): rectangles, ellipses, polygons, stars, free-form areas and text areas; drag a corner of text to size the letters. The Style menu sets font, bold, italic and alignment.
- **Handles** to move, scale, stretch and rotate drawings; clicks select exactly the shape under the mouse.
- **Snapping**: grid snap (1" to 1/32") and rotation steps (15 degrees by default). Hold Alt or Shift to place freely.
- **Machine Settings** in their own window (Ctrl+K).

Install with `CarveMaker-Setup-1.2.0.exe` below. It upgrades an existing installation; projects, the tool library and preferences are kept.
