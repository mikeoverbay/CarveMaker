' ============================================================================
'  VCarve.vb
'  V-carve toolpath engine for a V-bit (default 1/4" 90 degree, sharp tip).
'
'  Geometry: with half angle a and tip flat f, a tip at depth z cuts a cone
'  whose radius at the stock surface is  f/2 + z*tan(a).  To cut exactly up to
'  the glyph boundary the tip must be at depth  z = (D - f/2)/tan(a)  where D
'  is the distance from the boundary. The ideal carved surface is therefore the
'  distance field of the glyph, clamped at the flat depth F.
'
'  Strategy (per connected letter region):
'   1. Roughing: constant-Z inward offset contours (Clipper2 InflatePaths) at
'      inset distances f/2 + allowance + k*step, commanded at the depth for
'      (inset - allowance). They remove the bulk but never touch the finished
'      surface (a skin 'allowance' thick is left on every flank).
'   2-3. The floor at Z = -F (FloorSpirals): rings at every stepover inside the
'      flat-depth inset; from the middle of each piece of floor the tool spirals
'      out (turns blend one ring into the next) and ends with one lap of the edge,
'      the exact contour at the flat-depth inset, which finishes the bevel.
'   4. Centerline finish: the medial axis traced with maximal inscribed circles
'      (F-Engrave method). Every boundary sample P with inward normal n yields
'      the largest circle tangent at P inside the region, centre P + r*n and
'      depth (r - f/2)/tan(a) clamped to F. Following it with varying Z cuts
'      the ridge, lifts into sharp corners (r -> 0) and finishes both flanks.
'      Points whose cone lies inside a deeper kept one, and points over the
'      floor, are dropped (DropCovered).
'  Links between passes inside a letter are straight 3D feed moves when a
'  gouge check against the distance field allows it, otherwise a short lift to
'  the clearance height. Letters are separated by moves at the safe height.
' ============================================================================

Imports System.Globalization
Imports System.Threading
Imports Clipper2Lib

''' <summary>Top-level pipeline: text -> glyph polygons -> passes -> linked moves.</summary>
Public Module TextToToolpath

    ''' <summary>Plain-text convenience: every line uses the large size and left alignment.</summary>
    Public Function Generate(text As String, s As CarveSettings, token As CancellationToken) As Toolpath
        Return Generate(TextLine.FromPlainText(text, s), s, token)
    End Function

    Public Function Generate(lines As IList(Of TextLine), s As CarveSettings, token As CancellationToken) As Toolpath
        Return Generate(lines, Nothing, s, token)
    End Function

    ''' <summary>Text lines plus already-placed drawings (machine inches), all V-carved; the text follows its Text operation.</summary>
    Public Function Generate(lines As IList(Of TextLine), drawings As IList(Of PathsD), s As CarveSettings, token As CancellationToken) As Toolpath
        Dim items As New List(Of MachiningItem)
        If drawings IsNot Nothing Then
            Dim n = 0
            For Each d In drawings
                n += 1
                items.Add(New MachiningItem("Drawing " & n, d, New Machining()))
            Next
        End If
        Return JobBuilder.Generate(lines, items, s, token)
    End Function

    ''' <summary>V-carves one (already unioned) shape with the V-bit: passes plus linked moves.</summary>
    Public Function CarveShape(shape As PathsD, lineCenters As List(Of Double), s As CarveSettings, token As CancellationToken) As Toolpath
        Dim tp As New Toolpath()
        If shape Is Nothing OrElse shape.Count = 0 Then Return tp
        For Each p In shape
            tp.Outline.Add(GlyphOutline.ToPts(p))
        Next

        If s.FlatDepth > s.MaxToolDepthIn + 0.000001 Then
            tp.Warnings.Add(String.Format(CultureInfo.InvariantCulture,
                "Flat depth limited to the tool's max usable depth {0:0.000}""", s.MaxToolDepthIn))
        End If
        If s.StockThickness > 0 AndAlso s.EffectiveFlatDepth >= s.StockThickness Then
            tp.Warnings.Add(String.Format(CultureInfo.InvariantCulture,
                "Flat depth {0:0.000}"" reaches through the {1:0.000}"" stock!", s.EffectiveFlatDepth, s.StockThickness))
        End If

        Dim regions As List(Of PathsD) = VCarveEngine.SplitRegions(shape, lineCenters, s.Order)
        tp.RegionCount = regions.Count

        For i = 0 To regions.Count - 1
            token.ThrowIfCancellationRequested()
            Dim rs As New RegionShape(regions(i))
            tp.Regions.Add(rs)
            VCarveEngine.CarveRegion(rs, i, s, tp.Contours, token)
        Next

        ToolpathLinker.Link(tp, s, token)
        Return tp
    End Function
End Module

