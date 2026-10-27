' ============================================================================
'  ReliefEngine.vb
'  Roughing and finishing toolpaths for a 3D relief, for any tool.
'
'  The relief is a grid of flat-topped columns (ReliefGrid) that is never
'  below the real model: each column is the highest point of the model over
'  its cell. Everything is computed against those columns, so a toolpath that
'  clears them clears the model.
'
'  Drop cutter: the lowest tip Z at a point is the largest
'  (column top - tool height at the column's nearest distance) over the
'  columns under the tool; ProfileHeight is the exact tool shape per type.
'
'  Finishing: parallel passes in the model's frame. Each pass is sampled
'  finer than a cell; where the surface bends between two points, or rises
'  steeply, the step is halved, and at the finest step a steep rise becomes a
'  staircase (up first, then across; across first, then down) so the tool's
'  edge never cuts into a wall it is approaching. Points are then dropped
'  ONE-SIDEDLY: a straight line may replace them only if it stays above every
'  one of them (by at most the smoothing tolerance), so the path only ever
'  rises. Passes join along the surface when the next one starts nearby,
'  otherwise the tool lifts to the clearance height.
'
'  Roughing: constant-Z levels. On a node grid every drop is widened by the
'  allowance and by a node diagonal (the tool grown sideways and down), so any
'  point within that diagonal of a node that is clear at a level is clear too.
'  At each level the nodes clear of the widened surface form the region; it is
'  cut by straight runs along node rows (zig-zag) and then by its boundary
'  (marching squares: every boundary segment lies in a square with a clear
'  corner) to clean the run ends against the walls.
'
'  Spiral pattern (both passes): an Archimedean spiral about the grid's
'  centre, r = stepover * theta / (2 pi), out to the farthest corner, made of
'  straight chords (sagitta within the tolerance). Finishing treats every
'  chord exactly like a raster line (sampled, clipped to the machine area,
'  refined; with the whole box, a lap along its sides follows); roughing
'  cuts the stretches of the spiral that are clear at each level (every
'  sample within half a node of the next), then the same boundary loops.
'  Outside in is the same path travelled backwards. The spiral turns
'  clockwise going out (anticlockwise coming in).
'
'  Coordinates: the engines work in the grid's own frame (origin at its
'  centre); moves are returned in machine coordinates (LocalToMachine).
' ============================================================================

Imports System.Threading
Imports System.Threading.Tasks

''' <summary>A relief placed on the machine, as the toolpath engines see it.</summary>
Public NotInheritable Class ReliefGrid
    Public Property Columns As Integer
    Public Property Rows As Integer
    ''' <summary>Cell size (inches) along the grid's local X (columns) and Y (rows).</summary>
    Public Property CellX As Double
    Public Property CellY As Double
    ''' <summary>
    ''' Absolute Z (inches; 0 = stock top) of the surface over each cell: the highest point of the
    ''' model over the cell. Cells without model hold BaseZ. Index row * Columns + column, row 0 at
    ''' local -Y, column 0 at local -X.
    ''' </summary>
    Public Property Z As Single()
    ''' <summary>True where the model covers the cell.</summary>
    Public Property Covered As Boolean()
    ''' <summary>Machine position of the grid centre and its rotation (counter-clockwise, degrees).</summary>
    Public Property CenterX As Double
    Public Property CenterY As Double
    Public Property RotationDeg As Double
    ''' <summary>Highest and lowest Z of the relief (TopZ &lt;= 0; BaseZ = TopZ - depth).</summary>
    Public Property TopZ As Double
    Public Property BaseZ As Double

    Public Function LocalToMachine(lx As Double, ly As Double) As Pt2
        Dim c = Math.Cos(RotationDeg * Math.PI / 180), s = Math.Sin(RotationDeg * Math.PI / 180)
        Return New Pt2(CenterX + lx * c - ly * s, CenterY + lx * s + ly * c)
    End Function

    Public Function MachineToLocal(x As Double, y As Double) As Pt2
        Dim c = Math.Cos(-RotationDeg * Math.PI / 180), s = Math.Sin(-RotationDeg * Math.PI / 180)
        Dim dx = x - CenterX, dy = y - CenterY
        Return New Pt2(dx * c - dy * s, dx * s + dy * c)
    End Function
End Class

Public Enum ReliefLimit
    ''' <summary>Tool centre stays over the model (over covered cells).</summary>
    ModelOutline
    ''' <summary>Tool centre covers the whole grid rectangle.</summary>
    WholeBox
End Enum

''' <summary>How a 3D pass covers the model.</summary>
Public Enum ReliefPattern
    ''' <summary>Straight passes side by side (finishing at the raster angle, roughing along the model's width).</summary>
    Raster
    ''' <summary>One Archimedean spiral about the model's centre.</summary>
    Spiral
End Enum

''' <summary>Which way a spiral pass travels.</summary>
Public Enum SpiralDirection
    ''' <summary>From the centre out to the edge.</summary>
    InsideOut
    ''' <summary>From the edge in to the centre (the same spiral, travelled backwards).</summary>
    OutsideIn
End Enum

Public Class ReliefPassOptions
    Public Property Tool As ToolDefinition
    Public Property FeedRate As Double
    Public Property PlungeRate As Double
    ''' <summary>Distance between neighbouring passes (inches).</summary>
    Public Property Stepover As Double
    ''' <summary>Roughing: Z step between levels. Finishing: Z step when the stock was not roughed (0 = one pass).</summary>
    Public Property PassDepth As Double
    ''' <summary>Roughing: material left on the relief (inches). Finishing ignores it.</summary>
    Public Property Allowance As Double
    ''' <summary>Raster direction in the grid's local frame (0 = along local X). Not used by a spiral.</summary>
    Public Property AngleDeg As Double
    ''' <summary>Raster (straight passes) or Spiral (about the grid's centre, Stepover between turns).</summary>
    Public Property Pattern As ReliefPattern = ReliefPattern.Raster
    ''' <summary>Spiral only: from the centre out, or from the edge in.</summary>
    Public Property Direction As SpiralDirection = SpiralDirection.InsideOut
    ''' <summary>Largest gap allowed above the computed tool surface when points are dropped (never below it).</summary>
    Public Property Tolerance As Double = 0.0005
    Public Property Limit As ReliefLimit = ReliefLimit.ModelOutline
    Public Property SafeZ As Double = 0.25
    Public Property ClearanceZ As Double = 0.05
End Class

Public Class ReliefResult
    Public Property Moves As New List(Of ToolMove)
    ''' <summary>Number of cutting passes (raster runs and contour loops).</summary>
    Public Property PassCount As Integer
    Public Property Warnings As New List(Of String)
End Class

Public Module ReliefEngine
    Private Const BlockSize As Integer = 8
    ''' <summary>Finest step of the finishing refinement (inches).</summary>
    Private Const MinStep As Double = 0.0003
    ''' <summary>Rises steeper than this (Z per inch) are refined; at the finest step they become staircases.</summary>
    Private Const SteepSlope As Double = 1.5
    ''' <summary>Bend of the surface between two points that is refined away (inches).</summary>
    Private Const BendTolerance As Double = 0.00001
    ''' <summary>A rise this large between two points is also checked at every eighth (S-shaped cliffs).</summary>
    Private Const CliffRise As Double = 0.002
    ''' <summary>
    ''' The finishing path sits this much above the computed surface: between two computed points
    ''' the tool's rounded side can bulge a few millionths of an inch past a straight line where
    ''' it runs along a steep wall.
    ''' </summary>
    Private Const FinishLift As Double = 0.0001
    ''' <summary>Rapid down to this far above a level already cut, then plunge.</summary>
    Private Const PlungeGap As Double = 0.02
    ''' <summary>Longest chord of a spiral (inches).</summary>
    Private Const MaxChord As Double = 0.25

    ' ================================================================ tool shape

    ''' <summary>
    ''' Height above the tip of the tool's lowest point at distance r from its axis (exact, for every
    ''' type; capped at the flute length, where the shank's flat underside begins);
    ''' Double.PositiveInfinity beyond Diameter / 2.
    ''' </summary>
    Public Function ProfileHeight(t As ToolDefinition, r As Double) As Double
        Return ToolShape.Of(t).Eval(r)
    End Function

    ''' <summary>A tool's profile for the drop cutter, optionally grown sideways (Shift) and downwards (Lower).</summary>
    Private NotInheritable Class ToolShape
        Public Angled As Boolean      ' V-bit, drill, tapered ball nose: tip ball + cone
        Public R As Double            ' radius
        Public EndR As Double         ' ball / corner / tip-ball radius
        Public FlatR As Double        ' flat bottom radius (end mills)
        Public TanA As Double         ' tangent of the cone's half angle
        Public JoinR, JoinH As Double ' where the tip ball meets the cone
        Public Flute As Double        ' flute length: the lowest point never sits higher
        Public Shift As Double
        Public Lower As Double

        Public Shared Function [Of](t As ToolDefinition, Optional shift As Double = 0, Optional lower As Double = 0) As ToolShape
            Dim s As New ToolShape With {.R = Math.Max(0.0001, t.Diameter / 2.0), .Flute = Math.Max(0.0001, t.Length), .Shift = Math.Max(0.0, shift), .Lower = Math.Max(0.0, lower)}
            If t.UsesAngle Then
                s.Angled = True
                Dim a = Math.Max(0.1, Math.Min(179.9, t.AngleDeg)) * Math.PI / 360.0
                s.TanA = Math.Tan(a)
                s.EndR = t.EffectiveEndRadius
                s.JoinR = s.EndR * Math.Cos(a)
                s.JoinH = s.EndR * (1 - Math.Sin(a))
            Else
                s.EndR = Math.Min(t.EffectiveEndRadius, s.R)
                s.FlatR = s.R - s.EndR
            End If
            Return s
        End Function

        ''' <summary>How far from the axis the grown tool reaches.</summary>
        Public ReadOnly Property Reach As Double
            Get
                Return R + Shift
            End Get
        End Property

        ''' <summary>Height of the grown tool's lowest point at distance r (PositiveInfinity beyond Reach).</summary>
        Public Function Eval(r As Double) As Double
            If r > R + Shift Then Return Double.PositiveInfinity
            Return Base(Math.Max(0.0, r - Shift)) - Lower
        End Function

        Private Function Base(r As Double) As Double
            Dim h As Double
            If Angled Then
                h = If(r <= JoinR, ArcRise(EndR, r), JoinH + (r - JoinR) / TanA)
            Else
                h = If(r <= FlatR, 0.0, ArcRise(EndR, r - FlatR))
            End If
            Return Math.Min(h, Flute)
        End Function

        ''' <summary>e - sqrt(e^2 - x^2) without cancellation.</summary>
        Private Shared Function ArcRise(e As Double, x As Double) As Double
            If x <= 0 Then Return 0.0
            If x >= e Then Return e
            Return x * x / (e + Math.Sqrt(e * e - x * x))
        End Function
    End Class

    ' ================================================================ surface

    ''' <summary>The columns in the grid's frame, with block maxima to skip what cannot touch the tool.</summary>
    Private NotInheritable Class Surface
        Public Cols, Rows As Integer
        Public Cx, Cy As Double
        Public X0, Y0, X1, Y1 As Double     ' the grid rectangle (local frame)
        Public Z As Single()
        Public Covered As Boolean()
        Public BaseZ As Double
        Public BCols, BRows As Integer
        Public BMax As Single()

        Public Sub New(cols As Integer, rows As Integer, cx As Double, cy As Double, x0 As Double, y0 As Double, z As Single(), covered As Boolean(), baseZ As Double)
            Me.Cols = cols : Me.Rows = rows : Me.Cx = cx : Me.Cy = cy
            Me.X0 = x0 : Me.Y0 = y0 : X1 = x0 + cols * cx : Y1 = y0 + rows * cy
            Me.Z = z : Me.Covered = covered : Me.BaseZ = baseZ
            BCols = (cols + BlockSize - 1) \ BlockSize
            BRows = (rows + BlockSize - 1) \ BlockSize
            BMax = New Single(BCols * BRows - 1) {}
            Dim zz = z, c = cols, r = rows, bc = BCols, bm = BMax
            Parallel.For(0, BRows,
                Sub(bj)
                    For bi = 0 To bc - 1
                        Dim m = Single.NegativeInfinity
                        For j = bj * BlockSize To Math.Min(r, (bj + 1) * BlockSize) - 1
                            For i = bi * BlockSize To Math.Min(c, (bi + 1) * BlockSize) - 1
                                If zz(j * c + i) > m Then m = zz(j * c + i)
                            Next
                        Next
                        bm(bj * bc + bi) = m
                    Next
                End Sub)
        End Sub

        Public Shared Function FromGrid(g As ReliefGrid) As Surface
            Return New Surface(g.Columns, g.Rows, g.CellX, g.CellY, -g.Columns * g.CellX / 2, -g.Rows * g.CellY / 2, g.Z, g.Covered, g.BaseZ)
        End Function

        ''' <summary>A coarser surface: k x k cells become one holding their maximum (still never below the model).</summary>
        Public Function Pooled(k As Integer) As Surface
            If k <= 1 Then Return Me
            Dim pc = (Cols + k - 1) \ k, pr = (Rows + k - 1) \ k
            Dim pz(pc * pr - 1) As Single, pcov(pc * pr - 1) As Boolean
            Dim zz = Z, cov = Covered, c = Cols, r = Rows, b = CSng(BaseZ)
            Parallel.For(0, pr,
                Sub(j)
                    For i = 0 To pc - 1
                        Dim m = b
                        Dim any = False
                        For sj = j * k To Math.Min(r, (j + 1) * k) - 1
                            For si = i * k To Math.Min(c, (i + 1) * k) - 1
                                If zz(sj * c + si) > m Then m = zz(sj * c + si)
                                If cov(sj * c + si) Then any = True
                            Next
                        Next
                        pz(j * pc + i) = m
                        pcov(j * pc + i) = any
                    Next
                End Sub)
            Return New Surface(pc, pr, Cx * k, Cy * k, X0, Y0, pz, pcov, BaseZ)
        End Function

        Public Function InsideBox(px As Double, py As Double) As Boolean
            Return px >= X0 AndAlso px <= X1 AndAlso py >= Y0 AndAlso py <= Y1
        End Function

        ''' <summary>True when the cell under the point is covered by the model.</summary>
        Public Function CoveredAt(px As Double, py As Double) As Boolean
            If Not InsideBox(px, py) Then Return False
            Dim i = Math.Min(Cols - 1, CInt(Math.Floor((px - X0) / Cx)))
            Dim j = Math.Min(Rows - 1, CInt(Math.Floor((py - Y0) / Cy)))
            Return Covered(j * Cols + i)
        End Function

        ''' <summary>Lowest tip Z at (px, py) at which the (grown) tool is clear of every column.</summary>
        Public Function Drop(px As Double, py As Double, sh As ToolShape) As Double
            Dim reach = sh.Reach
            ' Outside the grid the surface is the base, right under the tool.
            Dim best = If(InsideBox(px, py), Double.NegativeInfinity, BaseZ - sh.Eval(0))
            Dim i0 = CInt(Math.Floor((px - reach - X0) / Cx)), i1 = CInt(Math.Floor((px + reach - X0) / Cx))
            Dim j0 = CInt(Math.Floor((py - reach - Y0) / Cy)), j1 = CInt(Math.Floor((py + reach - Y0) / Cy))
            i0 = Math.Max(0, i0) : j0 = Math.Max(0, j0) : i1 = Math.Min(Cols - 1, i1) : j1 = Math.Min(Rows - 1, j1)
            If i0 > i1 OrElse j0 > j1 Then Return If(Double.IsNegativeInfinity(best), BaseZ + sh.Lower, best)
            Dim lower = sh.Lower
            ' The block under the tool first: it gives a good bound for skipping the rest.
            Dim ci = Math.Max(i0, Math.Min(i1, CInt(Math.Floor((px - X0) / Cx))))
            Dim cj = Math.Max(j0, Math.Min(j1, CInt(Math.Floor((py - Y0) / Cy))))
            Dim cbi = ci \ BlockSize, cbj = cj \ BlockSize
            best = BlockDrop(px, py, sh, cbi, cbj, i0, i1, j0, j1, best, lower)
            For bj = j0 \ BlockSize To j1 \ BlockSize
                For bi = i0 \ BlockSize To i1 \ BlockSize
                    If bi = cbi AndAlso bj = cbj Then Continue For
                    best = BlockDrop(px, py, sh, bi, bj, i0, i1, j0, j1, best, lower)
                Next
            Next
            If Double.IsNegativeInfinity(best) Then best = BaseZ + lower
            Return best
        End Function

        Private Function BlockDrop(px As Double, py As Double, sh As ToolShape, bi As Integer, bj As Integer,
                                   i0 As Integer, i1 As Integer, j0 As Integer, j1 As Integer, best As Double, lower As Double) As Double
            Dim reach = sh.Reach
            Dim bx0 = X0 + bi * BlockSize * Cx, by0 = Y0 + bj * BlockSize * Cy
            Dim bx1 = X0 + Math.Min(Cols, (bi + 1) * BlockSize) * Cx, by1 = Y0 + Math.Min(Rows, (bj + 1) * BlockSize) * Cy
            Dim bdx = Math.Max(0.0, Math.Max(bx0 - px, px - bx1)), bdy = Math.Max(0.0, Math.Max(by0 - py, py - by1))
            Dim bd = Math.Sqrt(bdx * bdx + bdy * bdy)
            If bd > reach Then Return best
            If BMax(bj * BCols + bi) - sh.Eval(bd) <= best Then Return best
            Dim reach2 = reach * reach
            For j = Math.Max(j0, bj * BlockSize) To Math.Min(j1, bj * BlockSize + BlockSize - 1)
                Dim cy0 = Y0 + j * Cy
                Dim dy = Math.Max(0.0, Math.Max(cy0 - py, py - (cy0 + Cy)))
                If dy > reach Then Continue For
                Dim row = j * Cols
                For i = Math.Max(i0, bi * BlockSize) To Math.Min(i1, bi * BlockSize + BlockSize - 1)
                    Dim zc As Double = Z(row + i)
                    If zc + lower <= best Then Continue For
                    Dim cx0 = X0 + i * Cx
                    Dim dx = Math.Max(0.0, Math.Max(cx0 - px, px - (cx0 + Cx)))
                    Dim d2 = dx * dx + dy * dy
                    If d2 > reach2 Then Continue For
                    Dim v = zc - sh.Eval(Math.Sqrt(d2))
                    If v > best Then best = v
                Next
            Next
            Return best
        End Function
    End Class

    ''' <summary>The lowest tip Z at machine (x, y) at which no part of the tool is below the surface.</summary>
    Public Function DropZ(g As ReliefGrid, t As ToolDefinition, x As Double, y As Double) As Double
        Dim p = g.MachineToLocal(x, y)
        Return Surface.FromGrid(g).Drop(p.X, p.Y, ToolShape.Of(t))
    End Function

    ' ================================================================ moves

    ''' <summary>Accumulates moves in the grid's frame and knows where the tool is.</summary>
    Private NotInheritable Class Path
        Public ReadOnly Moves As New List(Of ToolMove)
        Public Cur As Pt3
        Public Started As Boolean
        Private ReadOnly _g As ReliefGrid
        Private ReadOnly _o As ReliefPassOptions

        Public Sub New(g As ReliefGrid, o As ReliefPassOptions)
            _g = g : _o = o
        End Sub

        Private Sub Add(kind As MoveKind, p As Pt3, feed As Double)
            Moves.Add(New ToolMove(kind, p, feed))
            Cur = p
        End Sub

        Public Sub Feed(p As Pt3)
            If Started AndAlso Math.Abs(p.X - Cur.X) < 0.0000001 AndAlso Math.Abs(p.Y - Cur.Y) < 0.0000001 Then
                If Math.Abs(p.Z - Cur.Z) < 0.0000001 Then Return
                If p.Z < Cur.Z Then
                    Add(MoveKind.Plunge, p, _o.PlungeRate)
                    Return
                End If
            End If
            Add(MoveKind.Feed, p, _o.FeedRate)
        End Sub

        ''' <summary>Lifts to the clearance height (safe height for the first move), travels, and goes down to p.</summary>
        ''' <param name="clearDownTo">Height down to which the air above p is known to be clear (a rapid stops there), or NaN.</param>
        Public Sub Travel(p As Pt3, clearDownTo As Double)
            Dim clear = Math.Min(_o.ClearanceZ, _o.SafeZ)
            If Not Started Then
                Add(MoveKind.Rapid, New Pt3(p.X, p.Y, _o.SafeZ), 0)
                Started = True
                If _o.SafeZ > clear + 0.0000001 Then Add(MoveKind.Rapid, New Pt3(p.X, p.Y, clear), 0)
            Else
                If Cur.Z < clear - 0.0000001 Then Add(MoveKind.Rapid, New Pt3(Cur.X, Cur.Y, clear), 0)
                Add(MoveKind.Rapid, New Pt3(p.X, p.Y, clear), 0)
            End If
            If Not Double.IsNaN(clearDownTo) AndAlso clearDownTo < clear - 0.0000001 AndAlso clearDownTo > p.Z Then
                Add(MoveKind.Rapid, New Pt3(p.X, p.Y, clearDownTo), 0)
            End If
            If p.Z < Cur.Z - 0.0000001 Then Add(MoveKind.Plunge, p, _o.PlungeRate)
        End Sub

        Public Sub Finish()
            If Started AndAlso Cur.Z < _o.SafeZ - 0.0000001 Then Add(MoveKind.Rapid, New Pt3(Cur.X, Cur.Y, _o.SafeZ), 0)
        End Sub

        ''' <summary>The moves in machine coordinates.</summary>
        Public Function ToMachine() As List(Of ToolMove)
            For Each mv In Moves
                Dim w = _g.LocalToMachine(mv.Target.X, mv.Target.Y)
                mv.Target = New Pt3(w.X, w.Y, mv.Target.Z)
            Next
            Return Moves
        End Function
    End Class

    Private Function CheckInput(g As ReliefGrid, o As ReliefPassOptions, r As ReliefResult) As Boolean
        If g Is Nothing OrElse g.Columns <= 0 OrElse g.Rows <= 0 OrElse g.Z Is Nothing OrElse g.Z.Length < g.Columns * g.Rows OrElse
           Not (g.CellX > 0) OrElse Not (g.CellY > 0) Then
            r.Warnings.Add("there is no relief to cut.")
            Return False
        End If
        If o.Tool Is Nothing OrElse Not (o.Tool.Diameter > 0) Then
            r.Warnings.Add("choose a tool.")
            Return False
        End If
        If Not (o.Stepover > 0) Then
            r.Warnings.Add("the stepover must be more than 0.")
            Return False
        End If
        If o.Stepover > 0.9 * o.Tool.Diameter Then
            r.Warnings.Add(String.Format(Globalization.CultureInfo.InvariantCulture, "the stepover ({0:0.####}"") is more than 90% of the tool's diameter; 90% is used.", o.Stepover))
            o.Stepover = 0.9 * o.Tool.Diameter
        End If
        Dim deepest = -g.BaseZ
        If deepest > o.Tool.Length + 0.000001 Then
            r.Warnings.Add(String.Format(Globalization.CultureInfo.InvariantCulture,
                           "the relief goes {0:0.###}"" below the stock top but the {1}'s flutes are {2:0.###}"" long: the shank may rub on steep walls.",
                           deepest, o.Tool.DisplayName(), o.Tool.Length))
        End If
        Return True
    End Function

    ' ================================================================ finishing

    ''' <summary>One sampled point of a pass: position along the pass and tip Z.</summary>
    Private Structure PassPoint
        Public T As Double
        Public Z As Double
        Public Sub New(t As Double, z As Double)
            Me.T = t : Me.Z = z
        End Sub
    End Structure

    ''' <summary>A run of a finishing pass: points along the line origin + T * direction.</summary>
    Private NotInheritable Class Run
        Public Ox, Oy, Ux, Uy As Double
        Public Points As New List(Of PassPoint)
        Public Function At(i As Integer) As Pt3
            Dim p = Points(i)
            Return New Pt3(Ox + Ux * p.T, Oy + Uy * p.T, p.Z)
        End Function
    End Class

    Public Function Finishing(g As ReliefGrid, o As ReliefPassOptions, roughed As Boolean, token As CancellationToken) As ReliefResult
        Dim result As New ReliefResult()
        If Not CheckInput(g, o, result) Then Return result
        Dim surf = Surface.FromGrid(g)
        Dim sh = ToolShape.Of(o.Tool)
        Dim tol = Math.Max(0.00001, o.Tolerance)
        Dim ds = Math.Min(surf.Cx, surf.Cy) * 0.75
        Dim allowed As Func(Of Double, Double, Boolean) = Function(x As Double, y As Double) If(o.Limit = ReliefLimit.WholeBox, surf.InsideBox(x, y), surf.CoveredAt(x, y))

        ' The passes in cutting order: runs of allowed samples along straight lines, refined.
        Dim ordered = If(o.Pattern = ReliefPattern.Spiral,
                         SpiralRuns(surf, sh, o, tol, ds, allowed, token),
                         RasterRuns(surf, sh, o, ds, allowed, token))
        If ordered.Count = 0 Then
            result.Warnings.Add("nothing to cut inside the machine area.")
            Return result
        End If

        Dim path As New Path(g, o)
        Dim levels As New List(Of Double)
        If Not roughed AndAlso o.PassDepth > 0 Then
            ' Not roughed: take the material down in levels; the last one is the finish.
            Dim minZ = ordered.Min(Function(r) r.Points.Min(Function(p) p.Z))
            Dim z = -o.PassDepth
            While z > minZ + 0.000001
                levels.Add(z)
                z -= o.PassDepth
            End While
        End If
        levels.Add(Double.NegativeInfinity)
        Dim above As Double = 0
        Dim maxLink = Math.Max(1.0, 8 * o.Tool.Diameter)
        For Each level In levels
            token.ThrowIfCancellationRequested()
            For Each r In ordered
                For Each piece In Clamp(r, level, above)
                    Dim pts = Simplify(piece, tol)
                    Dim first = piece.At(0)
                    Dim startPt = New Pt3(first.X, first.Y, pts(0).Z)
                    ' A spiral's chords follow on from each other: one pass until the path breaks off.
                    If Not (path.Started AndAlso Math.Abs(path.Cur.X - startPt.X) < 0.0000001 AndAlso Math.Abs(path.Cur.Y - startPt.Y) < 0.0000001) Then result.PassCount += 1
                    If path.Started AndAlso CanLink(path.Cur, startPt, maxLink, allowed) Then
                        LinkAlongSurface(path, startPt, surf, sh, If(Double.IsNegativeInfinity(level), Double.NegativeInfinity, level))
                    Else
                        ' What the roughing left above the surface is unknown: plunge from the clearance height.
                        path.Travel(startPt, Double.NaN)
                    End If
                    For i = 1 To pts.Count - 1
                        path.Feed(New Pt3(piece.Ox + piece.Ux * pts(i).T, piece.Oy + piece.Uy * pts(i).T, pts(i).Z))
                    Next
                Next
            Next
            above = level
        Next
        path.Finish()
        result.Moves = path.ToMachine()
        Return result
    End Function

    ''' <summary>Finishing raster: lines at the raster angle, Stepover apart across the grid, every other one backwards.</summary>
    Private Function RasterRuns(surf As Surface, sh As ToolShape, o As ReliefPassOptions, ds As Double,
                                allowed As Func(Of Double, Double, Boolean), token As CancellationToken) As List(Of Run)
        Dim th = o.AngleDeg * Math.PI / 180
        Dim ux = Math.Cos(th), uy = Math.Sin(th)
        Dim vx = -uy, vy = ux
        Dim hw = (surf.X1 - surf.X0) / 2, hh = (surf.Y1 - surf.Y0) / 2
        Dim alongHalf = Math.Abs(hw * ux) + Math.Abs(hh * uy)
        Dim acrossHalf = Math.Abs(hw * vx) + Math.Abs(hh * vy)
        Dim nLines = Math.Max(1, CInt(Math.Ceiling(2 * acrossHalf / o.Stepover)))
        Dim stepAcross = 2 * acrossHalf / nLines

        ' Every line on its own thread.
        Dim lines(nLines - 1) As List(Of Run)
        Dim po As New ParallelOptions With {.CancellationToken = token}
        Parallel.For(0, nLines, po,
            Sub(k)
                po.CancellationToken.ThrowIfCancellationRequested()
                Dim off = -acrossHalf + (k + 0.5) * stepAcross
                lines(k) = SampleRuns(vx * off, vy * off, ux, uy, -alongHalf, alongHalf, ds, surf, sh, allowed)
            End Sub)
        token.ThrowIfCancellationRequested()

        ' Zig-zag: every other line runs backwards.
        Dim ordered As New List(Of Run)
        For k = 0 To nLines - 1
            Dim lineRuns = lines(k)
            If k Mod 2 = 1 Then
                lineRuns.Reverse()
                For Each r In lineRuns
                    r.Points.Reverse()
                Next
            End If
            ordered.AddRange(lineRuns)
        Next
        Return ordered
    End Function

    ''' <summary>
    ''' Finishing spiral: every chord of the spiral is a line like a raster line (sampled, clipped to the
    ''' machine area, refined), in order from the centre out; with the whole box as the machine area a lap
    ''' along the box's sides follows. Outside in reverses the order and every run, so the tool travels the
    ''' same path backwards (the lap first, then the spiral inwards).
    ''' </summary>
    Private Function SpiralRuns(surf As Surface, sh As ToolShape, o As ReliefPassOptions, tol As Double, ds As Double,
                                allowed As Func(Of Double, Double, Boolean), token As CancellationToken) As List(Of Run)
        Dim v = SpiralVertices(surf.X0, surf.Y0, surf.X1, surf.Y1, o.Stepover, tol)
        Dim chords(v.Count - 2) As List(Of Run)
        Dim po As New ParallelOptions With {.CancellationToken = token}
        Parallel.For(0, v.Count - 1, po,
            Sub(k)
                po.CancellationToken.ThrowIfCancellationRequested()
                Dim a = v(k), b = v(k + 1)
                Dim dx = b.X - a.X, dy = b.Y - a.Y
                Dim len = Math.Sqrt(dx * dx + dy * dy)
                ' A chord that misses the grid has no allowed point (both limits lie inside the grid).
                If len < 0.0000001 OrElse Not TouchesBox(a, b, surf.X0, surf.Y0, surf.X1, surf.Y1) Then Return
                chords(k) = SampleRuns(a.X, a.Y, dx / len, dy / len, 0, len, ds, surf, sh, allowed)
            End Sub)
        token.ThrowIfCancellationRequested()
        Dim ordered As New List(Of Run)
        For Each chordRuns In chords
            If chordRuns IsNot Nothing Then ordered.AddRange(chordRuns)
        Next
        If o.Limit = ReliefLimit.WholeBox AndAlso ordered.Count > 0 Then
            ' The box clips the turns, so along its sides strips up to a stepover wide are cut from one side
            ' only: one more lap along the sides, clockwise like the spiral, from the corner nearest its end.
            Dim last = ordered(ordered.Count - 1)
            Dim endPt = last.At(last.Points.Count - 1)
            Dim corners = {New Pt2(surf.X0, surf.Y0), New Pt2(surf.X0, surf.Y1), New Pt2(surf.X1, surf.Y1), New Pt2(surf.X1, surf.Y0)}
            Dim k0 = 0
            For q = 1 To 3
                If corners(q).DistanceTo(New Pt2(endPt.X, endPt.Y)) < corners(k0).DistanceTo(New Pt2(endPt.X, endPt.Y)) Then k0 = q
            Next
            For q = 0 To 3
                Dim a = corners((k0 + q) Mod 4), b = corners((k0 + q + 1) Mod 4)
                Dim len = a.DistanceTo(b)
                If len > 0.0000001 Then ordered.AddRange(SampleRuns(a.X, a.Y, (b.X - a.X) / len, (b.Y - a.Y) / len, 0, len, ds, surf, sh, allowed))
            Next
        End If
        If o.Direction = SpiralDirection.OutsideIn Then
            ordered.Reverse()
            For Each r In ordered
                r.Points.Reverse()
            Next
        End If
        Return ordered
    End Function

    ''' <summary>
    ''' One finishing line, origin + T * (ux, uy) for T from t0 to t1: sampled at most ds apart (both ends
    ''' included), cut into runs of samples inside the machine area; runs of two or more samples are refined.
    ''' </summary>
    Private Function SampleRuns(ox As Double, oy As Double, ux As Double, uy As Double, t0 As Double, t1 As Double, ds As Double,
                                surf As Surface, sh As ToolShape, allowed As Func(Of Double, Double, Boolean)) As List(Of Run)
        Dim nSamples = Math.Max(2, CInt(Math.Ceiling((t1 - t0) / ds)) + 1)
        Dim dsLine = (t1 - t0) / (nSamples - 1)
        Dim runs As New List(Of Run)
        Dim cur As Run = Nothing
        For s = 0 To nSamples - 1
            Dim t = t0 + s * dsLine
            Dim x = ox + ux * t, y = oy + uy * t
            If allowed(x, y) Then
                If cur Is Nothing Then
                    cur = New Run With {.Ox = ox, .Oy = oy, .Ux = ux, .Uy = uy}
                    runs.Add(cur)
                End If
                cur.Points.Add(New PassPoint(t, surf.Drop(x, y, sh) + FinishLift))
            Else
                cur = Nothing
            End If
        Next
        Dim done As New List(Of Run)
        For Each r In runs
            If r.Points.Count < 2 Then Continue For
            Refine(r, surf, sh)
            done.Add(r)
        Next
        Return done
    End Function

    ' ================================================================ spiral

    ''' <summary>
    ''' Vertices of the Archimedean spiral r = pitch * theta / (2 pi) about the local origin (the grid's
    ''' centre), from the centre out to the farthest corner of the rectangle plus half a pitch (so every point
    ''' of the rectangle is within half a pitch of a turn). It turns clockwise going out.
    ''' Each chord is as long as a sagitta of tol allows on the curve there (short near the centre, where
    ''' the spiral bends most), at most MaxChord and an eighth of a turn.
    ''' </summary>
    Private Function SpiralVertices(x0 As Double, y0 As Double, x1 As Double, y1 As Double, pitch As Double, tol As Double) As List(Of Pt2)
        Dim rx = Math.Max(Math.Abs(x0), Math.Abs(x1)), ry = Math.Max(Math.Abs(y0), Math.Abs(y1))
        Dim a = pitch / (2 * Math.PI)
        Dim thEnd = (Math.Sqrt(rx * rx + ry * ry) + pitch / 2) / a
        Dim pts As New List(Of Pt2) From {New Pt2(0, 0)}
        Dim th = 0.0
        While th < thEnd
            Dim r2 = (a * th) ^ 2, a2 = a * a
            ' Radius of curvature of r = a theta: (r^2 + a^2)^1.5 / (r^2 + 2 a^2); a / 2 at the centre, growing outwards,
            ' so the curvature where a chord starts bounds its sagitta: chord^2 / (8 rho).
            Dim rho = (r2 + a2) * Math.Sqrt(r2 + a2) / (r2 + 2 * a2)
            ' 0.9: the radius (and so the arc) grows along the chord, which the start's curvature does not see.
            Dim chord = Math.Min(MaxChord, 0.9 * Math.Sqrt(8 * rho * tol))
            th = Math.Min(thEnd, th + Math.Min(Math.PI / 4, chord / Math.Sqrt(r2 + a2)))
            Dim r = a * th
            pts.Add(New Pt2(r * Math.Cos(th), -r * Math.Sin(th)))
        End While
        Return pts
    End Function

    ''' <summary>True when the segment's bounding box meets the rectangle (otherwise no point of it is inside).</summary>
    Private Function TouchesBox(a As Pt2, b As Pt2, x0 As Double, y0 As Double, x1 As Double, y1 As Double) As Boolean
        Return Math.Max(a.X, b.X) >= x0 AndAlso Math.Min(a.X, b.X) <= x1 AndAlso Math.Max(a.Y, b.Y) >= y0 AndAlso Math.Min(a.Y, b.Y) <= y1
    End Function

    ''' <summary>A spiral as points at most a given step apart (roughing tests every one of them at each level).</summary>
    Private NotInheritable Class SpiralSamples
        Public ReadOnly X As New List(Of Double)
        Public ReadOnly Y As New List(Of Double)
        ''' <summary>The path turns here (a chord's end): kept when the samples become moves.</summary>
        Public ReadOnly Corner As New List(Of Boolean)
        ''' <summary>False where the sample does not follow on from the one before (chords off the grid are left out).</summary>
        Public ReadOnly Joined As New List(Of Boolean)

        Public Sub New(v As List(Of Pt2), stepMax As Double, x0 As Double, y0 As Double, x1 As Double, y1 As Double)
            Dim follows = False
            For k = 0 To v.Count - 2
                Dim a = v(k), b = v(k + 1)
                If Not TouchesBox(a, b, x0, y0, x1, y1) Then
                    follows = False
                    Continue For
                End If
                If Not follows Then Add(a.X, a.Y, True, False)
                Dim n = Math.Max(1, CInt(Math.Ceiling(a.DistanceTo(b) / stepMax)))
                For q = 1 To n - 1
                    Dim t = q / CDbl(n)
                    Add(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, False, True)
                Next
                Add(b.X, b.Y, True, True)
                follows = True
            Next
        End Sub

        Private Sub Add(px As Double, py As Double, isCorner As Boolean, isJoined As Boolean)
            X.Add(px) : Y.Add(py) : Corner.Add(isCorner) : Joined.Add(isJoined)
        End Sub

        ''' <summary>
        ''' The stretches whose samples are all inside (as start, corners and end, in spiral order; a lone
        ''' sample is a stretch of one point). Outside in: the stretches and their points backwards.
        ''' </summary>
        Public Function Stretches(isInside As Func(Of Double, Double, Boolean), outsideIn As Boolean) As List(Of List(Of Pt2))
            Dim runs As New List(Of List(Of Pt2))
            Dim n = X.Count
            Dim i = 0
            While i < n
                If Not isInside(X(i), Y(i)) Then
                    i += 1
                    Continue While
                End If
                Dim i1 = i
                While i1 + 1 < n AndAlso Joined(i1 + 1) AndAlso isInside(X(i1 + 1), Y(i1 + 1))
                    i1 += 1
                End While
                Dim run As New List(Of Pt2) From {New Pt2(X(i), Y(i))}
                For q = i + 1 To i1 - 1
                    If Corner(q) Then run.Add(New Pt2(X(q), Y(q)))
                Next
                If i1 > i Then run.Add(New Pt2(X(i1), Y(i1)))
                runs.Add(run)
                i = i1 + 1
            End While
            If outsideIn Then
                runs.Reverse()
                For Each r In runs
                    r.Reverse()
                Next
            End If
            Return runs
        End Function
    End Class

    ''' <summary>
    ''' Refines a run between its samples: halves steps where the surface bends above the straight
    ''' line or rises steeply; at the finest step a steep rise becomes a staircase.
    ''' </summary>
    Private Sub Refine(r As Run, surf As Surface, sh As ToolShape)
        Dim src = r.Points
        Dim outPts As New List(Of PassPoint)(src.Count + 16)
        outPts.Add(src(0))
        For i = 1 To src.Count - 1
            RefineSegment(r, src(i - 1), src(i), surf, sh, outPts, 0)
            outPts.Add(src(i))
        Next
        r.Points = outPts
    End Sub

    Private Sub RefineSegment(r As Run, a As PassPoint, b As PassPoint, surf As Surface, sh As ToolShape, outPts As List(Of PassPoint), depth As Integer)
        Dim len = Math.Abs(b.T - a.T)
        Dim rise = Math.Abs(b.Z - a.Z)
        Dim tm = (a.T + b.T) / 2
        Dim zm = surf.Drop(r.Ox + r.Ux * tm, r.Oy + r.Uy * tm, sh) + FinishLift
        Dim bends = zm > (a.Z + b.Z) / 2 + BendTolerance
        ' Where the surface rises a lot it can be S-shaped (a cliff, such as the model's edge with the whole
        ' box machined): the line clears the midpoint but not a point beside it. Look at every eighth too.
        If Not bends AndAlso rise > CliffRise AndAlso len / 2 >= MinStep Then
            For Each f In {0.125, 0.25, 0.375, 0.625, 0.75, 0.875}
                Dim tq = a.T + (b.T - a.T) * f
                If surf.Drop(r.Ox + r.Ux * tq, r.Oy + r.Uy * tq, sh) + FinishLift > a.Z + (b.Z - a.Z) * f + BendTolerance Then
                    bends = True
                    Exit For
                End If
            Next
        End If
        Dim steep = rise > SteepSlope * len + 0.0005
        If Not bends AndAlso Not steep Then Return
        If len / 2 >= MinStep AndAlso depth < 24 Then
            Dim m As New PassPoint(tm, zm)
            RefineSegment(r, a, m, surf, sh, outPts, depth + 1)
            outPts.Add(m)
            RefineSegment(r, m, b, surf, sh, outPts, depth + 1)
            Return
        End If
        ' Finest step: never cut diagonally into what is ahead. Go up first, or across first and then down,
        ' at the highest of the three heights known here.
        Dim top = Math.Max(Math.Max(a.Z, b.Z), zm)
        If top > a.Z + 0.0000001 Then outPts.Add(New PassPoint(a.T, top))
        If top > b.Z + 0.0000001 Then outPts.Add(New PassPoint(b.T, top))
    End Sub

    ''' <summary>
    ''' A run at a roughing level (finishing without roughing): Z no lower than the level, and only
    ''' the stretches where material is left below the level above (with a sample either side).
    ''' </summary>
    Private Function Clamp(r As Run, level As Double, above As Double) As List(Of Run)
        Dim pieces As New List(Of Run)
        If Double.IsNegativeInfinity(level) AndAlso above >= 0 Then
            pieces.Add(r)
            Return pieces
        End If
        Dim n = r.Points.Count
        Dim needs(n - 1) As Boolean
        For i = 0 To n - 1
            needs(i) = r.Points(i).Z < above - 0.0000001
        Next
        Dim i0 = 0
        While i0 < n
            While i0 < n AndAlso Not needs(i0)
                i0 += 1
            End While
            If i0 >= n Then Exit While
            Dim i1 = i0
            While i1 + 1 < n AndAlso needs(i1 + 1)
                i1 += 1
            End While
            Dim a = Math.Max(0, i0 - 1), b = Math.Min(n - 1, i1 + 1)
            ' Do not start or end on the second point of a staircase (same position).
            While a > 0 AndAlso r.Points(a).T = r.Points(a - 1).T
                a -= 1
            End While
            While b < n - 1 AndAlso r.Points(b).T = r.Points(b + 1).T
                b += 1
            End While
            Dim piece As New Run With {.Ox = r.Ox, .Oy = r.Oy, .Ux = r.Ux, .Uy = r.Uy}
            For i = a To b
                Dim p = r.Points(i)
                piece.Points.Add(New PassPoint(p.T, If(Double.IsNegativeInfinity(level), p.Z, Math.Max(p.Z, level))))
            Next
            If piece.Points.Count >= 2 Then pieces.Add(piece)
            i0 = b + 1
        End While
        Return pieces
    End Function

    ''' <summary>
    ''' Drops points of a run one-sidedly: a straight line replaces points only when it stays above
    ''' every one of them, by at most the tolerance. Vertical steps are kept.
    ''' </summary>
    Private Function Simplify(r As Run, tol As Double) As List(Of PassPoint)
        Dim p = r.Points
        Dim result As New List(Of PassPoint)(Math.Max(2, p.Count \ 4))
        Dim n = p.Count
        ' Distances are measured along the direction of travel (every other line runs backwards).
        Dim dir = If(n > 1 AndAlso p(n - 1).T < p(0).T, -1.0, 1.0)
        result.Add(p(0))
        Dim anchor = 0
        Dim i = 1
        While i < n
            ' A vertical step (same position) ends the current line and is kept as it is.
            If p(i).T = p(i - 1).T Then
                If anchor <> i - 1 Then result.Add(p(i - 1))
                result.Add(p(i))
                anchor = i
                i += 1
                Continue While
            End If
            ' Extend the line from the anchor as far as the slope window allows.
            Dim lo = Double.NegativeInfinity, hi = Double.PositiveInfinity
            Dim last = i
            Dim j = i
            While j < n
                If p(j).T = p(j - 1).T Then Exit While
                Dim dt = (p(j).T - p(anchor).T) * dir
                If dt <= 0 Then Exit While
                Dim s = (p(j).Z - p(anchor).Z) / dt
                If s < lo - 0.000000001 OrElse s > hi + 0.000000001 Then Exit While
                last = j
                ' Point j must stay below any later chord, by at most the tolerance.
                lo = Math.Max(lo, s)
                hi = Math.Min(hi, (p(j).Z + tol - p(anchor).Z) / dt)
                j += 1
            End While
            result.Add(p(last))
            anchor = last
            i = last + 1
        End While
        Return result
    End Function

    ''' <summary>A feed link along the surface is used when the next pass starts close by, over the machine area.</summary>
    Private Function CanLink(a As Pt3, b As Pt3, maxLink As Double, allowed As Func(Of Double, Double, Boolean)) As Boolean
        Dim len = Math.Sqrt((b.X - a.X) ^ 2 + (b.Y - a.Y) ^ 2)
        If len > maxLink Then Return False
        Dim n = Math.Max(1, CInt(Math.Ceiling(len / 0.005)))
        For k = 0 To n
            Dim t = k / CDbl(n)
            If Not allowed(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t) Then Return False
        Next
        Return True
    End Function

    ''' <summary>Feeds from the current point to b following the surface (refined like a pass, never lower than floorZ).</summary>
    Private Sub LinkAlongSurface(path As Path, b As Pt3, surf As Surface, sh As ToolShape, floorZ As Double)
        Dim a = path.Cur
        Dim len = Math.Sqrt((b.X - a.X) ^ 2 + (b.Y - a.Y) ^ 2)
        If len < 0.0000001 Then
            path.Feed(b)
            Return
        End If
        Dim r As New Run With {.Ox = a.X, .Oy = a.Y, .Ux = (b.X - a.X) / len, .Uy = (b.Y - a.Y) / len}
        Dim n = Math.Max(1, CInt(Math.Ceiling(len / (Math.Min(surf.Cx, surf.Cy) * 0.75))))
        For k = 0 To n
            Dim t = len * k / n
            Dim z As Double
            If k = 0 Then
                z = a.Z
            ElseIf k = n Then
                z = b.Z
            Else
                z = Math.Max(surf.Drop(r.Ox + r.Ux * t, r.Oy + r.Uy * t, sh) + FinishLift, floorZ)
            End If
            r.Points.Add(New PassPoint(t, z))
        Next
        Refine(r, surf, sh)
        ' The ends are where the tool is and where the pass starts; refinement may only add points between.
        If Not Double.IsNegativeInfinity(floorZ) Then
            For i = 0 To r.Points.Count - 1
                r.Points(i) = New PassPoint(r.Points(i).T, Math.Max(r.Points(i).Z, floorZ))
            Next
        End If
        Dim pts = Simplify(r, 0.0005)
        For i = 1 To pts.Count - 1
            path.Feed(New Pt3(r.Ox + r.Ux * pts(i).T, r.Oy + r.Uy * pts(i).T, pts(i).Z))
        Next
        If path.Cur.Z <> b.Z OrElse path.Cur.X <> b.X OrElse path.Cur.Y <> b.Y Then path.Feed(b)
    End Sub

    ' ================================================================ roughing

    Public Function Roughing(g As ReliefGrid, o As ReliefPassOptions, token As CancellationToken) As ReliefResult
        Dim result As New ReliefResult()
        If Not CheckInput(g, o, result) Then Return result
        Dim fine = Surface.FromGrid(g)
        ' Node spacing: an eighth of the tool, at most 0.01", never finer than the cells.
        Dim c = Math.Max(Math.Min(fine.Cx, fine.Cy), Math.Min(o.Tool.Diameter / 8, 0.01))
        Dim k = Math.Max(1, CInt(Math.Floor(c / Math.Max(fine.Cx, fine.Cy))))
        Dim surf = fine.Pooled(k)
        Dim delta = c * Math.Sqrt(2)
        Dim allowance = Math.Max(0.0, o.Allowance)
        Dim sh = ToolShape.Of(o.Tool, shift:=allowance + delta, lower:=allowance)
        Dim nx = Math.Max(1, CInt(Math.Ceiling((fine.X1 - fine.X0) / c)))
        Dim ny = Math.Max(1, CInt(Math.Ceiling((fine.Y1 - fine.Y0) / c)))
        Dim nodeX As Func(Of Integer, Double) = Function(i As Integer) fine.X0 + (i + 0.5) * c
        Dim nodeY As Func(Of Integer, Double) = Function(j As Integer) fine.Y0 + (j + 0.5) * c

        ' Widened drop and machine area at every node.
        Dim dz(nx * ny - 1) As Double
        Dim ok(nx * ny - 1) As Boolean
        Dim po As New ParallelOptions With {.CancellationToken = token}
        Parallel.For(0, ny, po,
            Sub(j)
                po.CancellationToken.ThrowIfCancellationRequested()
                Dim y = nodeY(j)
                For i = 0 To nx - 1
                    Dim x = nodeX(i)
                    ok(j * nx + i) = If(o.Limit = ReliefLimit.WholeBox, fine.InsideBox(x, y), fine.CoveredAt(x, y))
                    dz(j * nx + i) = surf.Drop(x, y, sh)
                Next
            End Sub)
        token.ThrowIfCancellationRequested()

        Dim minZ = Double.PositiveInfinity
        For i = 0 To dz.Length - 1
            If ok(i) AndAlso dz(i) < minZ Then minZ = dz(i)
        Next
        If Double.IsPositiveInfinity(minZ) OrElse minZ >= -0.0001 Then
            result.Warnings.Add("there is no material to rough above the relief.")
            Return result
        End If
        Dim levels As New List(Of Double)
        Dim pd = Math.Max(0.001, o.PassDepth)
        Dim zl = -pd
        While zl > minZ + 0.0001
            levels.Add(zl)
            zl -= pd
        End While
        levels.Add(minZ)

        Dim path As New Path(g, o)
        Dim rowStep = Math.Max(1, CInt(Math.Round(o.Stepover / c)))
        ' Spiral: samples at most half a node apart over the node lattice, the same at every level. A sample
        ' maps to the node of its lattice square (within half a diagonal), so a straight move between two clear
        ' samples stays within a node diagonal of a clear node: clear, like a run between row nodes.
        Dim spiral As SpiralSamples = Nothing
        If o.Pattern = ReliefPattern.Spiral Then
            Dim lx1 = fine.X0 + nx * c, ly1 = fine.Y0 + ny * c
            spiral = New SpiralSamples(SpiralVertices(fine.X0, fine.Y0, lx1, ly1, Math.Max(o.Stepover, c), Math.Max(0.00001, o.Tolerance)),
                                       c / 2, fine.X0, fine.Y0, lx1, ly1)
        End If
        Dim previous As Double = Double.NaN      ' the level above (its rows / spiral were cut at every node clear here)
        Dim inside(nx * ny - 1) As Boolean
        For Each z In levels
            token.ThrowIfCancellationRequested()
            For i = 0 To inside.Length - 1
                inside(i) = ok(i) AndAlso dz(i) <= z
            Next
            Dim isInside As Func(Of Double, Double, Boolean) = Function(x As Double, y As Double) As Boolean
                               Dim i = CInt(Math.Floor((x - fine.X0) / c)), j = CInt(Math.Floor((y - fine.Y0) / c))
                               If i < 0 OrElse j < 0 OrElse i >= nx OrElse j >= ny Then Return False
                               Return inside(j * nx + i)
                           End Function

            If spiral IsNot Nothing Then
                ' 1. The stretches of the spiral that are clear at this level, in spiral order. Every sample clear
                '    here was clear at the level above too, where the tool went through it.
                For Each stretch In spiral.Stretches(isInside, o.Direction = SpiralDirection.OutsideIn)
                    GoTo_(path, New Pt3(stretch(0).X, stretch(0).Y, z), z, previous, isInside, c, cutAbove:=True)
                    For q = 1 To stretch.Count - 1
                        path.Feed(New Pt3(stretch(q).X, stretch(q).Y, z))
                    Next
                    result.PassCount += 1
                Next
            Else
                ' 1. Runs along every rowStep-th node row, zig-zag.
                Dim forward = True
                For j = 0 To ny - 1 Step rowStep
                    Dim runs As New List(Of (A As Integer, B As Integer))
                    Dim i = 0
                    While i < nx
                        If inside(j * nx + i) Then
                            Dim i1 = i
                            While i1 + 1 < nx AndAlso inside(j * nx + i1 + 1)
                                i1 += 1
                            End While
                            runs.Add((i, i1))
                            i = i1 + 1
                        Else
                            i += 1
                        End If
                    End While
                    If runs.Count = 0 Then Continue For
                    If Not forward Then runs.Reverse()
                    For Each rn In runs
                        Dim ia = If(forward, rn.A, rn.B), ib = If(forward, rn.B, rn.A)
                        Dim a As New Pt3(nodeX(ia), nodeY(j), z), b As New Pt3(nodeX(ib), nodeY(j), z)
                        GoTo_(path, a, z, previous, isInside, c, cutAbove:=True)
                        path.Feed(b)
                        result.PassCount += 1
                    Next
                    forward = Not forward
                Next
            End If

            ' 2. The region's boundary, to clean the run ends against the walls.
            For Each contour In Contours(dz, ok, nx, ny, z, nodeX, nodeY)
                Dim loopPts = contour
                If loopPts.Count < 2 Then Continue For
                ' Start at the point nearest to the tool.
                Dim best = 0, bd = Double.MaxValue
                Dim closed = loopPts.Count > 2 AndAlso loopPts(0).DistanceTo(loopPts(loopPts.Count - 1)) < 0.0000001
                If closed Then
                    For q = 0 To loopPts.Count - 2
                        Dim d = (loopPts(q).X - path.Cur.X) ^ 2 + (loopPts(q).Y - path.Cur.Y) ^ 2
                        If d < bd Then bd = d : best = q
                    Next
                    Dim rot As New List(Of Pt2)
                    For q = 0 To loopPts.Count - 2
                        rot.Add(loopPts((best + q) Mod (loopPts.Count - 1)))
                    Next
                    rot.Add(rot(0))
                    loopPts = rot
                End If
                GoTo_(path, New Pt3(loopPts(0).X, loopPts(0).Y, z), z, previous, isInside, c, cutAbove:=False)
                For q = 1 To loopPts.Count - 1
                    path.Feed(New Pt3(loopPts(q).X, loopPts(q).Y, z))
                Next
                result.PassCount += 1
            Next
            previous = z
        Next
        path.Finish()
        result.Moves = path.ToMachine()
        Return result
    End Function

    ''' <summary>
    ''' Gets to the start of a roughing pass at level z: a straight feed when every point of the way
    ''' is near a clear node, otherwise up, across and down (a rapid down to just above the level
    ''' above when that level cut through the start: a row node, or a spiral sample).
    ''' </summary>
    Private Sub GoTo_(path As Path, p As Pt3, z As Double, previous As Double, isInside As Func(Of Double, Double, Boolean), c As Double, cutAbove As Boolean)
        If path.Started AndAlso Math.Abs(path.Cur.Z - z) < 0.0000001 Then
            Dim a = path.Cur
            Dim len = Math.Sqrt((p.X - a.X) ^ 2 + (p.Y - a.Y) ^ 2)
            Dim n = Math.Max(1, CInt(Math.Ceiling(len / (c / 2))))
            Dim clear = True
            For k = 0 To n
                Dim t = k / CDbl(n)
                If Not isInside(a.X + (p.X - a.X) * t, a.Y + (p.Y - a.Y) * t) Then clear = False : Exit For
            Next
            If clear Then
                path.Feed(p)
                Return
            End If
        End If
        Dim downTo = If(cutAbove AndAlso Not Double.IsNaN(previous), previous + PlungeGap, Double.NaN)
        path.Travel(p, downTo)
    End Sub

    ''' <summary>
    ''' Marching squares over the node lattice: the boundary of {nodes in the machine area whose widened
    ''' drop is at or below z}, as polylines (closed ones repeat their first point).
    ''' </summary>
    Private Function Contours(dz As Double(), ok As Boolean(), nx As Integer, ny As Integer, z As Double,
                              nodeX As Func(Of Integer, Double), nodeY As Func(Of Integer, Double)) As List(Of List(Of Pt2))
        ' Value per node: <= 0 inside. Outside the area: far outside.
        Dim v = Function(i As Integer, j As Integer) As Double
                    If i < 0 OrElse j < 0 OrElse i >= nx OrElse j >= ny Then Return 1.0
                    Dim idx = j * nx + i
                    Return If(ok(idx), dz(idx) - z, 1.0)
                End Function
        ' A crossing point on the edge between two nodes (one inside, one outside), keyed by the edge.
        Dim pointOf = Function(i0 As Integer, j0 As Integer, i1 As Integer, j1 As Integer) As Pt2
                          Dim a = v(i0, j0), b = v(i1, j1)
                          Dim t = If(Math.Abs(a - b) < 1.0E-12, 0.5, a / (a - b))
                          t = Math.Max(0.0, Math.Min(1.0, t))
                          Dim x0 = NodeAt(nodeX, i0, nx), x1 = NodeAt(nodeX, i1, nx)
                          Dim y0 = NodeAt(nodeY, j0, ny), y1 = NodeAt(nodeY, j1, ny)
                          Return New Pt2(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t)
                      End Function
        ' Edges: key = (node a index, node b index) packed; segments link edge keys.
        Dim nextOf As New Dictionary(Of Long, Long)
        Dim pts As New Dictionary(Of Long, Pt2)
        Dim key = Function(i0 As Integer, j0 As Integer, i1 As Integer, j1 As Integer) As Long
                      Dim a = CLng(j0 + 1) * (nx + 2) + (i0 + 1), b = CLng(j1 + 1) * (nx + 2) + (i1 + 1)
                      Return If(a < b, a * 100000000L + b, b * 100000000L + a)
                  End Function
        For j = -1 To ny - 1
            For i = -1 To nx - 1
                ' Square corners: 0 (i,j), 1 (i+1,j), 2 (i+1,j+1), 3 (i,j+1).
                Dim cin0 = v(i, j) <= 0, cin1 = v(i + 1, j) <= 0, cin2 = v(i + 1, j + 1) <= 0, cin3 = v(i, j + 1) <= 0
                Dim idx = If(cin0, 1, 0) Or If(cin1, 2, 0) Or If(cin2, 4, 0) Or If(cin3, 8, 0)
                If idx = 0 OrElse idx = 15 Then Continue For
                ' Edges: e0 bottom (0-1), e1 right (1-2), e2 top (3-2), e3 left (0-3).
                Dim e As Long() = {key(i, j, i + 1, j), key(i + 1, j, i + 1, j + 1), key(i, j + 1, i + 1, j + 1), key(i, j, i, j + 1)}
                Dim si = i, sj = j
                Dim ep = Function(n As Integer) As Pt2
                             Select Case n
                                 Case 0 : Return pointOf(si, sj, si + 1, sj)
                                 Case 1 : Return pointOf(si + 1, sj, si + 1, sj + 1)
                                 Case 2 : Return pointOf(si, sj + 1, si + 1, sj + 1)
                                 Case Else : Return pointOf(si, sj, si, sj + 1)
                             End Select
                         End Function
                ' Segments with the inside on their right, so they chain head to tail around each region.
                Dim segs As New List(Of (A As Integer, B As Integer))
                Select Case idx
                    Case 1 : segs.Add((3, 0))
                    Case 2 : segs.Add((0, 1))
                    Case 3 : segs.Add((3, 1))
                    Case 4 : segs.Add((1, 2))
                    Case 5 : segs.Add((3, 2)) : segs.Add((1, 0))
                    Case 6 : segs.Add((0, 2))
                    Case 7 : segs.Add((3, 2))
                    Case 8 : segs.Add((2, 3))
                    Case 9 : segs.Add((2, 0))
                    Case 10 : segs.Add((0, 3)) : segs.Add((2, 1))
                    Case 11 : segs.Add((2, 1))
                    Case 12 : segs.Add((1, 3))
                    Case 13 : segs.Add((1, 0))
                    Case 14 : segs.Add((0, 3))
                End Select
                For Each sg In segs
                    Dim ka = e(sg.A), kb = e(sg.B)
                    If Not pts.ContainsKey(ka) Then pts(ka) = ep(sg.A)
                    If Not pts.ContainsKey(kb) Then pts(kb) = ep(sg.B)
                    nextOf(ka) = kb
                Next
            Next
        Next
        ' Chain the segments.
        Dim loops As New List(Of List(Of Pt2))
        Dim hasPrev As New HashSet(Of Long)(nextOf.Values)
        Dim used As New HashSet(Of Long)
        ' Open chains first (start where nothing leads in), then closed loops.
        For Each startKey In nextOf.Keys.Where(Function(kk) Not hasPrev.Contains(kk)).Concat(nextOf.Keys).ToList()
            If used.Contains(startKey) Then Continue For
            Dim chain As New List(Of Pt2)
            Dim kk2 = startKey
            Do
                chain.Add(pts(kk2))
                used.Add(kk2)
                Dim nk As Long
                If Not nextOf.TryGetValue(kk2, nk) Then Exit Do
                If used.Contains(nk) Then
                    chain.Add(pts(nk))
                    Exit Do
                End If
                kk2 = nk
            Loop
            loops.Add(DropCollinear(chain))
        Next
        Return loops
    End Function

    Private Function NodeAt(f As Func(Of Integer, Double), i As Integer, n As Integer) As Double
        If n = 1 Then Return f(0) + If(i < 0, -1.0, If(i > 0, 1.0, 0.0)) * 0.01
        Dim stepN = f(1) - f(0)
        If i < 0 Then Return f(0) - stepN
        If i >= n Then Return f(n - 1) + stepN
        Return f(i)
    End Function

    ''' <summary>Removes points exactly on the straight line between their neighbours (the path does not move).</summary>
    Private Function DropCollinear(p As List(Of Pt2)) As List(Of Pt2)
        If p.Count < 3 Then Return p
        Dim r As New List(Of Pt2) From {p(0)}
        For i = 1 To p.Count - 2
            Dim a = r(r.Count - 1), b = p(i), c = p(i + 1)
            Dim cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)
            Dim dot = (b.X - a.X) * (c.X - b.X) + (b.Y - a.Y) * (c.Y - b.Y)
            If Math.Abs(cross) > 0.0000000001 OrElse dot < 0 Then r.Add(b)
        Next
        r.Add(p(p.Count - 1))
        Return r
    End Function
End Module
