' ============================================================================
'  Relief.vb
'  3D models on the blank (DrawingKind.Model). The imported mesh or depth
'  image is kept as a heightmap (ReliefModel, ModelImport.vb) embedded in the
'  drawing, placed and scaled like any drawing, with a depth of its own:
'
'    Width / Height   the placed box (Scale % is the same thing relative to
'                     the model's natural size)
'    Depth            Z range of the relief; follows the size (keeps the
'                     model's proportions) until it is typed in
'    Top below stock  how far under the stock top the highest point sits
'
'  Two toolpaths carve it: roughing (DesignObject.Machining) and finishing
'  (DesignObject.FinishMachining), built by ReliefEngine.vb in the model's own
'  frame and cached, so moving or rotating the model only transforms them.
' ============================================================================

Imports System.ComponentModel
Imports System.Globalization
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text.Json.Serialization
Imports System.Threading
Imports Clipper2Lib

Partial Public Class DesignObject
    ''' <summary>Rows of the Relief category (shown for 3D models only).</summary>
    Friend Shared ReadOnly ModelPropertyNames As New HashSet(Of String)(StringComparer.Ordinal) From {
        NameOf(ScalePercent), NameOf(Depth), NameOf(DepthFollowsSize), NameOf(TopOffset), NameOf(ModelUnits),
        NameOf(ModelUp), NameOf(MachineArea), NameOf(NaturalSize)}

    ' The decoded heightmap and the array it came from (decoding is not free: cache it).
    Private _relief As ReliefModel
    Private _reliefData As Byte()
    Private _reliefError As String
    Private _modelKey As String
    Private _modelKeyData As Byte()

    ''' <summary>Full path of the file the model was imported from (to read it again for another carved side).</summary>
    <Browsable(False)>
    Public Property ModelPath As String = ""

    ''' <summary>The heightmap (ReliefModel.ToBytes), so the project file is self-contained.</summary>
    <Browsable(False)>
    Public Property ModelData As Byte()

    ''' <summary>Depth typed in by the user (used when the depth does not follow the size).</summary>
    <Browsable(False)>
    Public Property ModelDepth As Double = 0.25

    ''' <summary>Stored state of Depth follows size.</summary>
    <Browsable(False)>
    Public Property ModelDepthFollowsSize As Boolean = True

    ''' <summary>A 3D model's finishing toolpath (its roughing is Machining). Nothing for other drawings.</summary>
    <Browsable(False), JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
    Public Property FinishMachining As Machining

    <Category("Relief"), DisplayName("Scale (%)"), JsonIgnore, RefreshProperties(RefreshProperties.All),
     Description("Size of the model relative to its natural size (100% = as modelled, in the model units below). Scales evenly about the centre.")>
    Public Property ScalePercent As Double
        Get
            Dim nw = NaturalWidthIn
            Return If(nw > 0, Math.Round(100.0 * Width / nw, 3), 100.0)
        End Get
        Set(value As Double)
            Dim nw = NaturalWidthIn, nh = NaturalHeightIn
            If Not (value > 0) OrElse Not Double.IsFinite(value) OrElse nw <= 0 OrElse nh <= 0 Then Return
            Dim centre = LocalToWorld(0, 0)
            Width = Math.Round(Math.Max(0.01, nw * value / 100.0), 4)
            Height = Math.Round(Math.Max(0.01, nh * value / 100.0), 4)
            PlaceLocal(0, 0, centre)
        End Set
    End Property

    <Category("Relief"), DisplayName("Depth (in)"), JsonIgnore, RefreshProperties(RefreshProperties.All),
     Description("Height of the relief from its lowest to its highest point. Follows the size while 'Depth follows size' is on; typing a depth turns that off.")>
    Public Property Depth As Double
        Get
            If ModelDepthFollowsSize Then Return Math.Round(NaturalDepthIn * SizeScale(), 4)
            Return ModelDepth
        End Get
        Set(value As Double)
            If Not (value > 0) OrElse Not Double.IsFinite(value) Then Return
            ModelDepth = Math.Round(value, 4)
            ModelDepthFollowsSize = False
        End Set
    End Property

    <Category("Relief"), DisplayName("Depth follows size"), JsonIgnore, RefreshProperties(RefreshProperties.All),
     Description("On: the depth scales with the width and height, so the model keeps its proportions. Off: the depth stays as typed.")>
    Public Property DepthFollowsSize As Boolean
        Get
            Return ModelDepthFollowsSize
        End Get
        Set(value As Boolean)
            ' Turning it off keeps the depth the model has now.
            If ModelDepthFollowsSize AndAlso Not value Then ModelDepth = Depth
            ModelDepthFollowsSize = value
        End Set
    End Property

    <Category("Relief"), DisplayName("Top below stock (in)"),
     Description("How far below the top of the stock the highest point of the relief sits. 0 = the highest point is at the stock top; more lowers the whole relief (the roughing pass clears the stock above it).")>
    Public Property TopOffset As Double = 0.0

    <Category("Relief"), DisplayName("Model units"), RefreshProperties(RefreshProperties.All),
     Description("Units the model was made in (an STL or OBJ file does not say). Changing them keeps the scale percentage, so the size changes. 3MF files and images bring their own size.")>
    Public Property ModelUnits As ModelUnits = ModelUnits.Millimeters

    <Category("Relief"), DisplayName("Carved side"), TypeConverter(GetType(ModelUpAxisConverter)), RefreshProperties(RefreshProperties.All),
     Description("Which side of the model faces the cutter. Found automatically on import (the flat back of a relief faces down); change it if the wrong side came up. Reads the model file again.")>
    Public Property ModelUp As ModelUpAxis = ModelUpAxis.PlusZ

    <Category("Relief"), DisplayName("Machine area"), TypeConverter(GetType(ReliefLimitConverter)),
     Description("Where the tool centre may go: inside the model's outline (the tool's edge still reaches its radius beyond it, down to the model's lowest level), or the whole box around the model.")>
    Public Property MachineArea As ReliefLimit = ReliefLimit.ModelOutline

    <Category("Relief"), DisplayName("Natural size"), JsonIgnore,
     Description("Size of the model as it was made (in the model units), and what it came from.")>
    Public ReadOnly Property NaturalSize As String
        Get
            Dim m = Relief
            If m Is Nothing Then Return If(_reliefError, "no model data")
            Dim ci = CultureInfo.InvariantCulture
            Dim u = UnitInches(ModelUnits)
            Dim own = If(ModelUnits = ModelUnits.Inches, "", String.Format(ci, " ({0:0.###} x {1:0.###} x {2:0.###} {3})",
                                                                            NaturalWidthIn / u, NaturalHeightIn / u, NaturalDepthIn / u, UnitName(ModelUnits)))
            Return String.Format(ci, "{0:0.###} x {1:0.###} x {2:0.###} in{3}, {4}, {5}", NaturalWidthIn, NaturalHeightIn, NaturalDepthIn, own, m.Format, m.Description)
        End Get
    End Property

    ''' <summary>The model's heightmap (decoded from ModelData on first use), or Nothing.</summary>
    <Browsable(False), JsonIgnore>
    Public ReadOnly Property Relief As ReliefModel
        Get
            If Kind <> DrawingKind.Model Then Return Nothing
            If _reliefData IsNot ModelData Then
                _reliefData = ModelData
                _relief = Nothing
                _reliefError = Nothing
                If ModelData Is Nothing OrElse ModelData.Length = 0 Then
                    _reliefError = "The model data is missing: import the model again."
                Else
                    Try
                        _relief = ReliefModel.FromBytes(ModelData)
                    Catch ex As Exception
                        _reliefError = "The model data cannot be read (" & ex.Message & "): import the model again."
                    End Try
                End If
            End If
            Return _relief
        End Get
    End Property

    ''' <summary>Natural size in inches in the chosen model units (0 without a model).</summary>
    <Browsable(False), JsonIgnore>
    Public ReadOnly Property NaturalWidthIn As Double
        Get
            Dim m = Relief
            Return If(m Is Nothing, 0.0, m.NaturalWidth * UnitRatio(m))
        End Get
    End Property

    <Browsable(False), JsonIgnore>
    Public ReadOnly Property NaturalHeightIn As Double
        Get
            Dim m = Relief
            Return If(m Is Nothing, 0.0, m.NaturalHeight * UnitRatio(m))
        End Get
    End Property

    <Browsable(False), JsonIgnore>
    Public ReadOnly Property NaturalDepthIn As Double
        Get
            Dim m = Relief
            Return If(m Is Nothing, 0.0, m.NaturalDepth * UnitRatio(m))
        End Get
    End Property

    ''' <summary>Placed size over natural size (the geometric mean when the aspect is not kept).</summary>
    Public Function SizeScale() As Double
        Dim nw = NaturalWidthIn, nh = NaturalHeightIn
        If nw <= 0 OrElse nh <= 0 Then Return 1.0
        Return Math.Sqrt((Width / nw) * (Height / nh))
    End Function

    ''' <summary>The model's own units against the units it was read in (images are always inches).</summary>
    Private Function UnitRatio(m As ReliefModel) As Double
        If m.IsImage Then Return 1.0
        Return UnitInches(ModelUnits) / UnitInches(m.Units)
    End Function

    ''' <summary>Text that changes whenever the model's heightmap changes.</summary>
    Public Function ModelKey() As String
        If ModelData Is Nothing Then Return "none"
        If _modelKeyData IsNot ModelData Then
            _modelKeyData = ModelData
            _modelKey = Convert.ToBase64String(SHA256.HashData(ModelData), 0, 12)
        End If
        Return _modelKey
    End Function

    ''' <summary>The model's outline as its natural shape (inches, lower-left at 0, 0).</summary>
    Private Function BuildModelShape() As SvgShapeSet
        Dim m = Relief
        If m Is Nothing Then Throw New InvalidDataException(_reliefError)
        Dim k = UnitRatio(m)
        Dim nw = m.NaturalWidth * k, nh = m.NaturalHeight * k
        Dim tol = Math.Max(0.002, Math.Max(m.NaturalWidth, m.NaturalHeight) / 1500.0)
        Dim shape As New SvgShapeSet With {.Width = nw, .Height = nh, .ElementCount = 1}
        For Each p In m.Footprint(tol)
            Dim q As New PathD(p.Count)
            For Each pt In p
                q.Add(New PointD(pt.x * k, pt.y * k))
            Next
            shape.Paths.Add(q)
        Next
        If shape.Paths.Count = 0 Then
            ' A model with no outline still needs something to pick and to show: its box.
            shape.Paths.Add(New PathD From {New PointD(0, 0), New PointD(nw, 0), New PointD(nw, nh), New PointD(0, nh)})
        End If
        shape.Warnings.AddRange(m.Warnings)
        Return shape
    End Function

    ''' <summary>Defaults for a 3D model's missing or impossible values (called from Repair).</summary>
    Private Sub RepairModel()
        If ModelPath Is Nothing Then ModelPath = ""
        If Kind <> DrawingKind.Model Then Return
        If Not Double.IsFinite(ModelDepth) OrElse ModelDepth <= 0 Then ModelDepth = 0.25
        If Not Double.IsFinite(TopOffset) OrElse TopOffset < 0 Then TopOffset = 0
        If Machining Is Nothing OrElse Machining.Operation <> CutOperation.Roughing3D Then Machining = Global.CarveMaker.Machining.NewRelief(CutOperation.Roughing3D)
        If FinishMachining Is Nothing OrElse FinishMachining.Operation <> CutOperation.Finishing3D Then FinishMachining = Global.CarveMaker.Machining.NewRelief(CutOperation.Finishing3D)
    End Sub

    ''' <summary>Inches per model unit.</summary>
    Public Shared Function UnitInches(u As ModelUnits) As Double
        Select Case u
            Case ModelUnits.Millimeters : Return 1.0 / 25.4
            Case ModelUnits.Centimeters : Return 1.0 / 2.54
            Case ModelUnits.Meters : Return 1.0 / 0.0254
            Case ModelUnits.Feet : Return 12.0
            Case Else : Return 1.0
        End Select
    End Function

    Private Shared Function UnitName(u As ModelUnits) As String
        Select Case u
            Case ModelUnits.Millimeters : Return "mm"
            Case ModelUnits.Centimeters : Return "cm"
            Case ModelUnits.Meters : Return "m"
            Case ModelUnits.Feet : Return "ft"
            Case Else : Return "in"
        End Select
    End Function

    ''' <summary>A new 3D model drawing from an imported heightmap: natural size, roughing and finishing toolpaths.</summary>
    Public Shared Function FromModel(m As ReliefModel, path As String) As DesignObject
        Dim o As New DesignObject With {
            .Kind = DrawingKind.Model,
            .Name = IO.Path.GetFileNameWithoutExtension(path),
            .SvgFileName = IO.Path.GetFileName(path),
            .ModelPath = path,
            .ModelData = m.ToBytes(),
            .ModelUnits = m.Units,
            .ModelUp = m.Up,
            .LockAspect = True,
            .Machining = Global.CarveMaker.Machining.NewRelief(CutOperation.Roughing3D),
            .FinishMachining = Global.CarveMaker.Machining.NewRelief(CutOperation.Finishing3D)}
        o._relief = m
        o._reliefData = o.ModelData
        o.Width = Math.Round(Math.Max(0.01, m.NaturalWidth), 4)
        o.Height = Math.Round(Math.Max(0.01, m.NaturalHeight), 4)
        Return o
    End Function

    ''' <summary>Replaces the heightmap (the model read again, e.g. with another carved side), keeping the scale.</summary>
    Public Sub ReplaceModel(m As ReliefModel)
        Dim pct = ScalePercent
        ModelData = m.ToBytes()
        _relief = m
        _reliefData = ModelData
        _reliefError = Nothing
        ModelUp = m.Up
        InvalidateShape()
        ScalePercent = pct
    End Sub
