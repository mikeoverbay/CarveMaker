' ============================================================================
'  Machining.vb
'  What happens to each item of the job (the text block, each drawing):
'
'    V-carve  carved with the V-bit under 2. Tool, together with every other
'             V-carved item (the original CarveMaker engine, VCarve.vb).
'    Pocket   the area is cleared to a flat floor with an end mill: offset
'             rings from the centre out to the wall, level by level.
'    Profile  the tool follows the outline: Outside (cut the shape out),
'             Inside (cut an opening) or On the line (engrave), level by
'             level, with optional holding tabs.
'
'  JobBuilder runs everything in machining order: V-carve, then pockets,
'  then profiles last (cut-outs free the part, so they go at the end), one
'  segment per tool. Coordinates are machine inches, Z0 = top of the stock.
'
'  Milling direction (spindle turning clockwise seen from above): climb
'  milling keeps the material being cut on the LEFT of the direction of
'  travel. Clipper outers are counter-clockwise (area > 0), holes clockwise.
'  Outside profiles: material is the part inside the loop -> natural order.
'  Pockets / inside profiles: material is the wall outside the loop ->
'  reversed. Conventional milling is the opposite of each.
' ============================================================================

Imports System.ComponentModel
Imports System.Drawing.Design
Imports System.Globalization
Imports System.Text.Json.Serialization
Imports System.Threading
Imports System.Windows.Forms.Design
Imports Clipper2Lib

Public Enum CutOperation
    VCarve
    Pocket
    Profile
End Enum

Public Enum ProfileSide
    Outside
    Inside
    OnLine
End Enum