''' <summary>
''' Boundary geometry of one letter region (outer positive, holes negative,
''' interior on the left of every edge) with distance and containment queries.
''' </summary>
Public Class RegionShape
    Public Structure Seg
        Public Ax, Ay, Bx, By As Double     ' end points
        Public Ux, Uy As Double             ' unit direction
        Public Nx, Ny As Double             ' unit inward normal (left of direction)
        Public Len As Double
        Public Id As Integer                ' global feature index (loop order, then edge order)
    End Structure

    Public ReadOnly Paths As PathsD
    Public ReadOnly Segs As New List(Of Seg)
    Public ReadOnly Bounds As RectD
    ''' <summary>Id of the edge before / after each edge within its loop.</summary>
    Public ReadOnly PrevId As List(Of Integer) = New List(Of Integer)
    Public ReadOnly NextId As List(Of Integer) = New List(Of Integer)
    ''' <summary>
    ''' True when the vertex at the START of the edge is a gentle convex turn, i.e. a
    ''' point on a flattened smooth curve rather than a real corner of the glyph.
    ''' </summary>
    Public ReadOnly SmoothStart As List(Of Boolean) = New List(Of Boolean)

    ''' <summary>Convex turns smaller than this (radians) are treated as smooth curve points.</summary>
    Public Const SmoothTurn As Double = 30.0 * Math.PI / 180.0

    Public Sub New(region As PathsD)
        Paths = region
        Bounds = Clipper.GetBounds(region)
        Dim id As Integer = 0
        For Each p In region
            Dim firstId As Integer = id
            For i = 0 To p.Count - 1
                Dim a = p(i)
                Dim b = p((i + 1) Mod p.Count)
                Dim dx = b.x - a.x, dy = b.y - a.y
                Dim len = Math.Sqrt(dx * dx + dy * dy)
                If len < 0.000000001 Then Continue For
                Segs.Add(New Seg With {
                    .Ax = a.x, .Ay = a.y, .Bx = b.x, .By = b.y,
                    .Ux = dx / len, .Uy = dy / len,
                    .Nx = -dy / len, .Ny = dx / len,
                    .Len = len, .Id = id})
                PrevId.Add(id - 1)
                NextId.Add(id + 1)
                SmoothStart.Add(False)
                id += 1
            Next
            If id > firstId Then
                PrevId(firstId) = id - 1
                NextId(id - 1) = firstId
            End If
        Next
        ' Classify the turn at the start vertex of every edge.
        For i = 0 To Segs.Count - 1
            Dim prev = Segs(PrevId(i))
            Dim cur = Segs(i)
            Dim cross = prev.Ux * cur.Uy - prev.Uy * cur.Ux
            Dim dot = prev.Ux * cur.Ux + prev.Uy * cur.Uy
            Dim turn = Math.Atan2(cross, dot)       ' > 0 = left = convex (interior on the left)
            SmoothStart(i) = (turn >= -0.0000001 AndAlso turn < SmoothTurn)
        Next
    End Sub

    ''' <summary>Euclidean distance from (x, y) to the nearest boundary edge.</summary>
    Public Function DistanceToBoundary(x As Double, y As Double) As Double
        Dim best As Double = Double.MaxValue
        For Each s In Segs
            Dim t As Double = ((x - s.Ax) * s.Ux + (y - s.Ay) * s.Uy)
            If t < 0 Then t = 0
            If t > s.Len Then t = s.Len
            Dim px = s.Ax + t * s.Ux, py = s.Ay + t * s.Uy
            Dim d2 = (x - px) * (x - px) + (y - py) * (y - py)
            If d2 < best Then best = d2
        Next
        Return Math.Sqrt(best)
    End Function

    ''' <summary>True when (x, y) lies inside the material to be carved (inside the outer, outside the holes).</summary>
    Public Function Contains(x As Double, y As Double) As Boolean
        Dim pt As New PointD(x, y)
        Dim inside As Boolean = False
        For Each p In Paths
            Dim r = Clipper.PointInPolygon(pt, p, GlyphOutline.ClipperPrecision)
            If r = PointInPolygonResult.IsOn Then Return True
            If r = PointInPolygonResult.IsInside Then inside = Not inside
        Next
        Return inside
    End Function
End Class

Public Module VCarveEngine

    Private ReadOnly Prec As Integer = GlyphOutline.ClipperPrecision
    ''' <summary>Point-reduction tolerance applied to inset contours before they become G-code.</summary>
    Private Const SimplifyTol As Double = 0.0001
    ''' <summary>Inset results thinner than this are Clipper noise at the moment of collapse.</summary>
    Private Const MinPathArea As Double = 0.000001

    ''' <summary>
    ''' Splits a normalized shape into independent regions, each a list of paths:
    ''' one outer (positive area) followed by its holes (negative area). Islands
    ''' nested inside holes become their own regions.
    ''' </summary>
    Public Function SplitRegions(shape As PathsD) As List(Of PathsD)
        Return SplitRegions(shape, Nothing, CutOrder.LeftToRight)
    End Function

    ''' <summary>
    ''' Splits and orders the regions for machining. With line centres available the
    ''' letters are cut line by line (top line first); otherwise simply left to right.
    ''' </summary>
    Public Function SplitRegions(shape As PathsD, lineCenters As List(Of Double), order As CutOrder) As List(Of PathsD)
        Dim regions As New List(Of PathsD)
        Dim tree As New PolyTreeD()
        Clipper.BooleanOp(ClipType.Union, shape, Nothing, tree, FillRule.NonZero, Prec)
        CollectRegions(tree, regions)

        ' Sort keys per region: line index (0 = top line), then X; Y as a tiebreaker.
        ' (Clipper's RectD uses screen names: top = minimum Y, bottom = maximum Y.)
        Dim keyed As New List(Of Tuple(Of Integer, Double, Double, PathsD))
        For Each r In regions
            Dim b = Clipper.GetBounds(r)
            Dim cy As Double = (b.top + b.bottom) / 2.0
            Dim lineIdx As Integer = 0
            If order <> CutOrder.LeftToRight AndAlso lineCenters IsNot Nothing AndAlso lineCenters.Count > 0 Then
                Dim best As Double = Double.MaxValue
                For i = 0 To lineCenters.Count - 1
                    Dim d = Math.Abs(lineCenters(i) - cy)
                    If d < best Then
                        best = d
                        lineIdx = i
                    End If
                Next
            End If
            Dim x As Double = (b.left + b.right) / 2.0
            If order = CutOrder.Serpentine AndAlso (lineIdx And 1) = 1 Then x = -x
            keyed.Add(Tuple.Create(lineIdx, x, cy, r))
        Next
        keyed.Sort(Function(a, b)
                       Dim c = a.Item1.CompareTo(b.Item1)
                       If c = 0 Then c = a.Item2.CompareTo(b.Item2)
                       If c = 0 Then c = b.Item3.CompareTo(a.Item3)
                       Return c
                   End Function)
        regions.Clear()
        For Each k In keyed
            regions.Add(k.Item4)
        Next
        Return regions
    End Function

    Private Sub CollectRegions(node As PolyPathD, regions As List(Of PathsD))
        For i = 0 To node.Count - 1
            Dim outer As PolyPathD = node(i)
            Dim region As New PathsD()
            region.Add(EnsureOrientation(outer.Polygon, True))
            For j = 0 To outer.Count - 1
                Dim hole As PolyPathD = outer(j)
                region.Add(EnsureOrientation(hole.Polygon, False))
                ' Anything inside a hole is a new outer.
                CollectRegions(hole, regions)
            Next
            regions.Add(region)
        Next
    End Sub

    Private Function EnsureOrientation(p As PathD, positive As Boolean) As PathD
        Dim a As Double = Clipper.Area(p)
        If (a > 0) <> positive Then Return Clipper.ReversePath(p)
        Return p
    End Function

    ''' <summary>Inward offset by d with round joins; noise filtered, points reduced.</summary>
    Private Function Inset(region As PathsD, d As Double, s As CarveSettings) As PathsD
        If d <= 0 Then Return New PathsD(region)
        Dim raw = Clipper.InflatePaths(region, -d, JoinType.Round, EndType.Polygon, 2.0, Prec, s.CurveTolerance)
        Dim clean As New PathsD(raw.Count)
        For Each p In raw
            If p.Count >= 3 AndAlso Math.Abs(Clipper.Area(p)) >= MinPathArea Then clean.Add(p)
        Next
        If clean.Count = 0 Then Return clean
        Dim simple = Clipper.SimplifyPaths(clean, SimplifyTol, True)
        Dim result As New PathsD(simple.Count)
        For Each p In simple
            If p.Count >= 3 Then result.Add(p)
        Next
        Return result
    End Function

    ''' <summary>One outer with its holes at a given inset, plus its chain id.</summary>
    Private Class Lobe
        Public Paths As New PathsD()
        Public Chain As Integer = -1
        Public HasChild As Boolean

        Public ReadOnly Property Outer As PathD
            Get
                Return Paths(0)
            End Get
        End Property
    End Class

    ''' <summary>Per-region bookkeeping for machining order.</summary>
    Private Class EmitContext
        Public RegionIndex As Integer
        Public Level As Integer
        Public NextChain As Integer
    End Class

    ''' <summary>Tool geometry derived from the settings.</summary>
    Private Structure ToolGeom
        Public TanA As Double        ' surface radius gained per unit depth
        Public HalfFlat As Double    ' tip flat radius
        Public Flat As Double        ' max tip depth F
        Public FlatInset As Double   ' inset distance whose depth is exactly F

        ''' <summary>Tip depth (positive) that cuts exactly to a boundary at distance d, clamped to F.</summary>
        Public Function DepthFor(d As Double) As Double
            Return Math.Min(Math.Max(0.0, (d - HalfFlat) / TanA), Flat)
        End Function
    End Structure

    Private Function Geometry(s As CarveSettings) As ToolGeom
        Dim g As ToolGeom
        g.TanA = Math.Tan(s.IncludedAngleDeg * Math.PI / 360.0)
        g.HalfFlat = Math.Max(0.0, s.TipFlatIn) / 2.0
        g.Flat = s.EffectiveFlatDepth
        g.FlatInset = g.HalfFlat + g.Flat * g.TanA
        Return g
    End Function

    ''' <summary>Generates all passes for one letter region.</summary>
    Public Sub CarveRegion(rs As RegionShape, regionIndex As Integer, s As CarveSettings,
                           output As List(Of ToolpathContour), token As CancellationToken)
        Dim region As PathsD = rs.Paths
        Dim g As ToolGeom = Geometry(s)
        Dim finish As Boolean = s.FinishPass
        Dim allowance As Double = If(finish, Math.Max(0.0, s.RoughAllowance), 0.0)
        Dim insetStep As Double = s.DepthStep * g.TanA
        Dim refineTol As Double = Math.Max(insetStep / 16.0, 0.0001)
        Dim ctx As New EmitContext With {.RegionIndex = regionIndex, .Level = 0}

        ' The untouched region is the root "lobe" (chain -1) of the hierarchy.
        Dim prevD As Double = 0
        Dim prevLobes As List(Of Lobe) = GroupLobes(region)
        For Each l In prevLobes
            l.Chain = -1
        Next

        ' ---- 1. roughing contours ------------------------------------------
        Dim k As Integer = 1
        Do
            token.ThrowIfCancellationRequested()
            Dim d As Double = g.HalfFlat + allowance + k * insetStep
            If d >= g.FlatInset - 0.0000001 Then Exit Do
            Dim curLobes = GroupLobes(Inset(region, d, s))
            ' Commanded depth is for (d - allowance): the cone stops 'allowance' short of the boundary.
            Dim z As Double = -g.DepthFor(d - allowance)
            prevLobes = Advance(prevLobes, prevD, curLobes, d, z, PassKind.Rough, Not finish, refineTol, g, ctx, s, output, token)
            prevD = d
            If prevLobes.Count = 0 Then Exit Do
            k += 1
        Loop

        ' ---- 2 and 3. the floor at Z = -F, and its edge (the exact bevel contour) ---
        ' The edge loops are not emitted here: the floor ends with them (FloorSpirals). Advance
        ' still refines the ridges of roughing lobes that vanished before the flat depth.
        ' The floor that is cut (its edges), for the centerline: Nothing when there is none.
        Dim floorEdge As PathsD = Nothing
        If prevLobes.Count > 0 Then
            token.ThrowIfCancellationRequested()
            Dim curLobes = GroupLobes(Inset(region, g.FlatInset, s))
            prevLobes = Advance(prevLobes, prevD, curLobes, g.FlatInset, -g.Flat, PassKind.BevelFinish, Not finish, refineTol, g, ctx, s, output, token, emit:=False)
            If curLobes.Count > 0 Then
                floorEdge = New PathsD(curLobes.SelectMany(Function(l) l.Paths))
                FloorSpirals(rs, region, curLobes, g, s, ctx, output, token)
            End If
        End If

        ' ---- 4. centerline finishing ---------------------------------------
        If finish Then
            token.ThrowIfCancellationRequested()
            CenterlinePass(rs, g, s, ctx, output, token, floorEdge)
        End If
    End Sub

    ''' <summary>
    ''' Links the lobes of the new pass to their parents, optionally refines the
    ''' ridge of any parent lobe that vanished, then emits the new pass.
    ''' </summary>
    Private Function Advance(prevLobes As List(Of Lobe), dPrev As Double, curLobes As List(Of Lobe), dCur As Double,
                             z As Double, kind As PassKind, refine As Boolean, tol As Double, g As ToolGeom,
                             ctx As EmitContext, s As CarveSettings, output As List(Of ToolpathContour),
                             token As CancellationToken, Optional emit As Boolean = True) As List(Of Lobe)
        ' Parent of each new lobe = the previous lobe whose outer contains it.
        Dim parentOf As New Dictionary(Of Lobe, Integer)
        For Each cur In curLobes
            Dim parent As Integer = -1
            Dim probe As PointD = cur.Outer(0)
            For Each prev In prevLobes
                If Clipper.PointInPolygon(probe, prev.Outer, Prec) <> PointInPolygonResult.IsOutside Then
                    parent = prev.Chain
                    prev.HasChild = True
                    Exit For
                End If
            Next
            parentOf(cur) = parent
        Next

        ' Odd level: ridge refinement for lobes that vanished during this step
        ' (only when there is no centerline pass to cut the ridge properly).
        ctx.Level += 1
        If refine Then
            For Each prev In prevLobes
                If prev.HasChild Then Continue For
                token.ThrowIfCancellationRequested()
                RefineCollapsedLobe(prev, dPrev, dCur, tol, z, kind, g, ctx, s, output)
            Next
        End If

        ' Even level: the regular pass, one chain per lobe.
        ctx.Level += 1
        For Each cur In curLobes
            cur.Chain = ctx.NextChain
            ctx.NextChain += 1
            If emit Then EmitLoops(cur.Paths, z, dCur, kind, ctx, output, s, cur.Chain, parentOf(cur))
        Next
        Return curLobes
    End Function

    ' ====================================================================
    '  The floor: down in the middle, spiral out, trace the edge
    ' ====================================================================

    ''' <summary>Where the floor walk stands at one ring (FloorSpirals).</summary>
    Private NotInheritable Class WalkFrame
        Public Ring As FloorRing
        Public State As Integer             ' 0: not started; 1: its one inner ring done; 2: going through its branches
        Public Pending As List(Of FloorRing)
        Public LastChild As FloorRing       ' the branch just cut (state 2)
    End Class

    ''' <summary>A spiral turn's points reduced to those that matter (within the point-reduction tolerance).</summary>
    Private Function Reduce(pts As List(Of PointD)) As List(Of PointD)
        If pts.Count < 3 Then Return pts
        Dim simple = Clipper.SimplifyPath(New PathD(pts), SimplifyTol, False)
        If simple.Count < 2 Then Return pts
        Return New List(Of PointD)(simple)
    End Function

    ''' <summary>One path of the floor at one inset (level 0: the edge), with the rings just inside it.</summary>
    Private NotInheritable Class FloorRing
        Public Path As PathD            ' in the cutting direction
        Public IsHole As Boolean
        Public Level As Integer
        Public Room As Double           ' inset beyond the edge: how far a point may stray from it and stay on the floor
        Public Source As PathD          ' as Clipper made it (outers counter-clockwise)
        Public Area As Double           ' absolute
        Public Children As New List(Of FloorRing)
        ' Running length at each vertex, and the whole length (the closing edge included).
        Public At As Double()
        Public Length As Double
    End Class

    ''' <summary>
    ''' The flat floor of a wide stroke (inside the flat-depth inset), cut at Z = -F. In each piece of it the
    ''' tool goes down in the middle (the innermost ring), spirals out one stepover per turn (each turn blends
    ''' one ring into the next, so there is no step between them), and ends with one lap of the edge, which
    ''' also finishes the bevel. A floor around an island (a counter) spirals out from its middle both ways,
    ''' to the edge and to the island's edge. Where a ring splits, or a turn could leave the floor, the ring
    ''' is cut as a lap and the cut steps across to the next one (or starts a new pass). Every piece is an
    ''' open pass (FloorClear) cut in the order and direction given: same Chain, Order = its place.
    ''' The pocket toolpaths (MillingEngine) are separate and unchanged.
    ''' </summary>
    Private Sub FloorSpirals(rs As RegionShape, region As PathsD, edge As List(Of Lobe), g As ToolGeom, s As CarveSettings, ctx As EmitContext,
                             output As List(Of ToolpathContour), token As CancellationToken)
        Dim stepover = Math.Max(s.ClearStepover, 0.0005)
        ' How much closer to the outline than its nominal inset a ring's points may lie (round joins, point reduction).
        Dim slack = Math.Max(0.0, s.CurveTolerance) + SimplifyTol
        Dim climb = (s.Direction = MillingDirection.Climb)
        ' Level 0: the edge; then every stepover further in, until nothing is left.
        Dim roots As New List(Of FloorRing)
        Dim levels As New List(Of List(Of FloorRing))
        Dim prevLevel = edge.SelectMany(Function(l) l.Paths).Select(Function(p) NewRing(p, 0, 0.0, climb)).ToList()
        roots.AddRange(prevLevel)
        levels.Add(prevLevel)
        Dim d = g.FlatInset
        Dim level = 0
        Do
            token.ThrowIfCancellationRequested()
            d += stepover
            Dim paths = Inset(region, d, s)
            If paths.Count = 0 Then Exit Do
            level += 1
            Dim rings = paths.Select(Function(p) NewRing(p, level, level * stepover, climb)).ToList()
            For Each r In rings
                Dim parent = ParentRing(r, prevLevel)
                If parent Is Nothing Then roots.Add(r) Else parent.Children.Add(r)
            Next
            levels.Add(rings)
            prevLevel = rings
        Loop

        ' The middles. Between two rings every place is within half a stepover of one, but where the floor ends
        ' between a ring and the next (the middle of a piece, or of the part of a ring with nothing further in)
        ' the middle lies up to a whole stepover in. There, the inset half a stepover further in, less what the
        ' next ring reaches, is a thin strip along the middle: cut as a small ring of its own (inside out, the
        ' spiral of a piece starts there, so the plunge is in the middle).
        For k = 0 To levels.Count - 1
            token.ThrowIfCancellationRequested()
            Dim half = Inset(region, g.FlatInset + k * stepover + stepover / 2, s)
            If half.Count = 0 Then Continue For
            Dim strips As PathsD = half
            If k + 1 < levels.Count Then
                Dim reach = Clipper.InflatePaths(New PathsD(levels(k + 1).Select(Function(r) r.Source)), stepover / 2 + 2 * slack,
                                                 JoinType.Round, EndType.Polygon, 2.0, Prec, s.CurveTolerance)
                strips = Clipper.Difference(half, reach, FillRule.NonZero, Prec)
            End If
            Dim lvl = k
            Dim stripRings = strips.Where(Function(p) p.Count >= 3 AndAlso Math.Abs(Clipper.Area(p)) >= MinPathArea).
                Select(Function(p) NewRing(p, lvl + 1, lvl * stepover + stepover / 2, climb)).ToList()
            For Each m In stripRings.OrderBy(Function(r) If(r.IsHole, 1, 0))
                Dim parent = ParentRing(m, levels(k))
                ' A strip's hole with no hole ring inside it (it holds only the next ring's piece): it goes with
                ' the strip, or the ring, that holds it.
                If parent Is Nothing AndAlso m.IsHole Then
                    parent = stripRings.Where(Function(o) Not o.IsHole AndAlso Clipper.PointInPolygon(m.Source(0), o.Source, Prec) <> PointInPolygonResult.IsOutside).
                        OrderBy(Function(o) o.Area).FirstOrDefault()
                    If parent Is Nothing Then
                        parent = levels(k).Where(Function(o) Not o.IsHole AndAlso Clipper.PointInPolygon(m.Source(0), o.Source, Prec) <> PointInPolygonResult.IsOutside).
                            OrderBy(Function(o) o.Area).FirstOrDefault()
                    End If
                End If
                If parent Is Nothing Then roots.Add(m) Else parent.Children.Insert(0, m)
            Next
        Next

        ' Each ring is "reached" when the cut stands on it; a ring is safe for a point when that point is
        ' no farther from it than the ring's inset beyond the edge (the cone at Z = -F then stays inside).
        Dim pieces As New List(Of List(Of PointD))
        ' Where the cut stands (Nothing before the first piece): pieces start, and branches are taken, nearest first.
        Dim here = Function() As PointD?
                       If pieces.Count = 0 Then Return Nothing
                       Dim l = pieces(pieces.Count - 1)
                       Return l(l.Count - 1)
                   End Function
        ' Cuts ring n and everything inside it, innermost first. True when n ends cut all the way round (False:
        ' a spiral turn ended on it). A loop with its own stack: a deep floor has thousands of rings.
        Dim walk = Function(top As FloorRing) As Boolean
                       Dim stack As New Stack(Of WalkFrame)
                       stack.Push(New WalkFrame With {.Ring = top})
                       Dim result = False         ' what the frame last popped returned
                       While stack.Count > 0
                           token.ThrowIfCancellationRequested()
                           Dim fr = stack.Peek()
                           Dim n = fr.Ring
                           Select Case fr.State
                               Case 0
                                   If n.Children.Count = 0 Then
                                       ' The middle of this floor: down here, one lap of the innermost ring.
                                       pieces.Add(Lap(n, here()))
                                       result = True
                                       stack.Pop()
                                   ElseIf n.Children.Count = 1 Then
                                       fr.State = 1
                                       stack.Push(New WalkFrame With {.Ring = n.Children(0)})
                                   Else
                                       fr.State = 2
                                       fr.Pending = n.Children.ToList()
                                   End If
                               Case 1
                                   ' The one ring inside is done: one spiral turn out onto this ring.
                                   Dim c = n.Children(0)
                                   Dim cur = pieces(pieces.Count - 1)
                                   Dim turn = Blend(rs, c, n, cur(cur.Count - 1), stepover, g.FlatInset)
                                   If turn IsNot Nothing Then
                                       cur.AddRange(Reduce(turn).Skip(1))
                                       result = False
                                   Else
                                       ' No safe turn: finish the inner ring, then this one as a lap of its own.
                                       If Not result Then cur.AddRange(Lap(c, cur(cur.Count - 1)).Skip(1))
                                       Join(pieces, Lap(n, cur(cur.Count - 1)), c.Room, n.Room, slack)
                                       result = True
                                   End If
                                   stack.Pop()
                               Case Else
                                   ' A split: every branch from its own middle out (the nearest next), then this ring.
                                   If fr.LastChild IsNot Nothing Then
                                       ' A branch ending on a spiral turn gets its ring finished first.
                                       If Not result Then
                                           Dim cur = pieces(pieces.Count - 1)
                                           cur.AddRange(Lap(fr.LastChild, cur(cur.Count - 1)).Skip(1))
                                       End If
                                       fr.LastChild = Nothing
                                   End If
                                   If fr.Pending.Count > 0 Then
                                       Dim at = here()
                                       Dim nextOne = fr.Pending(0)
                                       If at.HasValue Then nextOne = fr.Pending.OrderBy(Function(c) DistanceTo(c, at.Value)).First()
                                       fr.Pending.Remove(nextOne)
                                       fr.LastChild = nextOne
                                       stack.Push(New WalkFrame With {.Ring = nextOne})
                                   Else
                                       Dim last = pieces(pieces.Count - 1)
                                       Join(pieces, Lap(n, last(last.Count - 1)), n.Children.Max(Function(c) c.Room), n.Room, slack)
                                       result = True
                                       stack.Pop()
                                   End If
                           End Select
                       End While
                       Return result
                   End Function
        ' Each piece of floor in turn, the nearest next.
        Dim pendingRoots = roots.ToList()
        While pendingRoots.Count > 0
            Dim at = here()
            Dim root = pendingRoots(0)
            If at.HasValue Then root = pendingRoots.OrderBy(Function(r) DistanceTo(r, at.Value)).First()
            pendingRoots.Remove(root)
            Dim lapped = walk(root)
            If root.Level = 0 AndAlso Not lapped Then
                ' Out on the edge: trace it once all the way round.
                Dim last = pieces(pieces.Count - 1)
                last.AddRange(Lap(root, last(last.Count - 1)).Skip(1))
            End If
        End While

        Dim chain = ctx.NextChain
        ctx.NextChain += 1
        For i = 0 To pieces.Count - 1
            ' Points already reduced lap by lap and turn by turn (one reduction over a whole spiral is very slow).
            Dim pts = pieces(i).Select(Function(q) New Pt3(q.x, q.y, -g.Flat)).ToList()
            If pts.Count = 0 Then Continue For
            output.Add(New ToolpathContour With {
                .Z = -g.Flat, .Inset = g.FlatInset, .Kind = PassKind.FloorClear, .IsClosed = False,
                .RegionIndex = ctx.RegionIndex, .Order = i, .Chain = chain, .Parent = -1, .Points = pts})
        Next
    End Sub

    ''' <summary>
    ''' A ring in the cutting direction. Spiralling out, the material still to cut lies outside an outer ring
    ''' and toward the island inside a hole ring: on the right of Clipper's orientation, so climb milling
    ''' (material on the left) runs every ring the other way.
    ''' </summary>
    Private Function NewRing(p As PathD, level As Integer, room As Double, climb As Boolean) As FloorRing
        Dim area = Clipper.Area(p)
        Dim path = New PathD(p)
        If climb Then path.Reverse()
        Dim r As New FloorRing With {.Path = path, .IsHole = area < 0, .Level = level, .Room = room, .Source = p, .Area = Math.Abs(area)}
        ReDim r.At(path.Count - 1)
        Dim run = 0.0
        For i = 0 To path.Count - 1
            r.At(i) = run
            Dim a = path(i), b = path((i + 1) Mod path.Count)
            run += Math.Sqrt((b.x - a.x) ^ 2 + (b.y - a.y) ^ 2)
        Next
        r.Length = run
        Return r
    End Function

    ''' <summary>
    ''' The ring one stepover out that a ring lies just inside: the outer ring holding it, or for a hole
    ''' ring, the hole ring it holds (holes grow inward); the nearest when there are several.
    ''' </summary>
    Private Function ParentRing(c As FloorRing, outer As List(Of FloorRing)) As FloorRing
        Dim best As FloorRing = Nothing
        For Each p In outer
            If p.IsHole <> c.IsHole Then Continue For
            If Not c.IsHole Then
                If Clipper.PointInPolygon(c.Path(0), p.Path, Prec) <> PointInPolygonResult.IsOutside AndAlso (best Is Nothing OrElse p.Area < best.Area) Then best = p
            Else
                If Clipper.PointInPolygon(p.Path(0), c.Path, Prec) <> PointInPolygonResult.IsOutside AndAlso (best Is Nothing OrElse p.Area > best.Area) Then best = p
            End If
        Next
        Return best
    End Function

    ''' <summary>Point at running length t (wrapped) along a ring.</summary>
    Private Function PointAt(r As FloorRing, t As Double) As PointD
        If r.Length <= 0 Then Return r.Path(0)
        t = t Mod r.Length
        If t < 0 Then t += r.Length
        ' Binary search for the edge holding t.
        Dim lo = 0, hi = r.At.Length - 1
        While lo < hi
            Dim mid = (lo + hi + 1) \ 2
            If r.At(mid) <= t Then lo = mid Else hi = mid - 1
        End While
        Dim a = r.Path(lo), b = r.Path((lo + 1) Mod r.Path.Count)
        Dim len = If(lo + 1 < r.At.Length, r.At(lo + 1), r.Length) - r.At(lo)
        Dim f = If(len > 0, (t - r.At(lo)) / len, 0.0)
        Return New PointD(a.x + f * (b.x - a.x), a.y + f * (b.y - a.y))
    End Function

    ''' <summary>Running length of the point of the ring nearest to p.</summary>
    Private Function Nearest(r As FloorRing, p As PointD) As Double
        Dim best = Double.MaxValue, bestT = 0.0
        For i = 0 To r.Path.Count - 1
            Dim a = r.Path(i), b = r.Path((i + 1) Mod r.Path.Count)
            Dim dx = b.x - a.x, dy = b.y - a.y
            Dim l2 = dx * dx + dy * dy
            Dim f = If(l2 > 0, Math.Max(0.0, Math.Min(1.0, ((p.x - a.x) * dx + (p.y - a.y) * dy) / l2)), 0.0)
            Dim qx = a.x + f * dx, qy = a.y + f * dy
            Dim d2 = (p.x - qx) ^ 2 + (p.y - qy) ^ 2
            If d2 < best Then
                best = d2
                bestT = r.At(i) + f * Math.Sqrt(l2)
            End If
        Next
        Return bestT
    End Function

    ''' <summary>How far p is from a ring.</summary>
    Private Function DistanceTo(r As FloorRing, p As PointD) As Double
        Dim q = PointAt(r, Nearest(r, p))
        Return Math.Sqrt((q.x - p.x) ^ 2 + (q.y - p.y) ^ 2)
    End Function

    ''' <summary>One lap of a ring from its point nearest to p (its first point when p is Nothing), back to there.</summary>
    Private Function Lap(r As FloorRing, p As PointD?) As List(Of PointD)
        Dim t0 = If(p.HasValue, Nearest(r, p.Value), 0.0)
        Dim pts As New List(Of PointD) From {PointAt(r, t0)}
        ' The vertices after t0 in order, then back to the start.
        Dim n = r.Path.Count
        Dim first = 0
        While first < n AndAlso r.At(first) <= t0
            first += 1
        End While
        For k = 0 To n - 1
            pts.Add(r.Path((first + k) Mod n))
        Next
        pts.Add(pts(0))
        Return pts
    End Function

    ''' <summary>
    ''' One spiral turn from a point on the inner ring c out to the ring n around it. The turn follows n (the
    ''' longer ring) once round from its point nearest the start; at the fraction u of the way round, the cut
    ''' is u of the way from the nearest point of c to n's point (that nearest point moves on round c with
    ''' it), so the turn starts on c, ends on n where the next turn starts, and every place gets turns about a
    ''' stepover apart, corners included. Nothing when a point could take the cone off the floor. The distance
    ''' to the outline changes no faster than the position, so a point u*D from c's point and (1-u)*D from n's
    ''' (D apart) keeps max(c's room - u*D, n's room - (1-u)*D) beyond the edge, a ring's room being its Level
    ''' stepovers; where that bound is not enough (along a corner's bisector) the distance is measured.
    ''' Steps are at most 0.01" (their chords then stay within about 0.0001").
    ''' </summary>
    Private Function Blend(rs As RegionShape, c As FloorRing, n As FloorRing, start As PointD, stepover As Double, flatInset As Double) As List(Of PointD)
        If c.Length <= 0 OrElse n.Length <= 0 Then Return Nothing
        Dim tn = Nearest(n, start)
        ' Distances along n from that point where n has a corner, plus enough in between that no step is long.
        Dim ts As New List(Of Double) From {0.0, n.Length}
        For i = 0 To n.At.Length - 1
            ts.Add(Frac(n.At(i) - tn, n.Length) * n.Length)
        Next
        ts.Sort()
        Dim fine As New List(Of Double)
        For i = 0 To ts.Count - 1
            If i > 0 Then
                Dim gap = ts(i) - ts(i - 1)
                If gap <= 0.0000000001 Then Continue For
                Dim parts = CInt(Math.Ceiling(gap / 0.01))
                For k = 1 To parts - 1
                    fine.Add(ts(i - 1) + gap * k / parts)
                Next
            End If
            fine.Add(ts(i))
        Next
        Dim roomC = c.Room, roomN = n.Room
        Dim edge = EdgeAt(c, Nearest(c, start))
        Dim result As New List(Of PointD)(fine.Count + 1) From {start}
        For Each t In fine
            Dim u = t / n.Length
            Dim b = PointAt(n, tn + t)
            Dim a = ProjectOnward(c, b, edge, 2 * MaxBlendGap * stepover)
            Dim dd = Math.Sqrt((b.x - a.x) ^ 2 + (b.y - a.y) ^ 2)
            ' Only rings that run alongside each other blend: where a ring splits (a small inner piece under a
            ' big ring) the turn would cut across the floor and miss the rest of the ring.
            If dd > MaxBlendGap * stepover Then Return Nothing
            Dim p As New PointD(a.x + u * (b.x - a.x), a.y + u * (b.y - a.y))
            If Math.Max(roomC - u * dd, roomN - (1 - u) * dd) < -BlendTol AndAlso rs.DistanceToBoundary(p.x, p.y) < flatInset - BlendTol Then Return Nothing
            result.Add(p)
        Next
        Return result
    End Function

    ''' <summary>The edge of a ring holding running length t.</summary>
    Private Function EdgeAt(r As FloorRing, t As Double) As Integer
        Dim lo = 0, hi = r.At.Length - 1
        While lo < hi
            Dim mid = (lo + hi + 1) \ 2
            If r.At(mid) <= t Then lo = mid Else hi = mid - 1
        End While
        Return lo
    End Function

    ''' <summary>
    ''' The point of ring r nearest to q among the edges from the given one onward (it only moves on round
    ''' the ring as q does), the edge advanced to where it was found.
    ''' </summary>
    Private Function ProjectOnward(r As FloorRing, q As PointD, ByRef edge As Integer, giveUp As Double) As PointD
        Dim n = r.Path.Count
        Dim best = Double.MaxValue, bestK = 0
        Dim bestP As PointD = r.Path(edge)
        ' The nearest point moves on by at most a few edges per 0.01" step; around a corner of the outer ring
        ' it sweeps the inner ring's arc there, so look well ahead.
        For k = 0 To Math.Min(n - 1, 400)
            Dim i = (edge + k) Mod n
            Dim a = r.Path(i), b = r.Path((i + 1) Mod n)
            Dim dx = b.x - a.x, dy = b.y - a.y
            Dim l2 = dx * dx + dy * dy
            Dim f = If(l2 > 0, Math.Max(0.0, Math.Min(1.0, ((q.x - a.x) * dx + (q.y - a.y) * dy) / l2)), 0.0)
            Dim px = a.x + f * dx, py = a.y + f * dy
            Dim d2 = (q.x - px) ^ 2 + (q.y - py) ^ 2
            If d2 < best Then
                best = d2 : bestK = k : bestP = New PointD(px, py)
            ElseIf k > 16 AndAlso Math.Sqrt(d2) > Math.Sqrt(best) + giveUp Then
                ' Well past it: the rest of the ring is no nearer for a turn (rings farther apart are refused).
                Exit For
            End If
        Next
        edge = (edge + bestK) Mod n
        Return bestP
    End Function

    ''' <summary>How far past the edge's inset a spiral point may come (as the links' gouge tolerance).</summary>
    Private Const BlendTol As Double = 0.0001

    ''' <summary>
    ''' Farthest apart (in stepovers) the two rings of a spiral turn may be at any place: a corner of the outer
    ''' ring is 1 / sin(half its angle) stepovers from the inner ring's, so 4 still blends corners down to 29 deg.
    ''' </summary>
    Private Const MaxBlendGap As Double = 4.0

    Private Function Frac(t As Double, length As Double) As Double
        Dim f = (t Mod length) / length
        If f < 0 Then f += 1
        Return f
    End Function

    ''' <summary>
    ''' Continues the last piece with a lap when the straight step onto it stays on the floor (a step no
    ''' longer than the room of the deeper of the two rings), else starts a new piece with it.
    ''' </summary>
    Private Sub Join(pieces As List(Of List(Of PointD)), lapPts As List(Of PointD), roomFrom As Double, roomTo As Double, slack As Double)
        Dim last = pieces(pieces.Count - 1)
        Dim a = last(last.Count - 1), b = lapPts(0)
        Dim stepLen = Math.Sqrt((b.x - a.x) ^ 2 + (b.y - a.y) ^ 2)
        If stepLen <= Math.Max(roomFrom, roomTo) + slack Then
            last.AddRange(lapPts)
        Else
            pieces.Add(lapPts)
        End If
    End Sub

    ''' <summary>
    ''' A lobe present at inset dLo has no descendant at inset dHi: bisect the extra
    ''' inset for the deepest non-empty contour and emit the successful probes
    ''' (shallow to deep) as one chain converging on the ridge.
    ''' </summary>
    Private Sub RefineCollapsedLobe(lobe As Lobe, dLo As Double, dHi As Double, tol As Double, zHi As Double,
                                    kind As PassKind, g As ToolGeom, ctx As EmitContext,
                                    s As CarveSettings, output As List(Of ToolpathContour))
        Dim a As Double = 0                       ' known non-empty
        Dim b As Double = dHi - dLo               ' known empty
        Dim probes As New List(Of KeyValuePair(Of Double, PathsD))
        Dim iterations As Integer = 0
        While b - a > tol AndAlso iterations < 12
            iterations += 1
            Dim mid As Double = (a + b) / 2.0
            Dim r As PathsD = Inset(lobe.Paths, mid, s)
            If r.Count = 0 Then
                b = mid
            Else
                a = mid
                probes.Add(New KeyValuePair(Of Double, PathsD)(dLo + mid, r))
            End If
        End While
        If probes.Count = 0 Then Return
        probes.Sort(Function(x, y) x.Key.CompareTo(y.Key))
        Dim chain As Integer = ctx.NextChain
        ctx.NextChain += 1
        For Each kv In probes
            ' Rough and the last step before the flat depth: each probe at its own depth (at the flat depth the
            ' cone would reach past the outline); a floor probe stays at the flat depth.
            Dim z As Double = If(kind = PassKind.Rough OrElse kind = PassKind.BevelFinish, -g.DepthFor(kv.Key), zHi)
            EmitLoops(kv.Value, z, kv.Key, kind, ctx, output, s, chain, lobe.Chain)
        Next
    End Sub

    ''' <summary>Groups a flat path list into lobes: each outer with the holes it contains.</summary>
    Private Function GroupLobes(paths As PathsD) As List(Of Lobe)
        Dim lobes As New List(Of Lobe)
        Dim holes As New List(Of PathD)
        For Each p In paths
            If p.Count < 3 Then Continue For
            If Clipper.Area(p) > 0 Then
                Dim l As New Lobe()
                l.Paths.Add(p)
                lobes.Add(l)
            Else
                holes.Add(p)
            End If
        Next
        For Each h In holes
            Dim bestIdx As Integer = -1
            Dim bestArea As Double = Double.MaxValue
            For i = 0 To lobes.Count - 1
                Dim outer = lobes(i).Outer
                If Clipper.PointInPolygon(h(0), outer, Prec) <> PointInPolygonResult.IsOutside Then
                    Dim ar As Double = Clipper.Area(outer)
                    If ar < bestArea Then
                        bestArea = ar
                        bestIdx = i
                    End If
                End If
            Next
            If bestIdx >= 0 Then lobes(bestIdx).Paths.Add(h)
        Next
        Return lobes
    End Function

    ''' <summary>Adds one closed pass per path, oriented for the requested milling direction.</summary>
    Private Sub EmitLoops(paths As PathsD, z As Double, insetDist As Double, kind As PassKind,
                          ctx As EmitContext, output As List(Of ToolpathContour), s As CarveSettings,
                          chain As Integer, parent As Integer)
        For Each p In paths
            If p.Count < 3 Then Continue For
            Dim c As New ToolpathContour With {
                .Z = z,
                .Inset = insetDist,
                .Kind = kind,
                .IsClosed = True,
                .RegionIndex = ctx.RegionIndex,
                .Order = ctx.Level,
                .Chain = chain,
                .Parent = parent
            }
            ' Clipper: outers CCW (positive), holes CW (negative), i.e. the material still
            ' to be removed (inside an outer loop, outside a hole loop) is on the LEFT of
            ' travel. With a clockwise spindle the tooth at the left-hand wall moves with
            ' the feed, which is climb milling, so climb keeps Clipper's orientation and
            ' conventional reverses every loop.
            Dim reverse As Boolean = (s.Direction = MillingDirection.Conventional)
            If reverse Then
                For i = p.Count - 1 To 0 Step -1
                    c.Points.Add(New Pt3(p(i).x, p(i).y, z))
                Next
            Else
                For i = 0 To p.Count - 1
                    c.Points.Add(New Pt3(p(i).x, p(i).y, z))
                Next
            End If
            output.Add(c)
        Next
    End Sub

    ' ====================================================================
    '  Centerline (medial axis) finishing
    ' ====================================================================

    Private Structure MAPoint
        Public X, Y, R As Double
    End Structure

    ''' <summary>
    ''' Traces the medial axis with maximal inscribed circles and emits it as
    ''' variable-Z open passes. Both sides of every stroke trace the same axis, so
    ''' the raw chains are deduplicated afterwards: chains are kept longest first and
    ''' trimmed where an already kept chain passes within a couple of sample spacings.
    ''' </summary>
    Private Sub CenterlinePass(rs As RegionShape, g As ToolGeom, s As CarveSettings, ctx As EmitContext,
                               output As List(Of ToolpathContour), token As CancellationToken, floorEdge As PathsD)
        Dim spacing As Double = Math.Max(s.FinishResolution, 0.0002)
        Dim maxR As Double = Math.Max(rs.Bounds.right - rs.Bounds.left, rs.Bounds.bottom - rs.Bounds.top) + 1.0
        Dim chains As New List(Of List(Of MAPoint))
        Dim chain As New List(Of MAPoint)

        ' Feature ids follow RegionShape.Segs (degenerate edges skipped), so walk the same way.
        Dim segIndex As Integer = 0
        For Each p In rs.Paths
            Dim n As Integer = p.Count
            ' Find the last non-degenerate edge direction of this loop for the first vertex's turn test.
            Dim prevUx As Double = 0, prevUy As Double = 0, havePrev As Boolean = False
            For i = n - 1 To 0 Step -1
                Dim a0 = p(i), b0 = p((i + 1) Mod n)
                Dim l0 = Math.Sqrt((b0.x - a0.x) ^ 2 + (b0.y - a0.y) ^ 2)
                If l0 >= 0.000000001 Then
                    prevUx = (b0.x - a0.x) / l0 : prevUy = (b0.y - a0.y) / l0 : havePrev = True
                    Exit For
                End If
            Next

            For i = 0 To n - 1
                token.ThrowIfCancellationRequested()
                Dim a = p(i)
                Dim b = p((i + 1) Mod n)
                Dim dx = b.x - a.x, dy = b.y - a.y
                Dim len = Math.Sqrt(dx * dx + dy * dy)
                If len < 0.000000001 Then Continue For
                Dim ux = dx / len, uy = dy / len
                Dim nx = -uy, ny = ux
                Dim myId As Integer = segIndex
                segIndex += 1

                ' Reflex vertex at A (right turn with the interior on the left): sweep the
                ' inward normal from the previous edge to this one so the axis branch that
                ' starts at the reflex corner is traced continuously.
                If havePrev Then
                    Dim cross = prevUx * uy - prevUy * ux
                    If cross < -0.000001 Then
                        Dim ang0 = Math.Atan2(prevUx, -prevUy)      ' angle of previous inward normal (-prevUy, prevUx)
                        Dim ang1 = Math.Atan2(ux, -uy)              ' angle of this inward normal
                        Dim sweep = ang1 - ang0
                        While sweep > 0 : sweep -= 2 * Math.PI : End While   ' reflex turn rotates the normal clockwise
                        Dim steps As Integer = Math.Max(1, CInt(Math.Ceiling(-sweep / 0.05)))
                        For f = 1 To steps - 1
                            Dim ang = ang0 + sweep * f / steps
                            ProcessSample(rs, a.x, a.y, Math.Cos(ang), Math.Sin(ang), myId, maxR, g, chain, chains)
                        Next
                    End If
                End If

                Dim ns As Integer = Math.Max(1, CInt(Math.Ceiling(len / spacing)))
                For k = 0 To ns - 1
                    Dim t = (k + 0.5) / ns
                    ProcessSample(rs, a.x + t * dx, a.y + t * dy, nx, ny, myId, maxR, g, chain, chains)
                Next
                prevUx = ux : prevUy = uy : havePrev = True
            Next
            ' A loop boundary ends the chain.
            If chain.Count > 0 Then
                chains.Add(chain)
                chain = New List(Of MAPoint)
            End If
        Next
        If chain.Count > 0 Then chains.Add(chain)

        chains = Deduplicate(chains, spacing)
        chains = DropCovered(rs, chains, g, floorEdge)

        ' Emit chains as open variable-Z passes (one chain per Chain id, all at one level).
        ctx.Level += 1
        For Each ch In chains
            Dim pts As New List(Of Pt3)(ch.Count)
            Dim deepest As Double = 0
            For Each m In ch
                Dim z As Double = -g.DepthFor(m.R)
                If z < deepest Then deepest = z
                pts.Add(New Pt3(m.X, m.Y, z))
            Next
            If deepest > -0.00001 Then Continue For      ' nothing the tool can reach
            pts = Simplify3D(pts, SimplifyTol)
            If pts.Count < 1 Then Continue For
            Dim c As New ToolpathContour With {
                .Z = deepest,
                .Inset = 0,
                .Kind = PassKind.Centerline,
                .IsClosed = False,
                .RegionIndex = ctx.RegionIndex,
                .Order = ctx.Level,
                .Chain = ctx.NextChain,
                .Parent = -1,
                .Points = pts
            }
            ctx.NextChain += 1
            output.Add(c)
        Next
    End Sub

    ''' <summary>Computes the maximal circle for one boundary sample and appends it to the current chain or starts a new one.</summary>
    Private Sub ProcessSample(rs As RegionShape, px As Double, py As Double, nx As Double, ny As Double, myId As Integer,
                              maxR As Double, g As ToolGeom, ByRef chain As List(Of MAPoint), chains As List(Of List(Of MAPoint)))
        Dim limitId As Integer = -1
        Dim r As Double = MaxCircle(rs, px, py, nx, ny, myId, maxR, limitId)
        If r < maxR Then
            Dim m As MAPoint
            m.X = px + r * nx : m.Y = py + r * ny
            ' Never wider than the room around its centre: a true maximal circle is exactly that, and one made too
            ' big (gentle turns skip their neighbouring edges) would cut past the outline (a ring's outer wall).
            m.R = Math.Min(r, rs.DistanceToBoundary(m.X, m.Y))
            ' A big jump between consecutive axis points means the tangency switched
            ' branches; start a new chain so the linker can decide how to connect.
            If chain.Count > 0 Then
                Dim last = chain(chain.Count - 1)
                Dim jump = Math.Sqrt((m.X - last.X) ^ 2 + (m.Y - last.Y) ^ 2)
                If jump > 0.02 + 2 * Math.Abs(m.R - last.R) Then
                    chains.Add(chain)
                    chain = New List(Of MAPoint)
                End If
            End If
            chain.Add(m)
        ElseIf chain.Count > 0 Then
            chains.Add(chain)
            chain = New List(Of MAPoint)
        End If
    End Sub

    ''' <summary>
    ''' Removes the second tracing of every axis: chains are processed longest first;
    ''' points within 2 sample spacings of an already kept point are "covered". Covered
    ''' runs at the ends of a chain are trimmed, covered runs in the middle split it,
    ''' and anything shorter than 2 points is dropped.
    ''' </summary>
    Private Function Deduplicate(chains As List(Of List(Of MAPoint)), spacing As Double) As List(Of List(Of MAPoint))
        Dim eps As Double = 2.0 * spacing
        Dim cell As Double = eps
        Dim grid As New Dictionary(Of Long, List(Of MAPoint))
        Dim kept As New List(Of List(Of MAPoint))

        Dim keyOf = Function(x As Double, y As Double) As Long
                        Dim ix As Long = CLng(Math.Floor(x / cell))
                        Dim iy As Long = CLng(Math.Floor(y / cell))
                        Return (ix << 32) Xor (iy And &HFFFFFFFFL)
                    End Function
        Dim covered = Function(m As MAPoint) As Boolean
                          Dim ix As Long = CLng(Math.Floor(m.X / cell))
                          Dim iy As Long = CLng(Math.Floor(m.Y / cell))
                          For dx = -1 To 1
                              For dy = -1 To 1
                                  Dim k As Long = ((ix + dx) << 32) Xor ((iy + dy) And &HFFFFFFFFL)
                                  Dim bucket As List(Of MAPoint) = Nothing
                                  If grid.TryGetValue(k, bucket) Then
                                      For Each q In bucket
                                          If (q.X - m.X) ^ 2 + (q.Y - m.Y) ^ 2 <= eps * eps Then Return True
                                      Next
                                  End If
                              Next
                          Next
                          Return False
                      End Function

        For Each ch In chains.OrderByDescending(Function(c) c.Count)
            ' Split into uncovered runs.
            Dim run As New List(Of MAPoint)
            Dim runs As New List(Of List(Of MAPoint))
            For Each m In ch
                If covered(m) Then
                    If run.Count > 0 Then
                        runs.Add(run)
                        run = New List(Of MAPoint)
                    End If
                Else
                    run.Add(m)
                End If
            Next
            If run.Count > 0 Then runs.Add(run)
            For Each r In runs
                If r.Count < 2 Then Continue For
                kept.Add(r)
                For Each m In r
                    Dim k = keyOf(m.X, m.Y)
                    Dim bucket As List(Of MAPoint) = Nothing
                    If Not grid.TryGetValue(k, bucket) Then
                        bucket = New List(Of MAPoint)
                        grid(k) = bucket
                    End If
                    bucket.Add(m)
                Next
            Next
        Next
        Return kept
    End Function

    ''' <summary>
    ''' How far across (inches) the cut of a dropped centerline point may reach past the cuts that are
    ''' kept (DropCovered). Settable for tests; 0 keeps every point.
    ''' </summary>
    Friend Property CoverTolerance As Double = 0.001

    ''' <summary>
    ''' Drops the centerline points that carve nothing the deeper ones do not: where the tool's cone at a
    ''' point lies inside the cone at a kept point, to within CoverTolerance. Cones of one angle nest exactly
    ''' when their circles at the stock surface do, |c1 - c2| + r1 &lt;= r2, r being the cut radius at the
    ''' surface (the circle's radius, at most the flat-depth inset). Around a rounded stroke end every
    ''' outline sample gives nearly the same circle, scattered by a few thou: traced in outline order they
    ''' zigzagged and bobbed up and down. Along a stroke, and into a sharp corner, each circle reaches
    ''' material the others do not, so those points stay. What is left of a chain is joined straight
    ''' across a gap when that move cannot gouge, else split there for the linker.
    ''' </summary>
    Private Function DropCovered(rs As RegionShape, chains As List(Of List(Of MAPoint)), g As ToolGeom, floorEdge As PathsD) As List(Of List(Of MAPoint))
        Dim tol = Math.Max(0.0, CoverTolerance)
        If chains.Count = 0 Then Return chains
        ' Every point, deepest cut first, so a point is only ever held by one kept before it.
        Dim order As New List(Of (Chain As Integer, Index As Integer, R As Double))
        For c = 0 To chains.Count - 1
            For i = 0 To chains(c).Count - 1
                order.Add((c, i, Math.Min(chains(c)(i).R, g.FlatInset)))
            Next
        Next
        If order.Count = 0 Then Return chains
        order.Sort(Function(a, b) b.R.CompareTo(a.R))
        Dim maxR = order(0).R
        Const cell As Double = 0.01
        Dim grid As New Dictionary(Of Long, List(Of (X As Double, Y As Double, R As Double)))
        Dim keyOf = Function(ix As Long, iy As Long) As Long
                        Return (ix << 32) Xor (iy And &HFFFFFFFFL)
                    End Function
        Dim keep = chains.Select(Function(ch) New Boolean(ch.Count - 1) {}).ToList()
        For Each a In order
            Dim m = chains(a.Chain)(a.Index)
            ' A kept cone that holds this one has its centre within (its radius - this radius + tol).
            Dim n = CInt(Math.Ceiling((maxR - a.R + tol) / cell))
            Dim ix = CLng(Math.Floor(m.X / cell)), iy = CLng(Math.Floor(m.Y / cell))
            Dim covered = False
            For dx = -n To n
                For dy = -n To n
                    Dim bucket As List(Of (X As Double, Y As Double, R As Double)) = Nothing
                    If Not grid.TryGetValue(keyOf(ix + dx, iy + dy), bucket) Then Continue For
                    For Each k In bucket
                        If Math.Sqrt((k.X - m.X) ^ 2 + (k.Y - m.Y) ^ 2) + a.R <= k.R + tol Then
                            covered = True
                            Exit For
                        End If
                    Next
                    If covered Then Exit For
                Next
                If covered Then Exit For
            Next
            If covered Then Continue For
            ' Over a floor that is cut (the full flat depth) the floor's spiral and edge cut it: not cut again
            ' here, but it still holds the smaller cones beside it. Where no floor was made (one too small to
            ' keep), the point is cut at the flat depth as before.
            keep(a.Chain)(a.Index) = Not (m.R >= g.FlatInset AndAlso OnFloor(floorEdge, m.X, m.Y))
            Dim own As List(Of (X As Double, Y As Double, R As Double)) = Nothing
            Dim key = keyOf(ix, iy)
            If Not grid.TryGetValue(key, own) Then
                own = New List(Of (X As Double, Y As Double, R As Double))
                grid(key) = own
            End If
            own.Add((m.X, m.Y, a.R))
        Next

        ' What is left of each chain. A gap between kept points is crossed straight when that move is safe;
        ' otherwise the chain keeps its own way across (as before: no new lift). Dropped ends just go.
        Dim tip = Function(m As MAPoint) New Pt3(m.X, m.Y, -g.DepthFor(m.R))
        Dim result As New List(Of List(Of MAPoint))
        For c = 0 To chains.Count - 1
            Dim ch = chains(c)
            Dim run As New List(Of MAPoint)
            Dim gapFrom = -1        ' first dropped point after the last kept one
            For i = 0 To ch.Count - 1
                If Not keep(c)(i) Then
                    If run.Count > 0 AndAlso gapFrom < 0 Then gapFrom = i
                    Continue For
                End If
                If gapFrom >= 0 AndAlso Not ToolpathLinker.GougeFree(rs, tip(run(run.Count - 1)), tip(ch(i)), g.TanA, g.HalfFlat) Then
                    run.AddRange(ch.GetRange(gapFrom, i - gapFrom))
                End If
                gapFrom = -1
                run.Add(ch(i))
            Next
            If run.Count > 0 Then result.Add(run)
        Next
        Return result
    End Function

    ''' <summary>True when (x, y) lies on the floor inside these edges (outers and holes; on an edge counts).</summary>
    Private Function OnFloor(edges As PathsD, x As Double, y As Double) As Boolean
        If edges Is Nothing Then Return False
        Dim pt As New PointD(x, y)
        Dim inside = False
        For Each p In edges
            Dim r = Clipper.PointInPolygon(pt, p, Prec)
            If r = PointInPolygonResult.IsOn Then Return True
            If r = PointInPolygonResult.IsInside Then inside = Not inside
        Next
        Return inside
    End Function

    ''' <summary>
    ''' Largest circle tangent to the boundary at P (inward normal n) that stays inside
    ''' the region: minimum over all edges of the line candidate and the end-point
    ''' candidates. Returns the radius and the id of the limiting edge.
    ''' Edges adjacent to the sample's own edge across a gentle convex turn are not
    ''' allowed to limit the circle: on a flattened smooth curve they would otherwise
    ''' create a spurious tiny medial branch into every vertex.
    ''' </summary>
    Private Function MaxCircle(rs As RegionShape, px As Double, py As Double, nx As Double, ny As Double,
                               myId As Integer, maxR As Double, ByRef limitId As Integer) As Double
        Dim r As Double = maxR
        limitId = -1
        Dim prevId As Integer = If(myId >= 0, rs.PrevId(myId), -1)
        Dim nextId As Integer = If(myId >= 0, rs.NextId(myId), -1)
        Dim skipPrevLine As Boolean = myId >= 0 AndAlso rs.SmoothStart(myId)        ' smooth vertex at my start
        Dim skipNextLine As Boolean = nextId >= 0 AndAlso rs.SmoothStart(nextId)    ' smooth vertex at my end

        For Each sg In rs.Segs
            Dim isPrev As Boolean = (sg.Id = prevId) AndAlso skipPrevLine
            Dim isNext As Boolean = (sg.Id = nextId) AndAlso skipNextLine
            Dim isSelf As Boolean = (sg.Id = myId)

            ' Line candidate: centre C = P + r n at distance r from the edge's line.
            If Not (isPrev OrElse isNext OrElse isSelf) Then
                Dim dpm = (px - sg.Ax) * sg.Nx + (py - sg.Ay) * sg.Ny
                Dim ndm = nx * sg.Nx + ny * sg.Ny
                If dpm > 0.000000001 AndAlso 1 - ndm > 0.000000001 Then
                    Dim rc = dpm / (1 - ndm)
                    If rc < r Then
                        Dim cx = px + rc * nx, cy = py + rc * ny
                        Dim foot = (cx - sg.Ax) * sg.Ux + (cy - sg.Ay) * sg.Uy
                        If foot >= -0.000000001 AndAlso foot <= sg.Len + 0.000000001 Then
                            r = rc
                            limitId = sg.Id
                        End If
                    End If
                End If
            End If
            ' End-point candidates: circle through P (tangent there) and the vertex Q.
            ' The vertex shared with a smooth neighbour is skipped for the same reason.
            If Not (isSelf OrElse isNext) Then
                Dim qx = sg.Ax - px, qy = sg.Ay - py
                Dim den = 2 * (nx * qx + ny * qy)
                If den > 0.000000000001 Then
                    Dim rc = (qx * qx + qy * qy) / den
                    If rc < r Then
                        r = rc
                        limitId = sg.Id
                    End If
                End If
            End If
            If Not (isSelf OrElse isPrev) Then
                Dim qx = sg.Bx - px, qy = sg.By - py
                Dim den = 2 * (nx * qx + ny * qy)
                If den > 0.000000000001 Then
                    Dim rc = (qx * qx + qy * qy) / den
                    If rc < r Then
                        r = rc
                        limitId = sg.Id
                    End If
                End If
            End If
        Next
        Return r
    End Function

    ''' <summary>Ramer-Douglas-Peucker in 3D for open polylines.</summary>
    Public Function Simplify3D(pts As List(Of Pt3), tol As Double) As List(Of Pt3)
        If pts.Count < 3 Then Return pts
        Dim keep(pts.Count - 1) As Boolean
        keep(0) = True : keep(pts.Count - 1) = True
        Dim stack As New Stack(Of KeyValuePair(Of Integer, Integer))
        stack.Push(New KeyValuePair(Of Integer, Integer)(0, pts.Count - 1))
        While stack.Count > 0
            Dim seg = stack.Pop()
            Dim i0 = seg.Key, i1 = seg.Value
            If i1 - i0 < 2 Then Continue While
            Dim a = pts(i0), b = pts(i1)
            Dim abx = b.X - a.X, aby = b.Y - a.Y, abz = b.Z - a.Z
            Dim ab2 = abx * abx + aby * aby + abz * abz
            Dim worst As Double = -1, worstIdx As Integer = -1
            For i = i0 + 1 To i1 - 1
                Dim p = pts(i)
                Dim d As Double
                If ab2 < 0.000000000001 Then
                    d = a.DistanceTo(p)
                Else
                    Dim t = ((p.X - a.X) * abx + (p.Y - a.Y) * aby + (p.Z - a.Z) * abz) / ab2
                    If t < 0 Then t = 0
                    If t > 1 Then t = 1
                    Dim qx = a.X + t * abx, qy = a.Y + t * aby, qz = a.Z + t * abz
                    d = Math.Sqrt((p.X - qx) ^ 2 + (p.Y - qy) ^ 2 + (p.Z - qz) ^ 2)
                End If
                If d > worst Then
                    worst = d
                    worstIdx = i
                End If
            Next
            If worst > tol Then
                keep(worstIdx) = True
                stack.Push(New KeyValuePair(Of Integer, Integer)(i0, worstIdx))
                stack.Push(New KeyValuePair(Of Integer, Integer)(worstIdx, i1))
            End If
        End While
        Dim result As New List(Of Pt3)
        For i = 0 To pts.Count - 1
            If keep(i) Then result.Add(pts(i))
        Next
        Return result
    End Function