End Class

''' <summary>Carved side names in the property grid.</summary>
Public Class ModelUpAxisConverter
    Inherits FriendlyEnumConverter
    Public Sub New()
        MyBase.New(GetType(ModelUpAxis), New Dictionary(Of Object, String) From {
            {ModelUpAxis.PlusZ, "+Z (top)"}, {ModelUpAxis.MinusZ, "-Z (bottom)"},
            {ModelUpAxis.PlusY, "+Y (back)"}, {ModelUpAxis.MinusY, "-Y (front)"},
            {ModelUpAxis.PlusX, "+X (right)"}, {ModelUpAxis.MinusX, "-X (left)"}})
    End Sub
End Class

''' <summary>Turns placed 3D models into their roughing and finishing segments (JobBuilder step 0).</summary>
Friend Module ReliefJob
    Private ReadOnly Ci As CultureInfo = CultureInfo.InvariantCulture
    ''' <summary>Finest cell the engines get: finer heightmaps are reduced (cell maxima) to this.</summary>
    Private Const MinCell As Double = 0.002
    Private Const CacheSize As Integer = 4

    ''' <summary>A computed toolpath in the model's own frame (compact: a 3D finish can be millions of moves).</summary>
    Private Class CachedPass
        Public Key As String
        Public Kinds As Byte()
        Public X, Y, Z, Feed As Double()
        Public PassCount As Integer
        Public Warnings As List(Of String)
    End Class

    Private ReadOnly _cacheLock As New Object()
    Private ReadOnly _cache As New List(Of CachedPass)

    ''' <summary>Progress notes for tests (Nothing in the app).</summary>
    Friend Property Trace As Action(Of String)

    ''' <summary>Why a 3D model cannot be carved as set up (empty when it can).</summary>
    Friend Function Problems(o As DesignObject, s As CarveSettings) As List(Of String)
        Dim p As New List(Of String)
        If o.Relief Is Nothing Then
            o.EnsureParsed(s.CurveTolerance)
            p.Add(If(o.ParseError, "The model data is missing: import the model again."))
            Return p
        End If
        Dim rough = o.Machining, finish = o.FinishMachining
        Dim roughOn = rough IsNot Nothing AndAlso rough.Enabled
        Dim finishOn = finish IsNot Nothing AndAlso finish.Enabled
        If Not roughOn AndAlso Not finishOn Then p.Add("Both 3D passes are turned off.")
        If roughOn Then p.AddRange(rough.Problems(s.StockThickness).Select(Function(x) "Roughing: " & x))
        If finishOn Then p.AddRange(finish.Problems(s.StockThickness).Select(Function(x) "Finishing: " & x))
        If Not (o.Depth > 0) Then p.Add("The model is flat seen from its carved side (0 deep): pick another Carved side in the Relief rows.")
        Return p
    End Function

    ''' <summary>Roughing of every model, then every finishing pass, appended to the job as segments.</summary>
    Friend Sub AddPasses(tp As Toolpath, items As List(Of MachiningItem), s As CarveSettings, token As CancellationToken)
        ' Each model is one object in the status bar's count.
        tp.RegionCount += items.Count
        For Each it In items
            Dim o = it.Model
            If o.Machining.Enabled Then
                Dim moves = Run(tp, o, o.Machining, s, roughed:=False, token:=token)
                JobBuilder.AppendSegment(tp, "3D roughing: " & o.Name, CutOperation.Roughing3D, o.Machining.Tool, o.Machining.SpindleRpm, moves)
            End If
        Next
        For Each it In items
            Dim o = it.Model
            If o.FinishMachining IsNot Nothing AndAlso o.FinishMachining.Enabled Then
                Dim moves = Run(tp, o, o.FinishMachining, s, roughed:=o.Machining.Enabled, token:=token)
                JobBuilder.AppendSegment(tp, "3D finishing: " & o.Name, CutOperation.Finishing3D, o.FinishMachining.Tool, o.FinishMachining.SpindleRpm, moves)
            End If
        Next
        Trace?.Invoke("3D passes added: " & tp.Moves.Count & " moves")
        For Each it In items
            Dim o = it.Model
            Dim deepest = o.TopOffset + o.Depth
            If s.StockThickness > 0 AndAlso deepest > s.StockThickness + 0.000001 Then
                tp.Warnings.Add(String.Format(Ci, "{0}: the relief reaches {1:0.###}"" below the stock top, deeper than the {2:0.###}"" stock.", o.Name, deepest, s.StockThickness))
            End If
        Next
    End Sub

    ''' <summary>One pass of one model in machine coordinates (computed in the model's frame, cached).</summary>
    Private Function Run(tp As Toolpath, o As DesignObject, m As Machining, s As CarveSettings, roughed As Boolean, token As CancellationToken) As List(Of ToolMove)
        Dim isRough = m.Operation = CutOperation.Roughing3D
        Dim key = String.Join("|", o.ModelKey(), o.ModelUnits, o.Width.ToString("R", Ci), o.Height.ToString("R", Ci), o.Mirror,
                              o.Depth.ToString("R", Ci), o.TopOffset.ToString("R", Ci), o.MachineArea, m.Key(), roughed,
                              s.SafeZ.ToString("R", Ci), s.ClearanceZ.ToString("R", Ci))
        Dim pass As CachedPass = Nothing
        SyncLock _cacheLock
            pass = _cache.FirstOrDefault(Function(c) c.Key = key)
            If pass IsNot Nothing Then
                _cache.Remove(pass)
                _cache.Add(pass)
            End If
        End SyncLock

        Dim lay = Layout(o)
        Trace?.Invoke(If(isRough, "roughing", "finishing") & If(pass Is Nothing, ": computing", ": from the cache"))
        If pass Is Nothing Then
            Dim grid = BuildGrid(o, lay)
            Trace?.Invoke("grid built " & grid.Columns & " x " & grid.Rows)
            Dim opt As New ReliefPassOptions With {
                .Tool = m.Tool, .FeedRate = m.FeedRate, .PlungeRate = m.PlungeRate,
                .Stepover = m.EffectiveStepover(), .PassDepth = m.PassDepth,
                .Allowance = If(isRough, m.Allowance, 0.0), .AngleDeg = If(isRough, 0.0, m.RasterAngle),
                .Tolerance = If(isRough, 0.0005, m.Tolerance), .Limit = o.MachineArea,
                .SafeZ = s.SafeZ, .ClearanceZ = Math.Min(s.ClearanceZ, s.SafeZ)}
            Dim result = If(isRough, ReliefEngine.Roughing(grid, opt, token), ReliefEngine.Finishing(grid, opt, roughed, token))
            token.ThrowIfCancellationRequested()
            Trace?.Invoke("engine done: " & result.Moves.Count & " moves")
            pass = Compact(key, result)
            SyncLock _cacheLock
                _cache.Add(pass)
                While _cache.Count > CacheSize
                    _cache.RemoveAt(0)
                End While
            End SyncLock
        End If

        tp.ReliefPassCount += pass.PassCount
        For Each w In pass.Warnings
            tp.Warnings.Add(o.Name & If(isRough, " roughing: ", " finishing: ") & w)
        Next
        ' Model frame -> machine: the engine's grid is centred on the box (plus the reduction's overhang), then
        ' the box turns about its centre and sits at its place.
        Dim moves As New List(Of ToolMove)(pass.Kinds.Length)
        For i = 0 To pass.Kinds.Length - 1
            Dim w = o.LocalToWorld(pass.X(i) + lay.OffX, pass.Y(i) + lay.OffY)
            moves.Add(New ToolMove(CType(pass.Kinds(i), MoveKind), New Pt3(w.X, w.Y, pass.Z(i)), pass.Feed(i)))
        Next
        Return moves
    End Function

    Private Function Compact(key As String, r As ReliefResult) As CachedPass
        Dim n = r.Moves.Count
        Dim c As New CachedPass With {
            .Key = key, .Kinds = New Byte(n - 1) {}, .X = New Double(n - 1) {}, .Y = New Double(n - 1) {},
            .Z = New Double(n - 1) {}, .Feed = New Double(n - 1) {}, .PassCount = r.PassCount,
            .Warnings = New List(Of String)(r.Warnings)}
        For i = 0 To n - 1
            Dim mv = r.Moves(i)
            c.Kinds(i) = CByte(mv.Kind)
            c.X(i) = mv.Target.X : c.Y(i) = mv.Target.Y : c.Z(i) = mv.Target.Z
            c.Feed(i) = mv.Feed
        Next
        Return c
    End Function

    ''' <summary>How a model's heightmap becomes the engines' grid at the placed size.</summary>
    Friend Structure GridLayout
        ''' <summary>Heightmap cells per grid cell along each side (1 = no reduction).</summary>
        Public K As Integer
        Public Columns, Rows As Integer
        Public CellX, CellY As Double
        ''' <summary>Where the grid's centre sits in the box's frame (a reduced grid can overhang the box).</summary>
        Public OffX, OffY As Double
    End Structure

    ''' <summary>
    ''' Heightmaps finer than MinCell at the placed size are reduced by cell maxima (still never below
    ''' the model), keeping the engines' work in proportion to the size.
    ''' </summary>
    Friend Function Layout(o As DesignObject) As GridLayout
        Dim m = o.Relief
        Dim cols = m.Columns, rows = m.Rows
        Dim cell = Math.Min(o.Width / cols, o.Height / rows)
        Dim l As New GridLayout With {.K = If(cell >= MinCell, 1, CInt(Math.Ceiling(MinCell / cell)))}
        l.Columns = (cols + l.K - 1) \ l.K
        l.Rows = (rows + l.K - 1) \ l.K
        l.CellX = o.Width / cols * l.K
        l.CellY = o.Height / rows * l.K
        ' The reduced grid can overhang the box on its right / top: its centre moves by half the overhang.
        l.OffX = (l.Columns * l.CellX - o.Width) / 2
        l.OffY = (l.Rows * l.CellY - o.Height) / 2
        Return l
    End Function

    ''' <summary>
    ''' The model as the engines see it, in its own frame (centre 0, 0, not rotated): absolute Z of
    ''' each cell (the stock top is 0), uncovered cells at the base, mirrored when the drawing is.
    ''' </summary>
    Friend Function BuildGrid(o As DesignObject, lay As GridLayout) As ReliefGrid
        Dim m = o.Relief
        Dim cols = m.Columns, rows = m.Rows
        Dim k = lay.K, gc = lay.Columns, gr = lay.Rows
        Dim topZ = -o.TopOffset
        Dim depth = o.Depth
        Dim baseZ = topZ - depth
        Dim g As New ReliefGrid With {
            .Columns = gc, .Rows = gr, .CellX = lay.CellX, .CellY = lay.CellY,
            .Z = New Single(gc * gr - 1) {}, .Covered = New Boolean(gc * gr - 1) {},
            .CenterX = 0, .CenterY = 0, .RotationDeg = 0, .TopZ = topZ, .BaseZ = baseZ}
        Dim heights = m.Heights
        Dim mirror = o.Mirror
        Threading.Tasks.Parallel.For(0, gr,
            Sub(j)
                For i = 0 To gc - 1
                    Dim best = Single.NaN
                    For sj = j * k To Math.Min(rows, (j + 1) * k) - 1
                        For si = i * k To Math.Min(cols, (i + 1) * k) - 1
                            Dim src = If(mirror, cols - 1 - si, si)
                            Dim h = heights(sj * cols + src)
                            If Not Single.IsNaN(h) AndAlso (Single.IsNaN(best) OrElse h > best) Then best = h
                        Next
                    Next
                    Dim idx = j * gc + i
                    If Single.IsNaN(best) Then
                        g.Z(idx) = CSng(baseZ)
                    Else
                        g.Z(idx) = CSng(baseZ + best * depth)
                        g.Covered(idx) = True
                    End If
                Next
            End Sub)
        Return g
    End Function
End Module
