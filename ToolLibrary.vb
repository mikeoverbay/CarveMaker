' ============================================================================
'  ToolLibrary.vb
'  Cutting tools for the mill, inch only. Every standard type is described by
'  four numbers: diameter, flute length, end radius and included angle (the
'  last two only when the type has them):
'
'    Flat end mill      D, L
'    Ball nose          D, L            (end radius = D/2)
'    Bull nose          D, L, R         (corner radius)
'    V-bit              D, L, A
'    Tapered ball nose  D, L, R, A      (R = tip ball radius, A = taper, included)
'    Drill              D, L, A         (point angle, 118 deg typical)
'
'  FluteProfile gives the side profile (radius against height above the tip),
'  used for the drawing in the Tool Library dialog and for the revolved 3D tool
'  in the simulation. The library lives in %AppData%\CarveMaker\ToolLibrary.json;
'  a project keeps its own copy of the tool it carves with.
' ============================================================================

Imports System.ComponentModel
Imports System.Drawing.Design
Imports System.Globalization
Imports System.IO
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports System.Text.RegularExpressions
Imports System.Windows.Forms.Design

Public Enum ToolType
    FlatEndMill
    BallNose
    BullNose
    VBit
    TaperedBallNose
    Drill
End Enum

''' <summary>One point of a tool's side profile, in inches.</summary>
Public Structure ToolProfilePoint
    ''' <summary>Distance from the tool axis.</summary>
    Public R As Double
    ''' <summary>Height above the tip.</summary>
    Public H As Double
    ''' <summary>Outward unit normal in the (r, h) plane; valid when Smooth (points on arcs).</summary>
    Public NR As Double, NH As Double
    Public Smooth As Boolean

    Public Sub New(r As Double, h As Double)
        Me.R = r : Me.H = h
    End Sub

    Public Sub New(r As Double, h As Double, nr As Double, nh As Double)
        Me.R = r : Me.H = h : Me.NR = nr : Me.NH = nh : Smooth = True
    End Sub
End Structure