''' <summary>Enum shown with readable names in the property grid (both directions).</summary>
Public MustInherit Class FriendlyEnumConverter
    Inherits EnumConverter

    Private ReadOnly _names As Dictionary(Of Object, String)

    Protected Sub New(t As Type, names As Dictionary(Of Object, String))
        MyBase.New(t)
        _names = names
    End Sub

    Public Overrides Function ConvertTo(context As ITypeDescriptorContext, culture As CultureInfo, value As Object, destinationType As Type) As Object
        Dim n As String = Nothing
        If destinationType Is GetType(String) AndAlso value IsNot Nothing AndAlso _names.TryGetValue(value, n) Then Return n
        Return MyBase.ConvertTo(context, culture, value, destinationType)
    End Function

    Public Overrides Function ConvertFrom(context As ITypeDescriptorContext, culture As CultureInfo, value As Object) As Object
        Dim s = TryCast(value, String)
        If s IsNot Nothing Then
            For Each kv In _names
                If String.Equals(kv.Value, s.Trim(), StringComparison.OrdinalIgnoreCase) Then Return kv.Key
            Next
        End If
        Return MyBase.ConvertFrom(context, culture, value)
    End Function
End Class

Public Class CutOperationConverter
    Inherits FriendlyEnumConverter
    Public Sub New()
        MyBase.New(GetType(CutOperation), New Dictionary(Of Object, String) From {
            {CutOperation.VCarve, "V-carve"}, {CutOperation.Pocket, "Pocket"}, {CutOperation.Profile, "Profile"}})
    End Sub
End Class

Public Class ProfileSideConverter
    Inherits FriendlyEnumConverter
    Public Sub New()
        MyBase.New(GetType(ProfileSide), New Dictionary(Of Object, String) From {
            {ProfileSide.Outside, "Outside"}, {ProfileSide.Inside, "Inside"}, {ProfileSide.OnLine, "On the line"}})
    End Sub
End Class

''' <summary>How one item (text block or drawing) is machined.</summary>
<TypeConverter(GetType(MachiningConverter))>
Public Class Machining
    Private Shared ReadOnly Ci As CultureInfo = CultureInfo.InvariantCulture

    ''' <summary>Extra depth below the stock for through cuts.</summary>
    Public Const ThroughOvercut As Double = 0.02

    Private _operation As CutOperation = CutOperation.VCarve

    <DisplayName("Operation"), RefreshProperties(RefreshProperties.All), TypeConverter(GetType(CutOperationConverter)),
     Description("V-carve: carved with the V-bit under 2. Tool. Pocket: the area is cleared to a flat floor with an end mill. Profile: the tool follows the outline (outside, inside or on the line), for example to cut the shape out of the board.")>
    Public Property Operation As CutOperation
        Get
            Return _operation
        End Get
        Set(value As CutOperation)
            _operation = value
            If value <> CutOperation.VCarve AndAlso Tool Is Nothing Then Tool = DefaultEndMill()
        End Set
    End Property

    <DisplayName("Tool"), Editor(GetType(MachiningToolEditor), GetType(UITypeEditor)),
     Description("Cutter for this pocket or profile. Click ... to choose it from the tool library (end mills; a V-bit can follow the line).")>
    Public Property Tool As ToolDefinition

    <DisplayName("Profile side"), TypeConverter(GetType(ProfileSideConverter)),
     Description("Outside: the tool runs outside the outline (cuts the shape out at its true size). Inside: inside the outline (cuts an opening). On the line: the tool centre follows the outline (engraving).")>
    Public Property Side As ProfileSide = ProfileSide.Outside

    <DisplayName("Cut through the stock"), RefreshProperties(RefreshProperties.All),
     Description("Cut the full stock thickness (4. Machine) plus 0.02"" so the shape comes free. Use tabs to hold it in place.")>
    Public Property ThroughCut As Boolean = False

    <DisplayName("Cut depth (in)"), Description("Final depth below the top of the stock.")>
    Public Property CutDepth As Double = 0.125

    <DisplayName("Pass depth (in)"), Description("Depth removed per pass. Deeper cuts are taken in several passes; about half the tool diameter suits wood.")>
    Public Property PassDepth As Double = 0.125

    <DisplayName("Stepover (%)"), Description("Distance between pocket passes, as a percentage of the tool diameter (40% is typical, 90% at most).")>
    Public Property StepoverPercent As Double = 40.0

    <DisplayName("Tabs per shape"), RefreshProperties(RefreshProperties.All),
     Description("Bridges of material left on each closed outline so a cut-out part stays put. 0 for none.")>
    Public Property TabCount As Integer = 0

    <DisplayName("Tab width (in)"), Description("Length of material each tab leaves along the cut.")>
    Public Property TabWidth As Double = 0.25

    <DisplayName("Tab thickness (in)"), Description("Height of the tabs above the bottom of the cut.")>
    Public Property TabThickness As Double = 0.1

    ''' <summary>The standard 1/4" flat end mill (same Id as in the library).</summary>
    Public Shared Function DefaultEndMill() As ToolDefinition
        Return ToolDefinition.Create(ToolType.FlatEndMill, 0.25, 0.75, id:="std-flat-0250")
    End Function

    ''' <summary>Depth of the bottom of the cut.</summary>
    Public Function BottomDepth(stockThickness As Double) As Double
        If Operation = CutOperation.Profile AndAlso ThroughCut Then Return Math.Max(0.0, stockThickness) + ThroughOvercut
        Return CutDepth
    End Function

    ''' <summary>Why a tool cannot do this operation (Nothing when it can).</summary>
    Public Function ToolProblem(t As ToolDefinition) As String
        If t Is Nothing Then Return "Choose a tool."
        Dim endMill = t.Type = ToolType.FlatEndMill OrElse t.Type = ToolType.BallNose OrElse t.Type = ToolType.BullNose
        Select Case Operation
            Case CutOperation.Pocket
                If Not endMill Then Return "Pockets need an end mill (flat, ball or bull nose)."
            Case CutOperation.Profile
                If t.Type = ToolType.VBit Then
                    If Side <> ProfileSide.OnLine Then Return "A V-bit can only follow the line: set Profile side to On the line, or pick an end mill."
                ElseIf Not endMill Then
                    Return "Profiles need an end mill (flat, ball or bull nose), or a V-bit on the line."
                End If
        End Select
        Return Nothing
    End Function

    ''' <summary>Problems that stop this item from being machined.</summary>
    Public Function Problems(stockThickness As Double) As List(Of String)
        Dim p As New List(Of String)
        If Operation = CutOperation.VCarve Then Return p
        Dim tp = ToolProblem(Tool)
        If tp IsNot Nothing Then p.Add(tp)
        If Tool IsNot Nothing Then
            For Each q In Tool.Problems()
                p.Add("Tool: " & q)
            Next
        End If
        If Not (BottomDepth(stockThickness) > 0) Then p.Add("Cut depth must be more than 0.")
        If Not (PassDepth > 0) Then p.Add("Pass depth must be more than 0.")
        If Operation = CutOperation.Pocket AndAlso (StepoverPercent < 5 OrElse StepoverPercent > 90) Then p.Add("Stepover must be between 5% and 90%.")
        If Operation = CutOperation.Profile AndAlso TabCount > 0 Then
            If TabCount > 50 Then p.Add("At most 50 tabs per shape.")
            If Not (TabWidth > 0) Then p.Add("Tab width must be more than 0.")
            If Not (TabThickness > 0) OrElse TabThickness >= BottomDepth(stockThickness) Then p.Add("Tab thickness must be more than 0 and less than the cut depth.")
        End If
        Return p
    End Function

    ''' <summary>One-line description, shown on the collapsed grid row.</summary>
    Public Function Summary() As String
        Select Case Operation
            Case CutOperation.Pocket
                Return "Pocket " & InchFormat.EditText(CutDepth) & """ deep, " & ToolName()
            Case CutOperation.Profile
                Dim side As String = If(Me.Side = ProfileSide.OnLine, "on the line", Me.Side.ToString().ToLowerInvariant())
                Dim depth = If(ThroughCut, "through", InchFormat.EditText(CutDepth) & """ deep")
                Return "Profile " & side & ", " & depth & If(TabCount > 0, ", " & TabCount & " tabs", "") & ", " & ToolName()
        End Select
        Return "V-carve"
    End Function

    Private Function ToolName() As String
        Return If(Tool Is Nothing, "no tool", Tool.DisplayName())
    End Function

    ''' <summary>Text that changes whenever anything affecting the toolpath changes.</summary>
    Public Function Key() As String
        Dim sb As New Text.StringBuilder()
        sb.Append(Operation).Append("|").Append(Side).Append("|").Append(ThroughCut).Append("|").
           Append(CutDepth.ToString("R", Ci)).Append("|").Append(PassDepth.ToString("R", Ci)).Append("|").
           Append(StepoverPercent.ToString("R", Ci)).Append("|").Append(TabCount).Append("|").
           Append(TabWidth.ToString("R", Ci)).Append("|").Append(TabThickness.ToString("R", Ci))
        If Tool IsNot Nothing Then
            sb.Append("|").Append(Tool.Type).Append("|").Append(Tool.Diameter.ToString("R", Ci)).Append("|").
               Append(Tool.Length.ToString("R", Ci)).Append("|").Append(Tool.EffectiveEndRadius.ToString("R", Ci)).Append("|").
               Append(Tool.AngleDeg.ToString("R", Ci))
        End If
        Return sb.ToString()
    End Function

    Public Function Clone() As Machining
        Dim c = DirectCast(MemberwiseClone(), Machining)
        c.Tool = Tool?.Clone()
        Return c
    End Function

    Public Overrides Function Equals(obj As Object) As Boolean
        Dim o = TryCast(obj, Machining)
        Return o IsNot Nothing AndAlso Key() = o.Key() AndAlso
               String.Equals(Tool?.Name, o.Tool?.Name, StringComparison.Ordinal)
    End Function

    Public Overrides Function GetHashCode() As Integer
        Return Key().GetHashCode()
    End Function

    Public Overrides Function ToString() As String
        Return Summary()
    End Function
End Class

''' <summary>Expandable in the grid, showing only the settings the chosen operation uses.</summary>
Public Class MachiningConverter
    Inherits ExpandableObjectConverter

    Public Overrides Function ConvertTo(context As ITypeDescriptorContext, culture As CultureInfo, value As Object, destinationType As Type) As Object
        Dim m = TryCast(value, Machining)
        If destinationType Is GetType(String) AndAlso m IsNot Nothing Then Return m.Summary()
        Return MyBase.ConvertTo(context, culture, value, destinationType)
    End Function

    Public Overrides Function GetProperties(context As ITypeDescriptorContext, value As Object, attributes As Attribute()) As PropertyDescriptorCollection
        Dim all = MyBase.GetProperties(context, value, attributes)
        Dim m = TryCast(value, Machining)
        If m Is Nothing Then Return all
        Dim keep As New HashSet(Of String) From {NameOf(Machining.Operation)}
        Select Case m.Operation
            Case CutOperation.Pocket
                keep.UnionWith({NameOf(Machining.Tool), NameOf(Machining.CutDepth), NameOf(Machining.PassDepth), NameOf(Machining.StepoverPercent)})
            Case CutOperation.Profile
                keep.UnionWith({NameOf(Machining.Tool), NameOf(Machining.Side), NameOf(Machining.ThroughCut), NameOf(Machining.PassDepth), NameOf(Machining.TabCount)})
                If Not m.ThroughCut Then keep.Add(NameOf(Machining.CutDepth))
                If m.TabCount > 0 Then keep.UnionWith({NameOf(Machining.TabWidth), NameOf(Machining.TabThickness)})
        End Select
        Dim shown = all.Cast(Of PropertyDescriptor)().Where(Function(p) keep.Contains(p.Name)).ToArray()
        Return New PropertyDescriptorCollection(shown)
    End Function