End Module

''' <summary>Orders passes and turns them into linked rapid/plunge/feed motions.</summary>
Public Module ToolpathLinker

    ''' <summary>Longest in-material feed link tried before lifting to the clearance height.</summary>
    Private Const MaxFeedLink As Double = 0.5
    ''' <summary>
    ''' The same between two pieces of one floor: the link runs at the flat depth over floor that is cut anyway,
    ''' so a longer one still beats a lift (when it cannot gouge).
    ''' </summary>
    Private Const MaxFloorLink As Double = 2.0
    ''' <summary>Permitted overshoot of the cone below the finished surface during a link (1e-4 in).</summary>
    Private Const GougeTol As Double = 0.0001

    Public Sub Link(tp As Toolpath, s As CarveSettings, token As CancellationToken)
        Dim moves As List(Of ToolMove) = tp.Moves
        moves.Clear()

        ' Bounds from the outline (the toolpath lies inside it).
        Dim minX As Double = Double.MaxValue, minY As Double = Double.MaxValue
        Dim maxX As Double = Double.MinValue, maxY As Double = Double.MinValue
        For Each poly In tp.Outline
            For Each p In poly
                If p.X < minX Then minX = p.X
                If p.X > maxX Then maxX = p.X
                If p.Y < minY Then minY = p.Y
                If p.Y > maxY Then maxY = p.Y
            Next
        Next
        If minX = Double.MaxValue Then
            minX = 0 : maxX = 0 : minY = 0 : maxY = 0
        End If
        tp.MinX = minX : tp.MaxX = maxX : tp.MinY = minY : tp.MaxY = maxY

        If tp.Contours.Count = 0 Then
            tp.MinZ = 0
            tp.EstimatedMinutes = 0
            Return
        End If

        Dim tanA As Double = Math.Tan(s.IncludedAngleDeg * Math.PI / 360.0)
        Dim halfFlat As Double = Math.Max(0.0, s.TipFlatIn) / 2.0
        Dim safe As Double = s.SafeZ
        Dim clear As Double = Math.Min(s.ClearanceZ, safe)

        Dim cutLen As Double = 0, plungeLen As Double = 0, rapidLen As Double = 0
        Dim cur As New Pt3(0, 0, safe)
        Dim minZ As Double = 0
        Dim ordered As New List(Of ToolpathContour)(tp.Contours.Count)

        For Each grp In tp.Contours.GroupBy(Function(c) c.RegionIndex).OrderBy(Function(g) g.Key)
            token.ThrowIfCancellationRequested()
            Dim rs As RegionShape = If(grp.Key >= 0 AndAlso grp.Key < tp.Regions.Count, tp.Regions(grp.Key), Nothing)
            Dim first As Boolean = True

            ' --- closed passes: depth-first through the lobe hierarchy -------------
            Dim closedPasses = grp.Where(Function(c) c.IsClosed).ToList()
            Dim sequence As New List(Of ToolpathContour)
            If closedPasses.Count > 0 Then
                Dim chains As New Dictionary(Of Integer, List(Of ToolpathContour))
                Dim children As New Dictionary(Of Integer, List(Of Integer))
                For Each c In closedPasses
                    Dim list As List(Of ToolpathContour) = Nothing
                    If Not chains.TryGetValue(c.Chain, list) Then
                        list = New List(Of ToolpathContour)
                        chains(c.Chain) = list
                        Dim kids As List(Of Integer) = Nothing
                        If Not children.TryGetValue(c.Parent, kids) Then
                            kids = New List(Of Integer)
                            children(c.Parent) = kids
                        End If
                        kids.Add(c.Chain)
                    End If
                    list.Add(c)
                Next
                For Each list In chains.Values
                    list.Sort(Function(a, b)
                                  Dim r = b.Z.CompareTo(a.Z)      ' shallow first
                                  If r = 0 Then r = a.Order.CompareTo(b.Order)
                                  Return r
                              End Function)
                Next
                Dim pos As New Pt2(cur.X, cur.Y)
                Dim visited As New HashSet(Of Integer)
                VisitChildren(-1, chains, children, sequence, pos, visited)
                For Each kv In chains.OrderBy(Function(k) k.Key)
                    If visited.Add(kv.Key) Then EmitChain(kv.Value, sequence, pos)
                Next
            End If

            ' --- open passes: greedy nearest. Centerlines either direction; the floor's pieces (a spiral out
            '     from the middle, ending on the edge) only forward and in their order (Order within Chain).
            Dim openPasses = grp.Where(Function(c) Not c.IsClosed).ToList()
            Dim pos2 As Pt2 = If(sequence.Count > 0, New Pt2(sequence(sequence.Count - 1).Points(0).X, sequence(sequence.Count - 1).Points(0).Y), New Pt2(cur.X, cur.Y))
            While openPasses.Count > 0
                Dim bestIdx As Integer = 0, bestDist As Double = Double.MaxValue, bestReverse As Boolean = False
                For i = 0 To openPasses.Count - 1
                    Dim c = openPasses(i)
                    Dim isFloor = c.Kind = PassKind.FloorClear
                    If isFloor AndAlso openPasses.Any(Function(o) o.Kind = PassKind.FloorClear AndAlso o.Chain = c.Chain AndAlso o.Order < c.Order) Then Continue For
                    Dim d0 = pos2.DistanceTo(New Pt2(c.Points(0).X, c.Points(0).Y))
                    Dim d1 = If(isFloor, Double.MaxValue, pos2.DistanceTo(New Pt2(c.Points(c.Points.Count - 1).X, c.Points(c.Points.Count - 1).Y)))
                    If d0 < bestDist Then
                        bestDist = d0 : bestIdx = i : bestReverse = False
                    End If
                    If d1 < bestDist Then
                        bestDist = d1 : bestIdx = i : bestReverse = True
                    End If
                Next
                Dim chosen = openPasses(bestIdx)
                openPasses.RemoveAt(bestIdx)
                If bestReverse Then chosen.Points.Reverse()
                sequence.Add(chosen)
                Dim last = chosen.Points(chosen.Points.Count - 1)
                pos2 = New Pt2(last.X, last.Y)
            End While

            ' --- emit motions for this region ---------------------------------
            Dim previous As ToolpathContour = Nothing
            For Each c In sequence
                ordered.Add(c)
                Dim start As Pt3 = c.Points(0)
                If start.Z < minZ Then minZ = start.Z

                Dim linked As Boolean = False
                If Not first AndAlso rs IsNot Nothing Then
                    ' Try a direct in-material feed link from the current tip position.
                    Dim dxy = Math.Sqrt((start.X - cur.X) ^ 2 + (start.Y - cur.Y) ^ 2)
                    Dim floorToFloor = previous IsNot Nothing AndAlso previous.Kind = PassKind.FloorClear AndAlso c.Kind = PassKind.FloorClear AndAlso previous.Chain = c.Chain
                    If dxy <= If(floorToFloor, MaxFloorLink, MaxFeedLink) AndAlso cur.Z <= 0 AndAlso GougeFree(rs, cur, start, tanA, halfFlat) Then
                        If floorToFloor AndAlso Math.Abs(start.Z - cur.Z) < 0.000001 Then
                            ' Level across the floor: a cut like the floor's own passes.
                            cutLen += cur.DistanceTo(start)
                            moves.Add(New ToolMove(MoveKind.Feed, start, s.FeedRate))
                        Else
                            plungeLen += cur.DistanceTo(start)
                            moves.Add(New ToolMove(MoveKind.Plunge, start, s.PlungeRate))
                        End If
                        cur = start
                        linked = True
                    End If
                End If

                If Not linked Then
                    Dim travelZ As Double = If(first, safe, clear)
                    If cur.Z < travelZ - 0.0000001 Then
                        Dim up As New Pt3(cur.X, cur.Y, travelZ)
                        rapidLen += cur.DistanceTo(up)
                        moves.Add(New ToolMove(MoveKind.Rapid, up, 0))
                        cur = up
                    End If
                    Dim above As New Pt3(start.X, start.Y, cur.Z)
                    rapidLen += cur.DistanceTo(above)
                    moves.Add(New ToolMove(MoveKind.Rapid, above, 0))
                    cur = above
                    If first AndAlso clear < cur.Z - 0.0000001 Then
                        Dim down As New Pt3(start.X, start.Y, clear)
                        rapidLen += cur.DistanceTo(down)
                        moves.Add(New ToolMove(MoveKind.Rapid, down, 0))
                        cur = down
                    End If
                    plungeLen += cur.DistanceTo(start)
                    moves.Add(New ToolMove(MoveKind.Plunge, start, s.PlungeRate))
                    cur = start
                End If
                first = False
                previous = c

                ' Cut along the pass.
                For k = 1 To c.Points.Count - 1
                    Dim nxt = c.Points(k)
                    If nxt.Z < minZ Then minZ = nxt.Z
                    cutLen += cur.DistanceTo(nxt)
                    moves.Add(New ToolMove(MoveKind.Feed, nxt, s.FeedRate))
                    cur = nxt
                Next
                If c.IsClosed AndAlso c.Points.Count > 1 Then
                    cutLen += cur.DistanceTo(start)
                    moves.Add(New ToolMove(MoveKind.Feed, start, s.FeedRate))
                    cur = start
                End If
            Next

            ' Leave the region at the safe height.
            If cur.Z < safe - 0.0000001 Then
                Dim up As New Pt3(cur.X, cur.Y, safe)
                rapidLen += cur.DistanceTo(up)
                moves.Add(New ToolMove(MoveKind.Rapid, up, 0))
                cur = up
            End If
        Next

        tp.Contours = ordered
        tp.MinZ = minZ
        tp.CutLength = cutLen + plungeLen
        tp.RapidLength = rapidLen
        Dim rapidRate As Double = If(s.RapidRate > 0, s.RapidRate, 100.0)
        tp.EstimatedMinutes = cutLen / s.FeedRate + plungeLen / s.PlungeRate + rapidLen / rapidRate

        ' Per-move durations for the simulation clock (same rates as the estimate).
        Dim prev As New Pt3(0, 0, safe)
        For Each mv In moves
            Dim rate As Double
            Select Case mv.Kind
                Case MoveKind.Rapid : rate = rapidRate
                Case MoveKind.Plunge : rate = If(mv.Feed > 0, mv.Feed, s.PlungeRate)
                Case Else : rate = If(mv.Feed > 0, mv.Feed, s.FeedRate)
            End Select
            mv.Seconds = prev.DistanceTo(mv.Target) / Math.Max(rate, 0.001) * 60.0
            prev = mv.Target
        Next
    End Sub

    ''' <summary>
    ''' True when the straight 3D move a -> b keeps the cone within the finished
    ''' surface: every sampled tip lies inside the region and its surface radius
    ''' (tip flat + depth * tan a) does not exceed the local boundary distance.
    ''' </summary>
    Friend Function GougeFree(rs As RegionShape, a As Pt3, b As Pt3, tanA As Double, halfFlat As Double) As Boolean
        Dim dxy = Math.Sqrt((b.X - a.X) ^ 2 + (b.Y - a.Y) ^ 2)
        Dim n As Integer = Math.Max(1, CInt(Math.Ceiling(dxy / 0.002)))
        For k = 0 To n
            Dim t = k / CDbl(n)
            Dim x = a.X + t * (b.X - a.X)
            Dim y = a.Y + t * (b.Y - a.Y)
            Dim z = a.Z + t * (b.Z - a.Z)
            ' Even a surface-level link must stay over the letter, never over uncut stock.
            If Not rs.Contains(x, y) Then Return False
            If z >= 0 Then Continue For
            Dim coneR = halfFlat + (-z) * tanA
            If coneR > rs.DistanceToBoundary(x, y) + GougeTol Then Return False
        Next
        Return True
    End Function

    ''' <summary>Depth-first: emit the nearest child chain, then its subtree, then the next child.</summary>
    Private Sub VisitChildren(parent As Integer, chains As Dictionary(Of Integer, List(Of ToolpathContour)),
                              children As Dictionary(Of Integer, List(Of Integer)),
                              ordered As List(Of ToolpathContour), ByRef pos As Pt2, visited As HashSet(Of Integer))
        Dim kids As List(Of Integer) = Nothing
        If Not children.TryGetValue(parent, kids) Then Return
        Dim pending As New List(Of Integer)(kids)
        While pending.Count > 0
            Dim bestIdx As Integer = 0
            Dim bestDist As Double = Double.MaxValue
            For k = 0 To pending.Count - 1
                Dim list = chains(pending(k))
                Dim dd As Double = NearestDistance(list(0), pos)
                If dd < bestDist Then
                    bestDist = dd
                    bestIdx = k
                End If
            Next
            Dim id As Integer = pending(bestIdx)
            pending.RemoveAt(bestIdx)
            If visited.Add(id) Then
                EmitChain(chains(id), ordered, pos)
                VisitChildren(id, chains, children, ordered, pos, visited)
            End If
        End While
    End Sub

    Private Sub EmitChain(list As List(Of ToolpathContour), ordered As List(Of ToolpathContour), ByRef pos As Pt2)
        For Each c In list
            RotateToNearest(c, pos)
            ordered.Add(c)
            pos = New Pt2(c.Points(0).X, c.Points(0).Y)
        Next
    End Sub

    Private Function NearestDistance(c As ToolpathContour, p As Pt2) As Double
        Dim best As Double = Double.MaxValue
        For Each q In c.Points
            Dim dd = p.DistanceTo(New Pt2(q.X, q.Y))
            If dd < best Then best = dd
        Next
        Return best
    End Function

    ''' <summary>Rotates a closed loop so it starts at the vertex nearest to p.</summary>
    Private Sub RotateToNearest(c As ToolpathContour, p As Pt2)
        If Not c.IsClosed Then Return
        Dim bestIdx As Integer = 0
        Dim best As Double = Double.MaxValue
        For i = 0 To c.Points.Count - 1
            Dim dd = p.DistanceTo(New Pt2(c.Points(i).X, c.Points(i).Y))
            If dd < best Then
                best = dd
                bestIdx = i
            End If
        Next
        If bestIdx = 0 Then Return
        Dim rotated As New List(Of Pt3)(c.Points.Count)
        rotated.AddRange(c.Points.GetRange(bestIdx, c.Points.Count - bestIdx))
        rotated.AddRange(c.Points.GetRange(0, bestIdx))
        c.Points = rotated
    End Sub
End Module