''' <summary>A cutting tool. Inches and degrees.</summary>
<TypeConverter(GetType(ToolDefinitionConverter))>
Public Class ToolDefinition
    Private Shared ReadOnly Ci As CultureInfo = CultureInfo.InvariantCulture

    ''' <summary>Id of the standard 1/4" 90 degree V-bit (the default carving tool).</summary>
    Public Const DefaultVBitId As String = "std-vbit-0250-90"

    <Browsable(False)>
    Public Property Id As String = NewId()

    <DisplayName("Name"), [ReadOnly](True)>
    Public Property Name As String = ""

    <DisplayName("Type"), [ReadOnly](True), TypeConverter(GetType(ToolTypeConverter))>
    Public Property Type As ToolType = ToolType.VBit

    <DisplayName("Diameter (in)"), [ReadOnly](True), Description("Cutting diameter.")>
    Public Property Diameter As Double = 0.25

    <DisplayName("Flute length (in)"), [ReadOnly](True), Description("Length of the cutting part, measured up from the tip.")>
    Public Property Length As Double = 0.125

    <DisplayName("End radius (in)"), [ReadOnly](True),
     Description("Ball radius (ball nose), corner radius (bull nose) or tip radius (tapered ball nose); 0 when the tool has none.")>
    Public Property EndRadius As Double = 0.0

    <DisplayName("Angle (deg)"), [ReadOnly](True),
     Description("Full included angle: V-bit, drill point or the taper of a tapered ball nose; 0 when the tool has none.")>
    Public Property AngleDeg As Double = 90.0

    Public Shared Function NewId() As String
        Return Guid.NewGuid().ToString("N")
    End Function

    ' ------------------------------------------------------------ type rules

    Public Shared Function DisplayTypeName(t As ToolType) As String
        Select Case t
            Case ToolType.FlatEndMill : Return "Flat end mill"
            Case ToolType.BallNose : Return "Ball nose"
            Case ToolType.BullNose : Return "Bull nose (corner radius)"
            Case ToolType.VBit : Return "V-bit"
            Case ToolType.TaperedBallNose : Return "Tapered ball nose"
            Case ToolType.Drill : Return "Drill"
        End Select
        Return t.ToString()
    End Function

    ''' <summary>The type has an included angle (V-bit, tapered ball nose, drill).</summary>
    Public Shared Function TypeUsesAngle(t As ToolType) As Boolean
        Return t = ToolType.VBit OrElse t = ToolType.TaperedBallNose OrElse t = ToolType.Drill
    End Function

    ''' <summary>The type has an end radius (ball, bull, tapered ball nose).</summary>
    Public Shared Function TypeUsesRadius(t As ToolType) As Boolean
        Return t = ToolType.BallNose OrElse t = ToolType.BullNose OrElse t = ToolType.TaperedBallNose
    End Function

    <Browsable(False), JsonIgnore>
    Public ReadOnly Property UsesAngle As Boolean
        Get
            Return TypeUsesAngle(Type)
        End Get
    End Property

    <Browsable(False), JsonIgnore>
    Public ReadOnly Property UsesRadius As Boolean
        Get
            Return TypeUsesRadius(Type)
        End Get
    End Property

    ''' <summary>End radius actually used by the geometry (D/2 for a ball nose, 0 when the type has none).</summary>
    <Browsable(False), JsonIgnore>
    Public ReadOnly Property EffectiveEndRadius As Double
        Get
            If Type = ToolType.BallNose Then Return Diameter / 2.0
            If Not UsesRadius Then Return 0.0
            Return Math.Max(0.0, EndRadius)
        End Get
    End Property

    ''' <summary>Height above the tip where the tool first reaches its full diameter.</summary>
    <Browsable(False), JsonIgnore>
    Public ReadOnly Property FullDiameterHeight As Double
        Get
            Dim d2 = Diameter / 2.0
            If Not UsesAngle Then Return Math.Min(EffectiveEndRadius, d2)
            Dim a = HalfAngle()
            Dim tanA = Math.Tan(a)
            If tanA <= 0 Then Return Double.PositiveInfinity
            Dim r = EffectiveEndRadius
            Dim ht = r * (1 - Math.Sin(a)), rt = r * Math.Cos(a)
            Return ht + Math.Max(0.0, d2 - rt) / tanA
        End Get
    End Property

    ''' <summary>Deepest V-carve depth: full diameter reached, or the end of the flutes if sooner.</summary>
    <Browsable(False), JsonIgnore>
    Public ReadOnly Property UsableDepth As Double
        Get
            Return Math.Max(0.0, Math.Min(FullDiameterHeight, Length))
        End Get
    End Property

    ''' <summary>Length of the plain shank drawn above the flutes (one diameter, at least 0.1").</summary>
    <Browsable(False), JsonIgnore>
    Public ReadOnly Property ShankStub As Double
        Get
            Return Math.Max(0.1, Diameter)
        End Get
    End Property

    Private Function HalfAngle() As Double
        Return Math.Max(0.1, Math.Min(179.9, AngleDeg)) * Math.PI / 360.0
    End Function

    ''' <summary>Clears the numbers the type does not use (a ball nose's radius is always D/2).</summary>
    Public Sub Normalize()
        If Not UsesAngle Then AngleDeg = 0.0
        If Not UsesRadius Then EndRadius = 0.0
        If Type = ToolType.BallNose Then EndRadius = Diameter / 2.0
    End Sub

    ''' <summary>Typical angle and radius for a type, applied when the user switches type.</summary>
    Public Sub ApplyTypeDefaults()
        Select Case Type
            Case ToolType.VBit : AngleDeg = 90.0
            Case ToolType.Drill : AngleDeg = 118.0
            Case ToolType.TaperedBallNose
                AngleDeg = 7.2
                EndRadius = Math.Min(0.03125, Diameter / 4.0)
            Case ToolType.BullNose
                EndRadius = If(Diameter > 0.125, 0.03125, Diameter / 4.0)
        End Select
        Normalize()
    End Sub

    ''' <summary>Problems that make the tool unusable; empty when it is fine.</summary>
    Public Function Problems() As List(Of String)
        Dim p As New List(Of String)
        If Not (Diameter > 0) Then p.Add("Diameter must be more than 0.")
        If Not (Length > 0) Then p.Add("Flute length must be more than 0.")
        If p.Count > 0 Then Return p
        If Diameter > 6 Then p.Add("Diameter is over 6"".")
        If Length > 12 Then p.Add("Flute length is over 12"".")
        Select Case Type
            Case ToolType.BullNose
                If Not (EndRadius > 0) OrElse EndRadius >= Diameter / 2.0 Then
                    p.Add("Corner radius must be more than 0 and less than half the diameter (" & InchFormat.ShortText(Diameter / 2.0) & ").")
                End If
            Case ToolType.VBit, ToolType.Drill
                If Not (AngleDeg > 0) OrElse AngleDeg >= 180 Then p.Add("Angle must be between 0 and 180 degrees.")
            Case ToolType.TaperedBallNose
                If Not (AngleDeg > 0) OrElse AngleDeg >= 90 Then p.Add("Taper angle (included) must be between 0 and 90 degrees.")
                If Not (EndRadius > 0) Then
                    p.Add("Tip radius must be more than 0 (use a V-bit for a sharp tip).")
                ElseIf EndRadius * Math.Cos(HalfAngle()) >= Diameter / 2.0 Then
                    p.Add("Tip radius is too large for the diameter.")
                End If
        End Select
        If p.Count = 0 AndAlso UsesRadius AndAlso Length <= EffectiveEndRadius * (1 - If(UsesAngle, Math.Sin(HalfAngle()), 0.0)) Then
            p.Add("Flute length must reach past the rounded end.")
        End If
        Return p
    End Function

    ' ------------------------------------------------------------ geometry

    ''' <summary>
    ''' Side profile of the cutting part from the tip on the axis (r = 0, h = 0)
    ''' outward and up to the top of the flutes (h = Length). Arcs are split into
    ''' arcSegments pieces and carry exact normals. The shank (radius D/2) sits above.
    ''' </summary>
    Public Function FluteProfile(arcSegments As Integer) As List(Of ToolProfilePoint)
        Dim pts As New List(Of ToolProfilePoint)
        Dim d2 = Math.Max(0.0001, Diameter / 2.0)
        Dim L = Math.Max(0.0001, Length)
        Dim n = Math.Max(2, arcSegments)

        If Not UsesAngle Then
            ' Flat, ball or bull nose: flat bottom, quarter-circle corner, straight side.
            Dim r = Math.Min(EffectiveEndRadius, d2)
            Dim flatR = d2 - r
            If r <= 0 Then
                pts.Add(New ToolProfilePoint(0, 0))
                pts.Add(New ToolProfilePoint(d2, 0))
                pts.Add(New ToolProfilePoint(d2, L))
                Return pts
            End If
            If flatR > 0 Then pts.Add(New ToolProfilePoint(0, 0))
            For k = 0 To n
                Dim th = -Math.PI / 2 + (Math.PI / 2) * k / n
                Dim ph = r + r * Math.Sin(th)
                If ph > L Then
                    Dim thL = Math.Asin(Math.Max(-1.0, Math.Min(1.0, (L - r) / r)))
                    pts.Add(New ToolProfilePoint(flatR + r * Math.Cos(thL), L, Math.Cos(thL), Math.Sin(thL)))
                    Return pts
                End If
                pts.Add(New ToolProfilePoint(flatR + r * Math.Cos(th), ph, Math.Cos(th), Math.Sin(th)))
            Next
            If L > r Then pts.Add(New ToolProfilePoint(d2, L))
            Return pts
        End If

        ' V-bit, drill or tapered ball nose: optional ball tip, cone, straight side.
        Dim a = HalfAngle()
        Dim tanA = Math.Tan(a)
        Dim rb = EffectiveEndRadius
        Dim ht As Double = 0, rt As Double = 0
        If rb > 0 Then
            ht = rb * (1 - Math.Sin(a))
            rt = rb * Math.Cos(a)
            For k = 0 To n
                Dim th = -Math.PI / 2 + (Math.PI / 2 - a) * k / n
                Dim ph = rb + rb * Math.Sin(th)
                If ph > L Then
                    Dim thL = Math.Asin(Math.Max(-1.0, Math.Min(1.0, (L - rb) / rb)))
                    pts.Add(New ToolProfilePoint(rb * Math.Cos(thL), L, Math.Cos(thL), Math.Sin(thL)))
                    Return pts
                End If
                pts.Add(New ToolProfilePoint(rb * Math.Cos(th), ph, Math.Cos(th), Math.Sin(th)))
            Next
        Else
            pts.Add(New ToolProfilePoint(0, 0))
        End If
        Dim hD = ht + Math.Max(0.0, d2 - rt) / tanA
        If hD < L Then
            pts.Add(New ToolProfilePoint(d2, hD))
            pts.Add(New ToolProfilePoint(d2, L))
        Else
            pts.Add(New ToolProfilePoint(Math.Min(d2, rt + (L - ht) * tanA), L))
        End If
        Return pts
    End Function

    ''' <summary>Flute profile followed by the shank stub, ending back on the axis at the top.</summary>
    Public Function FullProfile(arcSegments As Integer) As List(Of ToolProfilePoint)
        Dim pts = FluteProfile(arcSegments)
        Dim d2 = Math.Max(0.0001, Diameter / 2.0)
        Dim L = Math.Max(0.0001, Length)
        If pts(pts.Count - 1).R < d2 - 0.0000001 Then pts.Add(New ToolProfilePoint(d2, L))
        pts.Add(New ToolProfilePoint(d2, L + ShankStub))
        pts.Add(New ToolProfilePoint(0, L + ShankStub))
        Return pts
    End Function

    ' ------------------------------------------------------------ naming

    ''' <summary>Name built from the numbers, e.g. 1/4" 90° V-bit.</summary>
    Public Function AutoName() As String
        Dim d = InchFormat.ShortText(Diameter)
        Select Case Type
            Case ToolType.FlatEndMill : Return d & " flat end mill"
            Case ToolType.BallNose : Return d & " ball nose"
            Case ToolType.BullNose : Return d & " bull nose R" & InchFormat.ShortText(EndRadius)
            Case ToolType.VBit : Return d & " " & Deg(AngleDeg) & "° V-bit"
            Case ToolType.TaperedBallNose : Return d & " tapered ball nose R" & InchFormat.ShortText(EndRadius) & " " & Deg(AngleDeg / 2) & "°/side"
            Case ToolType.Drill : Return d & " drill " & Deg(AngleDeg) & "°"
        End Select
        Return d & " tool"
    End Function

    Private Shared Function Deg(v As Double) As String
        Return v.ToString("0.##", Ci)
    End Function

    ''' <summary>Name, or the generated name when the name is empty.</summary>
    Public Function DisplayName() As String
        Return If(String.IsNullOrWhiteSpace(Name), AutoName(), Name)
    End Function

    ''' <summary>Printable-ASCII description for G-code comments.</summary>
    Public Function AsciiName() As String
        Return DisplayName().Replace("°", " deg").Replace("""", "in")
    End Function

    ' ------------------------------------------------------------ factory

    Public Shared Function Create(type As ToolType, diameter As Double, length As Double,
                                  Optional endRadius As Double = 0, Optional angleDeg As Double = 0,
                                  Optional id As String = Nothing) As ToolDefinition
        Dim t As New ToolDefinition With {
            .Id = If(id, NewId()), .Type = type, .Diameter = diameter, .Length = length,
            .EndRadius = endRadius, .AngleDeg = angleDeg}
        t.Normalize()
        t.Name = t.AutoName()
        Return t
    End Function

    ''' <summary>V-bit whose flutes end where it reaches full diameter.</summary>
    Public Shared Function MakeVBit(diameter As Double, angleDeg As Double, Optional id As String = Nothing) As ToolDefinition
        Dim cone = (diameter / 2.0) / Math.Tan(Math.Max(0.1, Math.Min(179.9, angleDeg)) * Math.PI / 360.0)
        Return Create(ToolType.VBit, diameter, Math.Ceiling(cone * 10000.0 - 0.000001) / 10000.0, 0, angleDeg, id)
    End Function

    ''' <summary>The standard 1/4" 90 degree V-bit.</summary>
    Public Shared Function DefaultVBit() As ToolDefinition
        Return MakeVBit(0.25, 90.0, DefaultVBitId)
    End Function

    ''' <summary>A new tool of a type with typical numbers.</summary>
    Public Shared Function NewOfType(type As ToolType) As ToolDefinition
        Select Case type
            Case ToolType.FlatEndMill : Return Create(type, 0.25, 0.75)
            Case ToolType.BallNose : Return Create(type, 0.25, 0.75)
            Case ToolType.BullNose : Return Create(type, 0.25, 0.75, 0.03125)
            Case ToolType.TaperedBallNose : Return Create(type, 0.25, 1.0, 0.03125, 7.2)
            Case ToolType.Drill : Return Create(type, 0.125, 0.875, 0, 118)
            Case Else : Return MakeVBit(0.25, 90.0)
        End Select
    End Function

    ''' <summary>Copy with the same Id.</summary>
    Public Function Clone() As ToolDefinition
        Return DirectCast(MemberwiseClone(), ToolDefinition)
    End Function

    ''' <summary>Copy with a new Id (a separate library entry).</summary>
    Public Function CloneAsNew() As ToolDefinition
        Dim c = Clone()
        c.Id = NewId()
        Return c
    End Function

    ''' <summary>Same numbers, type and name (the Id is not compared).</summary>
    Public Function SameGeometry(o As ToolDefinition) As Boolean
        If o Is Nothing Then Return False
        Return Type = o.Type AndAlso Near(Diameter, o.Diameter) AndAlso Near(Length, o.Length) AndAlso
               Near(EffectiveEndRadius, o.EffectiveEndRadius) AndAlso (Not UsesAngle OrElse Near(AngleDeg, o.AngleDeg))
    End Function

    Private Shared Function Near(a As Double, b As Double) As Boolean
        Return Math.Abs(a - b) < 0.0000001
    End Function

    Public Overrides Function Equals(obj As Object) As Boolean
        Dim o = TryCast(obj, ToolDefinition)
        Return o IsNot Nothing AndAlso SameGeometry(o) AndAlso String.Equals(Name, o.Name, StringComparison.Ordinal)
    End Function

    Public Overrides Function GetHashCode() As Integer
        Return HashCode.Combine(Type, Math.Round(Diameter, 6), Math.Round(Length, 6))
    End Function

    Public Overrides Function ToString() As String
        Return DisplayName()
    End Function
End Class

''' <summary>Shows tool types with readable names in the property grid.</summary>
Public Class ToolTypeConverter
    Inherits EnumConverter

    Public Sub New()
        MyBase.New(GetType(ToolType))
    End Sub

    Public Overrides Function ConvertTo(context As ITypeDescriptorContext, culture As CultureInfo, value As Object, destinationType As Type) As Object
        If destinationType Is GetType(String) AndAlso TypeOf value Is ToolType Then Return ToolDefinition.DisplayTypeName(DirectCast(value, ToolType))
        Return MyBase.ConvertTo(context, culture, value, destinationType)
    End Function
End Class

''' <summary>Expandable in the grid; the row itself shows the tool's name.</summary>
Public Class ToolDefinitionConverter
    Inherits ExpandableObjectConverter

    Public Overrides Function ConvertTo(context As ITypeDescriptorContext, culture As CultureInfo, value As Object, destinationType As Type) As Object
        Dim t = TryCast(value, ToolDefinition)
        If destinationType Is GetType(String) AndAlso t IsNot Nothing Then Return t.DisplayName()
        Return MyBase.ConvertTo(context, culture, value, destinationType)
    End Function
End Class

''' <summary>The "..." button on the V-carve tool row: opens the tool library to pick a V-bit.</summary>
Public Class ToolPickerEditor
    Inherits UITypeEditor

    Public Overrides Function GetEditStyle(context As ITypeDescriptorContext) As UITypeEditorEditStyle
        Return UITypeEditorEditStyle.Modal
    End Function

    Public Overrides Function EditValue(context As ITypeDescriptorContext, provider As IServiceProvider, value As Object) As Object
        Dim current = TryCast(value, ToolDefinition)
        Dim svc = TryCast(provider?.GetService(GetType(IWindowsFormsEditorService)), IWindowsFormsEditorService)
        Using dlg As New frmToolLibrary(AddressOf VCarveToolProblem, current)
            Dim result = If(svc IsNot Nothing, svc.ShowDialog(dlg), dlg.ShowDialog())
            If result = DialogResult.OK AndAlso dlg.SelectedTool IsNot Nothing Then
                ' Same tool, unchanged: keep the old object so the project is not marked as changed.
                If current IsNot Nothing AndAlso current.Id = dlg.SelectedTool.Id AndAlso current.Equals(dlg.SelectedTool) Then Return value
                Return dlg.SelectedTool.Clone()
            End If
        End Using
        Return value
    End Function

    ''' <summary>Why a tool cannot V-carve (Nothing when it can).</summary>
    Public Shared Function VCarveToolProblem(t As ToolDefinition) As String
        If t Is Nothing Then Return "Pick a tool."
        If t.Type <> ToolType.VBit Then Return "V-carving needs a V-bit."
        Return Nothing
    End Function
End Class

''' <summary>The user's tool library (JSON in %AppData%\CarveMaker).</summary>
Public Class ToolLibrary
    Public Property Version As Integer = 1
    Public Property Tools As New List(Of ToolDefinition)

    ''' <summary>Set when the file could not be read and the standard tools were used instead.</summary>
    <JsonIgnore>
    Public Property LoadWarning As String

    ''' <summary>Where the library is read and saved (tests point it elsewhere).</summary>
    Public Shared Property LibraryPath As String =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CarveMaker", "ToolLibrary.json")

    Private Shared ReadOnly Options As JsonSerializerOptions = CreateOptions()

    Private Shared Function CreateOptions() As JsonSerializerOptions
        Dim o As New JsonSerializerOptions With {
            .WriteIndented = True,
            .PropertyNameCaseInsensitive = True,
            .ReadCommentHandling = JsonCommentHandling.Skip,
            .AllowTrailingCommas = True
        }
        o.Converters.Add(New JsonStringEnumConverter())
        Return o
    End Function

    ''' <summary>Reads the library; never throws (missing file: standard tools; unreadable: standard tools plus LoadWarning).</summary>
    Public Shared Function Load() As ToolLibrary
        Dim p = LibraryPath
        If Not File.Exists(p) Then Return Defaults()
        Try
            Dim library = JsonSerializer.Deserialize(Of ToolLibrary)(File.ReadAllText(p), Options)
            If library Is Nothing OrElse library.Tools Is Nothing Then Throw New InvalidDataException("no tool list")
            library.Tools.RemoveAll(Function(t) t Is Nothing)
            Dim seen As New HashSet(Of String)
            For Each t In library.Tools
                If String.IsNullOrWhiteSpace(t.Id) OrElse Not seen.Add(t.Id) Then
                    t.Id = ToolDefinition.NewId()
                    seen.Add(t.Id)
                End If
                If t.Name Is Nothing Then t.Name = ""
                t.Normalize()
            Next
            Return library
        Catch ex As Exception
            Dim bad = p & ".bad"
            Try
                File.Copy(p, bad, True)
            Catch
            End Try
            Dim d = Defaults()
            d.LoadWarning = "The tool library could not be read (" & ex.Message & "). Showing the standard tools; the old file was kept as " & Path.GetFileName(bad) & "."
            Return d
        End Try
    End Function

    ''' <summary>Writes the library (via a temporary file so a crash never leaves half a file).</summary>
    Public Sub Save()
        Dim p = LibraryPath
        Directory.CreateDirectory(Path.GetDirectoryName(p))
        Dim tmp = p & ".tmp"
        File.WriteAllText(tmp, JsonSerializer.Serialize(Me, Options))
        File.Move(tmp, p, True)
    End Sub

    Public Function FindById(id As String) As ToolDefinition
        If String.IsNullOrEmpty(id) Then Return Nothing
        Return Tools.FirstOrDefault(Function(t) t.Id = id)
    End Function

    ''' <summary>Standard inch tools.</summary>
    Public Shared Function Defaults() As ToolLibrary
        Dim library As New ToolLibrary()
        Dim tools = library.Tools
        tools.Add(ToolDefinition.Create(ToolType.FlatEndMill, 0.0625, 0.1875, id:="std-flat-0063"))
        tools.Add(ToolDefinition.Create(ToolType.FlatEndMill, 0.125, 0.5, id:="std-flat-0125"))
        tools.Add(ToolDefinition.Create(ToolType.FlatEndMill, 0.25, 0.75, id:="std-flat-0250"))
        tools.Add(ToolDefinition.Create(ToolType.FlatEndMill, 0.5, 1.0, id:="std-flat-0500"))
        tools.Add(ToolDefinition.Create(ToolType.BallNose, 0.0625, 0.25, id:="std-ball-0063"))
        tools.Add(ToolDefinition.Create(ToolType.BallNose, 0.125, 0.5, id:="std-ball-0125"))
        tools.Add(ToolDefinition.Create(ToolType.BallNose, 0.25, 0.75, id:="std-ball-0250"))
        tools.Add(ToolDefinition.Create(ToolType.BullNose, 0.25, 0.75, 0.03125, id:="std-bull-0250"))
        tools.Add(ToolDefinition.Create(ToolType.BullNose, 0.5, 1.0, 0.0625, id:="std-bull-0500"))
        tools.Add(ToolDefinition.MakeVBit(0.25, 90, ToolDefinition.DefaultVBitId))
        tools.Add(ToolDefinition.MakeVBit(0.25, 60, "std-vbit-0250-60"))
        tools.Add(ToolDefinition.MakeVBit(0.5, 90, "std-vbit-0500-90"))
        tools.Add(ToolDefinition.MakeVBit(0.5, 60, "std-vbit-0500-60"))
        tools.Add(ToolDefinition.MakeVBit(0.5, 30, "std-vbit-0500-30"))
        tools.Add(ToolDefinition.Create(ToolType.TaperedBallNose, 0.25, 1.0, 0.03125, 7.2, "std-taper-r031"))
        tools.Add(ToolDefinition.Create(ToolType.TaperedBallNose, 0.25, 1.0, 0.0625, 7.2, "std-taper-r063"))
        tools.Add(ToolDefinition.Create(ToolType.Drill, 0.125, 0.875, 0, 118, "std-drill-0125"))
        tools.Add(ToolDefinition.Create(ToolType.Drill, 0.25, 1.5, 0, 118, "std-drill-0250"))
        Return library
    End Function
End Class

''' <summary>Inch numbers as text: fractions where exact (to 1/64"), decimals otherwise.</summary>
Public Module InchFormat
    Private ReadOnly Ci As CultureInfo = CultureInfo.InvariantCulture
    Private ReadOnly MixedRx As New Regex("^(\d+)(?:\s+|\s*-\s*)(\d+)\s*/\s*(\d+)$", RegexOptions.Compiled)
    Private ReadOnly FractionRx As New Regex("^(\d+)\s*/\s*(\d+)$", RegexOptions.Compiled)

    ''' <summary>"1/4", "1-1/2", "2" when v is a multiple of 1/64 (within 0.00005"); otherwise Nothing.</summary>
    Public Function Fraction(v As Double) As String
        If Double.IsNaN(v) OrElse Double.IsInfinity(v) OrElse v <= 0 Then Return Nothing
        Dim n64 = Math.Round(v * 64.0)
        If n64 < 1 OrElse Math.Abs(v - n64 / 64.0) > 0.00005 Then Return Nothing
        Dim whole = CLng(Math.Floor(n64 / 64.0))
        Dim num = CLng(n64) - whole * 64, den As Long = 64
        While num > 0 AndAlso num Mod 2 = 0
            num \= 2 : den \= 2
        End While
        If num = 0 Then Return whole.ToString(Ci)
        If whole = 0 Then Return num.ToString(Ci) & "/" & den.ToString(Ci)
        Return whole.ToString(Ci) & "-" & num.ToString(Ci) & "/" & den.ToString(Ci)
    End Function

    ''' <summary>1/4" or 0.236".</summary>
    Public Function ShortText(v As Double) As String
        Return If(Fraction(v), v.ToString("0.0###", Ci)) & """"
    End Function

    ''' <summary>0.2500" (1/4) for drawings.</summary>
    Public Function DimText(v As Double) As String
        Dim f = Fraction(v)
        Dim d = v.ToString("0.0000", Ci) & """"
        Return If(f Is Nothing OrElse f.IndexOf("/"c) < 0, d, d & " (" & f & ")")
    End Function

    ''' <summary>Plain decimal for an edit box.</summary>
    Public Function EditText(v As Double) As String
        Return v.ToString("0.0###", Ci)
    End Function

    ''' <summary>Accepts 0.25, .25, 1/4, 1-1/4, 1 1/4, with or without a trailing " or in.</summary>
    Public Function TryParse(text As String, ByRef value As Double) As Boolean
        value = 0
        If text Is Nothing Then Return False
        Dim t = text.Trim().ToLowerInvariant()
        If t.EndsWith("""") Then
            t = t.Substring(0, t.Length - 1).Trim()
        ElseIf t.EndsWith("in") Then
            t = t.Substring(0, t.Length - 2).Trim()
        End If
        If t.Length = 0 Then Return False
        Dim m = MixedRx.Match(t)
        If m.Success Then
            Dim den = Double.Parse(m.Groups(3).Value, Ci)
            If den = 0 Then Return False
            value = Double.Parse(m.Groups(1).Value, Ci) + Double.Parse(m.Groups(2).Value, Ci) / den
            Return True
        End If
        m = FractionRx.Match(t)
        If m.Success Then
            Dim den = Double.Parse(m.Groups(2).Value, Ci)
            If den = 0 Then Return False
            value = Double.Parse(m.Groups(1).Value, Ci) / den
            Return True
        End If
        If Double.TryParse(t, NumberStyles.Float, Ci, value) OrElse Double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, value) Then
            Return Not (Double.IsNaN(value) OrElse Double.IsInfinity(value))
        End If
        Return False
    End Function
End Module