End Class

''' <summary>The "..." button on a pocket or profile Tool row: the tool library, filtered for that operation.</summary>
Public Class MachiningToolEditor
    Inherits UITypeEditor

    Public Overrides Function GetEditStyle(context As ITypeDescriptorContext) As UITypeEditorEditStyle
        Return UITypeEditorEditStyle.Modal
    End Function

    Public Overrides Function EditValue(context As ITypeDescriptorContext, provider As IServiceProvider, value As Object) As Object
        Dim m = TryCast(context?.Instance, Machining)
        If m Is Nothing Then Return value
        Dim current = TryCast(value, ToolDefinition)
        Dim svc = TryCast(provider?.GetService(GetType(IWindowsFormsEditorService)), IWindowsFormsEditorService)
        Dim title = If(m.Operation = CutOperation.Pocket, "Tool Library - choose the pocketing tool", "Tool Library - choose the profile tool")
        Using dlg As New frmToolLibrary(Function(t) m.ToolProblem(t), current, title)
            Dim result = If(svc IsNot Nothing, svc.ShowDialog(dlg), dlg.ShowDialog())
            If result = DialogResult.OK AndAlso dlg.SelectedTool IsNot Nothing Then
                If current IsNot Nothing AndAlso current.Id = dlg.SelectedTool.Id AndAlso current.Equals(dlg.SelectedTool) Then Return value
                Return dlg.SelectedTool.Clone()
            End If
        End Using
        Return value
    End Function
End Class

''' <summary>One item of the job: its placed shape (machine inches) and how to machine it.</summary>
Public Class MachiningItem
    Public Property Name As String
    Public Property Paths As PathsD
    Public Property Machining As Machining
    ''' <summary>True for the text block (its line centres steer the V-carve order).</summary>
    Public Property IsText As Boolean

    Public Sub New(name As String, paths As PathsD, machining As Machining, Optional isText As Boolean = False)
        Me.Name = name
        Me.Paths = paths
        Me.Machining = If(machining, New Machining())
        Me.IsText = isText
    End Sub
End Class

''' <summary>A cutting pass for the pocket/profile linker.</summary>
Friend Class MillPass
    ''' <summary>Tool tip positions; closed loops do not repeat the first point.</summary>
    Public Points As New List(Of Pt3)
    Public Closed As Boolean = True
    ''' <summary>Tool-centre region the tool may feed across to reach this pass at the current depth (Nothing: always lift).</summary>
    Public LinkRegion As PathsD
    ''' <summary>Longest in-material link before lifting instead.</summary>
    Public MaxLink As Double
    Public Kind As PassKind = PassKind.Pocket
End Class

''' <summary>Pocket and profile pass generation plus their linking into moves.</summary>
Public Module MillingEngine
    Private ReadOnly Ci As CultureInfo = CultureInfo.InvariantCulture
    ''' <summary>Chord error of offset arcs. Clipper puts the chords inside the true arc.</summary>
    Private Const ArcTolerance As Double = 0.0002
    ''' <summary>Extra clearance on every offset so the arc chords and simplification never cut into a wall.</summary>
    Private Const WallGap As Double = 0.0003

    ' ------------------------------------------------------------ pocket

    ''' <summary>
    ''' Offset-ring pocket, one connected area at a time (all levels), rings from the
    ''' centre out to the wall so the wall is finished last at every level.
    ''' </summary>
    Friend Function PocketPasses(region As PathsD, m As Machining, s As CarveSettings, name As String, lineCenters As List(Of Double), warnings As List(Of String)) As List(Of MillPass)
        Dim passes As New List(Of MillPass)
        Dim tool = m.Tool
        Dim r = tool.Diameter / 2.0
        Dim arcTol = ArcTolerance
        Dim stepLen = tool.Diameter * Math.Max(0.05, Math.Min(0.9, m.StepoverPercent / 100.0))
        Dim reverse = (s.Direction = MillingDirection.Climb)       ' material is the wall outside each ring
        Dim zLevels = Levels(m.BottomDepth(s.StockThickness), m.PassDepth)
        Dim area As Double = 0, covered As Double = 0
        Dim anyBoundary = False

        For Each component In VCarveEngine.SplitRegions(region, lineCenters, s.Order)
            area += Math.Abs(Clipper.Area(component))
            Dim boundary = Clipper.SimplifyPaths(Inflate(component, -(r + WallGap), arcTol), 0.0001)
            If boundary.Count = 0 Then Continue For
            anyBoundary = True
            covered += Math.Abs(Clipper.Area(Inflate(boundary, r, arcTol)))

            Dim rings As New List(Of PathsD)
            Dim d As Double = 0
            While rings.Count < 5000
                Dim ring = If(d = 0, boundary, Clipper.SimplifyPaths(Inflate(boundary, -d, arcTol), 0.0001))
                If ring.Count = 0 Then Exit While
                rings.Add(ring)
                d += stepLen
            End While
            ' Clean-up: wherever opposite rings ended more than a tool diameter apart (stepover over
            ' 50%, narrow necks) a stripe stays out of reach. Its outline lies inside the boundary and
            ' every point of a stripe is within half its width of that outline, so cutting along the
            ' outline clears it. Cut just before the wall ring.
            Dim allRings As New PathsD()
            For Each rg In rings
                allRings.AddRange(rg)
            Next
            Dim swept = Clipper.InflatePaths(allRings, r, JoinType.Round, EndType.Joined, 2.0, GlyphOutline.ClipperPrecision, arcTol)
            Dim residual = Clipper.Difference(boundary, swept, FillRule.NonZero, GlyphOutline.ClipperPrecision)
            ' Drop numerical slivers: anything that vanishes under a 0.001" inset is noise.
            Dim realResidual = Inflate(Inflate(residual, -0.001, arcTol), 0.001, arcTol)
            If realResidual.Count > 0 Then
                Dim cleanup = Clipper.SimplifyPaths(Clipper.Intersect(residual, Inflate(realResidual, 0.002, arcTol), FillRule.NonZero, GlyphOutline.ClipperPrecision), 0.0001)
                If cleanup.Count > 0 Then rings.Insert(Math.Min(1, rings.Count), cleanup)
            End If

            Dim pos As New Pt2(Double.NaN, Double.NaN)
            Dim levelIndex = 0
            For Each z In zLevels
                Dim firstOfLevel = True
                For k = rings.Count - 1 To 0 Step -1
                    Dim loops = rings(k).Where(Function(p) p.Count >= 3).ToList()
                    While loops.Count > 0
                        Dim bestI = 0
                        If Not Double.IsNaN(pos.X) Then
                            Dim bestD = Double.MaxValue
                            For i = 0 To loops.Count - 1
                                Dim dd = NearestVertexDistance(loops(i), pos)
                                If dd < bestD Then bestD = dd : bestI = i
                            Next
                        End If
                        Dim lp = loops(bestI)
                        loops.RemoveAt(bestI)
                        Dim pass = MakeLoop(lp, z, reverse, pos)
                        pass.LinkRegion = boundary
                        ' Going down a level the tool travels through the area it has just cleared.
                        pass.MaxLink = If(firstOfLevel AndAlso levelIndex > 0, Double.MaxValue, 3 * tool.Diameter)
                        pass.Kind = PassKind.Pocket
                        passes.Add(pass)
                        pos = New Pt2(pass.Points(0).X, pass.Points(0).Y)
                        firstOfLevel = False
                    End While
                Next
                levelIndex += 1
            Next
        Next

        If Not anyBoundary Then
            warnings.Add(name & ": every part is narrower than the " & tool.DisplayName() & "; nothing is pocketed.")
        ElseIf area > 0 AndAlso (area - covered) / area > 0.02 Then
            warnings.Add(String.Format(Ci, "{0}: {1:0}% of the area is narrower than the {2} and stays uncut.", name, 100 * (area - covered) / area, tool.DisplayName()))
        End If
        Return passes
    End Function

    Private Function Inflate(paths As PathsD, delta As Double, arcTol As Double) As PathsD
        Return Clipper.InflatePaths(paths, delta, JoinType.Round, EndType.Polygon, 2.0, GlyphOutline.ClipperPrecision, arcTol)
    End Function

    ' ------------------------------------------------------------ profile

    ''' <summary>Profile passes: each loop to full depth, holes before outers, optional tabs.</summary>
    Friend Function ProfilePasses(region As PathsD, m As Machining, s As CarveSettings, name As String, warnings As List(Of String)) As List(Of MillPass)
        Dim passes As New List(Of MillPass)
        Dim tool = m.Tool
        Dim r = If(m.Side = ProfileSide.OnLine, 0.0, tool.Diameter / 2.0 + WallGap)
        Dim arcTol = ArcTolerance
        Dim loops As PathsD
        Select Case m.Side
            Case ProfileSide.Outside : loops = Clipper.InflatePaths(region, r, JoinType.Round, EndType.Polygon, 2.0, GlyphOutline.ClipperPrecision, arcTol)
            Case ProfileSide.Inside : loops = Clipper.InflatePaths(region, -r, JoinType.Round, EndType.Polygon, 2.0, GlyphOutline.ClipperPrecision, arcTol)
            Case Else : loops = New PathsD(region)
        End Select
        loops = Clipper.SimplifyPaths(loops, 0.0001)
        If loops.Count = 0 Then
            warnings.Add(name & ": the shape is too small for an inside profile with the " & tool.DisplayName() & ".")
            Return passes
        End If
        If m.Side = ProfileSide.Inside AndAlso loops.Count < region.Count Then
            warnings.Add(name & ": some openings are smaller than the " & tool.DisplayName() & " and are skipped.")
        End If

        ' Outside: the part is inside the loop (natural order is climb). Inside: the wall is outside (reversed).
        Dim reverse As Boolean
        Select Case m.Side
            Case ProfileSide.Outside : reverse = (s.Direction = MillingDirection.Conventional)
            Case ProfileSide.Inside : reverse = (s.Direction = MillingDirection.Climb)
            Case Else : reverse = False
        End Select

        Dim bottom = m.BottomDepth(s.StockThickness)
        Dim zLevels = Levels(bottom, m.PassDepth)
        Dim tabTop As Double = Double.NegativeInfinity
        Dim tabLift As Double = 0
        If m.TabCount > 0 Then
            Dim partBottom = If(m.ThroughCut, s.StockThickness, m.CutDepth)
            tabTop = -(partBottom - m.TabThickness)
            tabLift = m.TabWidth + tool.Diameter
        End If

        ' Holes first (they must be cut while the part is still held), then outers; nearest first.
        Dim pending = loops.Where(Function(p) p.Count >= 2).ToList()
        Dim pos As New Pt2(Double.NaN, Double.NaN)
        Dim holesFirst = pending.Where(Function(p) Clipper.Area(p) < 0).ToList()
        Dim outers = pending.Where(Function(p) Clipper.Area(p) >= 0).ToList()
        For Each grp In {holesFirst, outers}
            While grp.Count > 0
                Dim bestI = 0
                If Not Double.IsNaN(pos.X) Then
                    Dim bestD = Double.MaxValue
                    For i = 0 To grp.Count - 1
                        Dim dd = NearestVertexDistance(grp(i), pos)
                        If dd < bestD Then bestD = dd : bestI = i
                    Next
                End If
                Dim lp = grp(bestI)
                grp.RemoveAt(bestI)
                Dim firstOfLoop = True
                For Each z In zLevels
                    Dim pass = MakeLoop(lp, z, reverse, pos)
                    If m.TabCount > 0 AndAlso z < tabTop - 0.000001 Then
                        Dim lifted = ApplyTabs(pass.Points, tabTop, m.TabCount, tabLift)
                        If lifted Is Nothing Then
                            If firstOfLoop Then warnings.Add(name & ": an outline is too short for " & m.TabCount & " tabs; it is cut without tabs.")
                        Else
                            pass.Points = lifted
                        End If
                    End If
                    ' Every level after the first starts where the last one ended: plunge in place.
                    pass.LinkRegion = If(firstOfLoop, Nothing, New PathsD())
                    pass.MaxLink = If(firstOfLoop, 0, 0.000001)
                    pass.Kind = PassKind.Profile
                    passes.Add(pass)
                    pos = New Pt2(pass.Points(0).X, pass.Points(0).Y)
                    firstOfLoop = False
                Next
            End While
        Next
        Return passes
    End Function

    ''' <summary>Depth levels from the first pass down to the bottom (negative Z).</summary>
    Friend Function Levels(bottom As Double, passDepth As Double) As List(Of Double)
        Dim list As New List(Of Double)
        If Not (bottom > 0) Then Return list
        Dim p = Math.Max(0.001, passDepth)
        Dim n = Math.Max(1, CInt(Math.Ceiling(bottom / p - 0.000001)))
        For k = 1 To n
            list.Add(-Math.Min(bottom, k * bottom / n))
        Next
        Return list
    End Function

    ''' <summary>Closed loop at depth z, oriented, starting at the vertex nearest pos.</summary>
    Private Function MakeLoop(path As PathD, z As Double, reverse As Boolean, pos As Pt2) As MillPass
        Dim pts As New List(Of Pt2)(path.Count)
        For Each p In path
            pts.Add(New Pt2(p.x, p.y))
        Next
        If reverse Then pts.Reverse()
        Dim startI = 0
        If Not Double.IsNaN(pos.X) Then
            Dim best = Double.MaxValue
            For i = 0 To pts.Count - 1
                Dim d = (pts(i).X - pos.X) ^ 2 + (pts(i).Y - pos.Y) ^ 2
                If d < best Then best = d : startI = i
            Next
        End If
        Dim pass As New MillPass()
        For i = 0 To pts.Count - 1
            Dim q = pts((startI + i) Mod pts.Count)
            pass.Points.Add(New Pt3(q.X, q.Y, z))
        Next
        Return pass
    End Function

    Private Function NearestVertexDistance(path As PathD, pos As Pt2) As Double
        Dim best = Double.MaxValue
        For Each p In path
            Dim d = (p.x - pos.X) ^ 2 + (p.y - pos.Y) ^ 2
            If d < best Then best = d
        Next
        Return best
    End Function

    ''' <summary>
    ''' Lifts a closed constant-Z loop to tabTop over tabCount stretches of length
    ''' tabLift (tab width + tool diameter), evenly spaced, the first half a spacing
    ''' from the start. Returns Nothing when the loop is too short for the tabs.
    ''' </summary>
    Friend Function ApplyTabs(loopPts As List(Of Pt3), tabTop As Double, tabCount As Integer, tabLift As Double) As List(Of Pt3)
        Dim n = loopPts.Count
        If n < 2 Then Return Nothing
        Dim perim As Double = 0
        For i = 0 To n - 1
            perim += Dist2D(loopPts(i), loopPts((i + 1) Mod n))
        Next
        Dim spacing = perim / tabCount
        If spacing < tabLift * 1.5 Then Return Nothing
        Dim z = loopPts(0).Z
        Dim starts As New List(Of Double), ends As New List(Of Double)
        For i = 0 To tabCount - 1
            Dim c = (i + 0.5) * spacing
            starts.Add(c - tabLift / 2) : ends.Add(c + tabLift / 2)
        Next
        Dim inTab = Function(sv As Double) As Boolean
                        For i = 0 To starts.Count - 1
                            If sv > starts(i) + 0.0000001 AndAlso sv < ends(i) - 0.0000001 Then Return True
                        Next
                        Return False
                    End Function

        Dim result As New List(Of Pt3)
        result.Add(loopPts(0))
        Dim sPos As Double = 0
        For i = 0 To n - 1
            Dim a = loopPts(i), b = loopPts((i + 1) Mod n)
            Dim len = Dist2D(a, b)
            If len < 0.0000001 Then Continue For
            ' Tab boundaries crossed by this edge, in order.
            Dim cuts As New List(Of Double)
            For Each sv In starts.Concat(ends)
                If sv > sPos + 0.0000001 AndAlso sv < sPos + len - 0.0000001 Then cuts.Add(sv)
            Next
            cuts.Sort()
            For Each sv In cuts
                Dim f = (sv - sPos) / len
                Dim x = a.X + (b.X - a.X) * f, y = a.Y + (b.Y - a.Y) * f
                Dim entering = starts.Contains(sv)
                result.Add(New Pt3(x, y, If(entering, z, tabTop)))
                result.Add(New Pt3(x, y, If(entering, tabTop, z)))
            Next
            sPos += len
            Dim endZ = If(i = n - 1, z, If(inTab(sPos), tabTop, z))
            If i < n - 1 Then result.Add(New Pt3(b.X, b.Y, endZ))
        Next
        Return result
    End Function

    Private Function Dist2D(a As Pt3, b As Pt3) As Double
        Return Math.Sqrt((b.X - a.X) ^ 2 + (b.Y - a.Y) ^ 2)
    End Function

    ''' <summary>True when the point lies inside a normalized region (outers CCW, holes CW).</summary>
    Friend Function InsideRegion(region As PathsD, x As Double, y As Double) As Boolean
        Dim winding = 0
        Dim pt As New PointD(x, y)
        For Each p In region
            If Clipper.PointInPolygon(pt, p, GlyphOutline.ClipperPrecision) <> PointInPolygonResult.IsOutside Then
                winding += If(Clipper.Area(p) >= 0, 1, -1)
            End If
        Next
        Return winding > 0
    End Function

    Private Function SegmentInside(region As PathsD, a As Pt3, b As Pt3) As Boolean
        Dim len = Dist2D(a, b)
        Dim n = Math.Max(1, CInt(Math.Ceiling(len / 0.01)))
        For k = 0 To n
            Dim t = k / CDbl(n)
            If Not InsideRegion(region, a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t) Then Return False
        Next
        Return True
    End Function

    ' ------------------------------------------------------------ linking

    ''' <summary>
    ''' Turns passes into moves: in-material feed links where the pass allows it,
    ''' otherwise lift to the clearance height (safe height before the first pass and
    ''' after the last), rapid across and plunge. Appends to moves and contours.
    ''' </summary>
    Friend Sub LinkPasses(passes As List(Of MillPass), s As CarveSettings, moves As List(Of ToolMove), contours As List(Of ToolpathContour), regionIndex As Integer)
        If passes.Count = 0 Then Return
        Dim safe = s.SafeZ
        Dim clear = Math.Min(s.ClearanceZ, safe)
        Dim cur As New Pt3(Double.NaN, Double.NaN, safe)
        Dim first = True
        For Each p In passes
            If p.Points.Count = 0 Then Continue For
            Dim start = p.Points(0)
            Dim linked = False
            If Not first AndAlso cur.Z <= 0 AndAlso p.LinkRegion IsNot Nothing Then
                Dim dxy = Math.Sqrt((start.X - cur.X) ^ 2 + (start.Y - cur.Y) ^ 2)
                If dxy <= p.MaxLink AndAlso (dxy < 0.000001 OrElse SegmentInside(p.LinkRegion, cur, New Pt3(start.X, start.Y, cur.Z))) Then
                    If dxy >= 0.000001 Then
                        cur = New Pt3(start.X, start.Y, cur.Z)
                        moves.Add(New ToolMove(MoveKind.Feed, cur, s.FeedRate))
                    End If
                    If start.Z < cur.Z - 0.0000001 Then
                        moves.Add(New ToolMove(MoveKind.Plunge, start, s.PlungeRate))
                    ElseIf start.Z > cur.Z + 0.0000001 Then
                        moves.Add(New ToolMove(MoveKind.Feed, start, s.FeedRate))
                    End If
                    cur = start
                    linked = True
                End If
            End If
            If Not linked Then
                Dim travel = If(first, safe, clear)
                If Not first AndAlso cur.Z < travel - 0.0000001 Then
                    cur = New Pt3(cur.X, cur.Y, travel)
                    moves.Add(New ToolMove(MoveKind.Rapid, cur, 0))
                End If
                cur = New Pt3(start.X, start.Y, travel)
                moves.Add(New ToolMove(MoveKind.Rapid, cur, 0))
                If travel > clear + 0.0000001 Then
                    cur = New Pt3(start.X, start.Y, clear)
                    moves.Add(New ToolMove(MoveKind.Rapid, cur, 0))
                End If
                moves.Add(New ToolMove(MoveKind.Plunge, start, s.PlungeRate))
                cur = start
            End If
            first = False
            For k = 1 To p.Points.Count - 1
                Dim nxt = p.Points(k)
                Dim kind = If(Math.Abs(nxt.X - cur.X) < 0.0000001 AndAlso Math.Abs(nxt.Y - cur.Y) < 0.0000001 AndAlso nxt.Z < cur.Z, MoveKind.Plunge, MoveKind.Feed)
                moves.Add(New ToolMove(kind, nxt, If(kind = MoveKind.Plunge, s.PlungeRate, s.FeedRate)))
                cur = nxt
            Next
            If p.Closed AndAlso p.Points.Count > 1 Then
                Dim back = New Pt3(start.X, start.Y, start.Z)
                If cur.Z <> back.Z AndAlso Math.Abs(cur.X - back.X) < 0.0000001 AndAlso Math.Abs(cur.Y - back.Y) < 0.0000001 Then
                    moves.Add(New ToolMove(MoveKind.Plunge, back, s.PlungeRate))
                Else
                    moves.Add(New ToolMove(MoveKind.Feed, back, s.FeedRate))
                End If
                cur = back
            End If
            contours.Add(New ToolpathContour With {
                .Points = New List(Of Pt3)(p.Points), .IsClosed = p.Closed, .Kind = p.Kind, .Z = p.Points.Min(Function(q) q.Z), .RegionIndex = regionIndex})
        Next
        If cur.Z < safe - 0.0000001 Then moves.Add(New ToolMove(MoveKind.Rapid, New Pt3(cur.X, cur.Y, safe), 0))
    End Sub
End Module

''' <summary>Builds the whole job: V-carve, pockets, profiles, one segment per tool.</summary>
Public Module JobBuilder
    Private ReadOnly Ci As CultureInfo = CultureInfo.InvariantCulture

    Public Function Generate(lines As IList(Of TextLine), items As IList(Of MachiningItem), s As CarveSettings, token As CancellationToken) As Toolpath
        Dim tp As New Toolpath()
        tp.BlankMinX = s.BlankOriginX
        tp.BlankMinY = s.BlankOriginY
        tp.BlankMaxX = s.BlankOriginX + Math.Max(0.0, s.BlankWidthIn)
        tp.BlankMaxY = s.BlankOriginY + Math.Max(0.0, s.BlankHeightIn)

        Dim all As New List(Of MachiningItem)
        Dim lineCenters As List(Of Double) = Nothing
        If lines IsNot Nothing AndAlso lines.Any(Function(l) Not String.IsNullOrWhiteSpace(l.Text)) Then
            Dim textShape = GlyphOutline.BuildShape(lines, s, tp.Warnings, lineCenters)
            If textShape.Count > 0 Then
                Dim tb = Clipper.GetBounds(textShape)
                tp.TextMinX = tb.left : tp.TextMaxX = tb.right : tp.TextMinY = tb.top : tp.TextMaxY = tb.bottom
                all.Add(New MachiningItem("Text", textShape, If(s.TextMachining, New Machining()), True))
            End If
        End If
        token.ThrowIfCancellationRequested()
        If items IsNot Nothing Then
            For Each it In items
                If it Is Nothing OrElse it.Paths Is Nothing OrElse it.Paths.Count = 0 Then Continue For
                Dim db = Clipper.GetBounds(it.Paths)
                If db.left < tp.BlankMinX - 0.0001 OrElse db.right > tp.BlankMaxX + 0.0001 OrElse db.top < tp.BlankMinY - 0.0001 OrElse db.bottom > tp.BlankMaxY + 0.0001 Then
                    If Not tp.Warnings.Contains("A drawing runs off the edge of the blank.") Then tp.Warnings.Add("A drawing runs off the edge of the blank.")
                End If
                all.Add(it)
            Next
        End If
        If all.Count = 0 Then Return tp

        For Each it In all
            For Each p In it.Paths
                tp.Outline.Add(GlyphOutline.ToPts(p))
            Next
        Next

        ' Items that cannot be machined as set up are skipped with a note.
        Dim usable As New List(Of MachiningItem)
        For Each it In all
            Dim probs = it.Machining.Problems(s.StockThickness)
            If probs.Count > 0 Then
                tp.Warnings.Add(it.Name & " skipped: " & String.Join(" ", probs))
            Else
                usable.Add(it)
            End If
        Next

        ' 1. V-carve everything set to V-carve, as one union (letters and drawings may overlap).
        Dim vItems = usable.Where(Function(i) i.Machining.Operation = CutOperation.VCarve).ToList()
        If vItems.Count > 0 Then
            Dim shape As New PathsD()
            For Each it In vItems
                shape.AddRange(it.Paths)
            Next
            If vItems.Count > 1 Then shape = Clipper.Union(shape, Nothing, FillRule.NonZero, GlyphOutline.ClipperPrecision)
            Dim centers = If(vItems.Any(Function(i) i.IsText), lineCenters, Nothing)
            Dim vtp = TextToToolpath.CarveShape(shape, centers, s, token)
            tp.Warnings.AddRange(vtp.Warnings)
            AppendSegment(tp, "V-carve", CutOperation.VCarve, s.CarveTool, vtp.Moves)
            tp.Contours.AddRange(vtp.Contours)
            tp.Regions.AddRange(vtp.Regions)
            tp.RegionCount += vtp.RegionCount
        End If

        ' 2. Pockets, grouped by tool; the tool the profiles start with goes last so it carries straight on.
        Dim pocketItems = usable.Where(Function(i) i.Machining.Operation = CutOperation.Pocket).ToList()
        Dim profileItems = usable.Where(Function(i) i.Machining.Operation = CutOperation.Profile).
            OrderBy(Function(i) If(i.Machining.Side = ProfileSide.OnLine, 0, If(i.Machining.Side = ProfileSide.Inside, 1, 2))).ToList()
        Dim profileGroups = GroupByTool(profileItems)
        ' Cut-outs go last: groups with outside profiles after the others.
        profileGroups = profileGroups.OrderBy(Function(g) If(g.Any(Function(i) i.Machining.Side = ProfileSide.Outside), 1, 0)).ToList()
        Dim pocketGroups = GroupByTool(pocketItems)
        If profileGroups.Count > 0 Then
            Dim firstProfileTool = profileGroups(0)(0).Machining.Tool
            pocketGroups = pocketGroups.OrderBy(Function(g) If(SameTool(g(0).Machining.Tool, firstProfileTool), 1, 0)).ToList()
        End If

        Dim regionIndex = tp.RegionCount
        For Each g In pocketGroups
            token.ThrowIfCancellationRequested()
            Dim moves As New List(Of ToolMove)
            For Each it In g
                Dim passes = MillingEngine.PocketPasses(Normalize(it.Paths), it.Machining, s, it.Name, If(it.IsText, lineCenters, Nothing), tp.Warnings)
                MillingEngine.LinkPasses(passes, s, moves, tp.Contours, regionIndex)
                regionIndex += 1
                tp.RegionCount += 1
            Next
            AppendSegment(tp, "Pocket: " & String.Join(", ", g.Select(Function(i) i.Name)), CutOperation.Pocket, g(0).Machining.Tool, moves)
        Next

        ' 3. Profiles.
        For Each g In profileGroups
            token.ThrowIfCancellationRequested()
            Dim moves As New List(Of ToolMove)
            For Each it In g
                Dim passes = MillingEngine.ProfilePasses(Normalize(it.Paths), it.Machining, s, it.Name, tp.Warnings)
                MillingEngine.LinkPasses(passes, s, moves, tp.Contours, regionIndex)
                regionIndex += 1
                tp.RegionCount += 1
                Dim bottom = it.Machining.BottomDepth(s.StockThickness)
                If bottom > it.Machining.Tool.Length + 0.000001 Then
                    tp.Warnings.Add(String.Format(Ci, "{0}: the cut ({1:0.###}"") is deeper than the {2}'s flutes ({3:0.###}"").", it.Name, bottom, it.Machining.Tool.DisplayName(), it.Machining.Tool.Length))
                End If
            Next
            AppendSegment(tp, "Profile: " & String.Join(", ", g.Select(Function(i) i.Name)), CutOperation.Profile, g(0).Machining.Tool, moves)
        Next
        For Each it In pocketItems
            Dim bottom = it.Machining.BottomDepth(s.StockThickness)
            If bottom > it.Machining.Tool.Length + 0.000001 Then
                tp.Warnings.Add(String.Format(Ci, "{0}: the pocket ({1:0.###}"") is deeper than the {2}'s flutes ({3:0.###}"").", it.Name, bottom, it.Machining.Tool.DisplayName(), it.Machining.Tool.Length))
            End If
            If s.StockThickness > 0 AndAlso bottom >= s.StockThickness Then
                tp.Warnings.Add(String.Format(Ci, "{0}: the pocket reaches through the {1:0.###}"" stock!", it.Name, s.StockThickness))
            End If
        Next

        FinishStats(tp, s)
        Return tp
    End Function

    ''' <summary>Outers counter-clockwise, holes clockwise, overlaps merged.</summary>
    Private Function Normalize(paths As PathsD) As PathsD
        Return Clipper.Union(paths, Nothing, FillRule.NonZero, GlyphOutline.ClipperPrecision)
    End Function

    Private Function SameTool(a As ToolDefinition, b As ToolDefinition) As Boolean
        If a Is Nothing OrElse b Is Nothing Then Return a Is b
        Return a.SameGeometry(b)
    End Function

    Private Function GroupByTool(items As List(Of MachiningItem)) As List(Of List(Of MachiningItem))
        Dim groups As New List(Of List(Of MachiningItem))
        For Each it In items
            Dim g = groups.FirstOrDefault(Function(x) SameTool(x(0).Machining.Tool, it.Machining.Tool))
            If g Is Nothing Then
                g = New List(Of MachiningItem)
                groups.Add(g)
            End If
            g.Add(it)
        Next
        Return groups
    End Function

    Private Sub AppendSegment(tp As Toolpath, name As String, op As CutOperation, tool As ToolDefinition, moves As List(Of ToolMove))
        If moves.Count = 0 Then Return
        tp.Segments.Add(New ToolpathSegment With {
            .Name = name, .Operation = op, .Tool = If(tool?.Clone(), ToolDefinition.DefaultVBit()),
            .FirstMove = tp.Moves.Count, .MoveCount = moves.Count})
        tp.Moves.AddRange(moves)
    End Sub

    ''' <summary>Bounds, depth, lengths, time and per-move durations over the whole job.</summary>
    Friend Sub FinishStats(tp As Toolpath, s As CarveSettings)
        Dim minX = Double.MaxValue, minY = Double.MaxValue, maxX = Double.MinValue, maxY = Double.MinValue
        For Each poly In tp.Outline
            For Each p In poly
                minX = Math.Min(minX, p.X) : maxX = Math.Max(maxX, p.X)
                minY = Math.Min(minY, p.Y) : maxY = Math.Max(maxY, p.Y)
            Next
        Next
        Dim minZ As Double = 0
        For Each mv In tp.Moves
            If mv.Kind <> MoveKind.Rapid Then
                minX = Math.Min(minX, mv.Target.X) : maxX = Math.Max(maxX, mv.Target.X)
                minY = Math.Min(minY, mv.Target.Y) : maxY = Math.Max(maxY, mv.Target.Y)
            End If
            minZ = Math.Min(minZ, mv.Target.Z)
        Next
        If minX = Double.MaxValue Then
            minX = 0 : maxX = 0 : minY = 0 : maxY = 0
        End If
        tp.MinX = minX : tp.MaxX = maxX : tp.MinY = minY : tp.MaxY = maxY
        tp.MinZ = minZ

        Dim rapidRate As Double = If(s.RapidRate > 0, s.RapidRate, 100.0)
        Dim cutLen As Double = 0, rapidLen As Double = 0, minutes As Double = 0
        Dim prev As New Pt3(0, 0, s.SafeZ)
        For i = 0 To tp.Moves.Count - 1
            Dim mv = tp.Moves(i)
            Dim d = prev.DistanceTo(mv.Target)
            Dim rate As Double
            Select Case mv.Kind
                Case MoveKind.Rapid : rate = rapidRate : rapidLen += d
                Case MoveKind.Plunge : rate = If(mv.Feed > 0, mv.Feed, s.PlungeRate) : cutLen += d
                Case Else : rate = If(mv.Feed > 0, mv.Feed, s.FeedRate) : cutLen += d
            End Select
            mv.Seconds = d / Math.Max(rate, 0.001) * 60.0
            minutes += mv.Seconds / 60.0
            prev = mv.Target
        Next
        tp.CutLength = cutLen
        tp.RapidLength = rapidLen
        tp.EstimatedMinutes = minutes
        For Each sg In tp.Segments
            Dim m As Double = 0, z As Double = 0
            For i = sg.FirstMove To sg.FirstMove + sg.MoveCount - 1
                m += tp.Moves(i).Seconds / 60.0
                z = Math.Min(z, tp.Moves(i).Target.Z)
            Next
            sg.EstimatedMinutes = m
            sg.MinZ = z
        Next
    End Sub
End Module
