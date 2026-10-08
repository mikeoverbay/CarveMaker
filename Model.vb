' ============================================================================
'  Model.vb
'  Shared data types for Text_to_CNC_path: geometry primitives, toolpath
'  containers and the user-editable job settings shown in the PropertyGrid.
'  All linear units are INCHES unless a member name says otherwise.
'  Z = 0 is the top of the stock; cutting depths are negative Z values.
' ============================================================================

Imports System.ComponentModel
Imports System.Drawing.Text
Imports System.Globalization
Imports System.Text.Json.Serialization

''' <summary>2D point (inches, CNC convention: X right, Y up).</summary>
Public Structure Pt2
    Public X As Double
    Public Y As Double

    Public Sub New(x As Double, y As Double)
        Me.X = x
        Me.Y = y
    End Sub

    Public Function DistanceTo(other As Pt2) As Double
        Dim dx = other.X - X
        Dim dy = other.Y - Y
        Return Math.Sqrt(dx * dx + dy * dy)
    End Function

    Public Overrides Function ToString() As String
        Return String.Format(CultureInfo.InvariantCulture, "({0:0.####}, {1:0.####})", X, Y)
    End Function
End Structure

''' <summary>3D point (inches).</summary>
Public Structure Pt3
    Public X As Double
    Public Y As Double
    Public Z As Double

    Public Sub New(x As Double, y As Double, z As Double)
        Me.X = x
        Me.Y = y
        Me.Z = z
    End Sub

    Public Function DistanceTo(other As Pt3) As Double
        Dim dx = other.X - X
        Dim dy = other.Y - Y
        Dim dz = other.Z - Z
        Return Math.Sqrt(dx * dx + dy * dy + dz * dz)
    End Function

    Public Overrides Function ToString() As String
        Return String.Format(CultureInfo.InvariantCulture, "({0:0.####}, {1:0.####}, {2:0.####})", X, Y, Z)
    End Function
End Structure

Public Enum MoveKind
    ''' <summary>G0 traverse at safe height (or vertical retract).</summary>
    Rapid
    ''' <summary>G1 vertical move into the material at the plunge feed.</summary>
    Plunge
    ''' <summary>G1 cutting move at the cutting feed.</summary>
    Feed
End Enum

''' <summary>One linear machine motion. The start point is the previous move's target.</summary>
Public Class ToolMove
    Public Property Kind As MoveKind
    Public Property Target As Pt3
    ''' <summary>Feed rate in inches/minute (ignored for rapids).</summary>
    Public Property Feed As Double
    ''' <summary>Duration of this move in seconds (filled by the linker, used by the simulation clock).</summary>
    Public Property Seconds As Double

    Public Sub New(kind As MoveKind, target As Pt3, feed As Double)
        Me.Kind = kind
        Me.Target = target
        Me.Feed = feed
    End Sub
End Class

Public Enum PassKind
    ''' <summary>Constant-Z inset contour cut with a small allowance left for finishing.</summary>
    Rough
    ''' <summary>The exact contour at the flat depth: finishes the bevel where the floor begins.</summary>
    BevelFinish
    ''' <summary>Constant-Z pass at the flat depth clearing a floor wider than the V can reach.</summary>
    FloorClear
    ''' <summary>Variable-Z medial-axis (centerline) finishing pass. Open polyline.</summary>
    Centerline
End Enum

''' <summary>One cutting pass: a closed constant-Z loop or an open variable-Z centerline.</summary>
Public Class ToolpathContour
    ''' <summary>Pass vertices with tip Z. For closed loops the last point is NOT repeated.</summary>
    Public Property Points As New List(Of Pt3)
    ''' <summary>True for a closed loop (the engine feeds back to the first point).</summary>
    Public Property IsClosed As Boolean = True
    Public Property Kind As PassKind = PassKind.Rough
    ''' <summary>Nominal tip Z (negative). For centerline passes this is the deepest point.</summary>
    Public Property Z As Double
    ''' <summary>Inset distance from the glyph boundary that produced a closed loop (0 for centerlines).</summary>
    Public Property Inset As Double

    Public ReadOnly Property IsClearing As Boolean
        Get
            Return Kind = PassKind.FloorClear
        End Get
    End Property
    ''' <summary>Index of the connected glyph region (letter) this loop belongs to.</summary>
    Public Property RegionIndex As Integer
    ''' <summary>
    ''' Machining order level inside the region. Regular passes get even levels in
    ''' depth order; ridge-refinement passes emitted between two regular passes get
    ''' the odd level in between, so they are cut before the next regular pass.
    ''' </summary>
    Public Property Order As Integer
    ''' <summary>
    ''' Id of the lobe (outer + its holes at one depth, or one lobe's whole ridge
    ''' refinement) this contour belongs to. Contours of a chain are cut consecutively.
    ''' </summary>
    Public Property Chain As Integer
    ''' <summary>
    ''' Chain id of the lobe one pass shallower that contains this lobe, or -1 for the
    ''' first pass. Lets the linker finish a lobe completely before moving to a sibling.
    ''' </summary>
    Public Property Parent As Integer = -1
End Class

''' <summary>Result of a toolpath generation run.</summary>
Public Class Toolpath
    ''' <summary>Normalized glyph outline polygons (outers and holes), for display.</summary>
    Public Property Outline As New List(Of List(Of Pt2))
    ''' <summary>All cutting loops in machining order.</summary>
    Public Property Contours As New List(Of ToolpathContour)
    ''' <summary>Linked machine motions ready for G-code output and display.</summary>
    Public Property Moves As New List(Of ToolMove)
    ''' <summary>Per-region boundary geometry (indexed by RegionIndex) used for gouge checks.</summary>
    Public Property Regions As New List(Of RegionShape)

    ''' <summary>Blank (stock) rectangle in machine coordinates, for display and the G-code header.</summary>
    Public Property BlankMinX As Double
    Public Property BlankMinY As Double
    Public Property BlankMaxX As Double
    Public Property BlankMaxY As Double

    Public Property RegionCount As Integer
    Public Property MinX As Double
    Public Property MaxX As Double
    Public Property MinY As Double
    Public Property MaxY As Double
    Public Property MinZ As Double

    ''' <summary>Total length of feed/plunge moves (inches).</summary>
    Public Property CutLength As Double
    ''' <summary>Total length of rapid moves (inches).</summary>
    Public Property RapidLength As Double
    ''' <summary>Rough cycle time estimate.</summary>
    Public Property EstimatedMinutes As Double
    ''' <summary>Any non-fatal notes produced while generating (e.g. depth clamped).</summary>
    Public Property Warnings As New List(Of String)

    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return Moves.Count = 0
        End Get
    End Property

    Public ReadOnly Property Width As Double
        Get
            Return Math.Max(0, MaxX - MinX)
        End Get
    End Property

    Public ReadOnly Property Height As Double
        Get
            Return Math.Max(0, MaxY - MinY)
        End Get
    End Property
End Class

Public Enum TextAlign
    Left
    Center
    Right
End Enum

Public Enum SizeSlot
    Large = 0
    Medium = 1
    Small = 2
End Enum

''' <summary>A font family plus style, chosen independently for each size slot.</summary>
<TypeConverter(GetType(ExpandableObjectConverter))>
Public Class FontChoice
    <DisplayName("Family"), TypeConverter(GetType(FontFamilyNameConverter)),
     Description("Installed font family for lines of this size.")>
    Public Property Family As String = "Arial"

    <DisplayName("Bold")>
    Public Property Bold As Boolean = False

    <DisplayName("Italic")>
    Public Property Italic As Boolean = False

    Public Sub New()
    End Sub

    Public Sub New(family As String, Optional bold As Boolean = False, Optional italic As Boolean = False)
        Me.Family = family
        Me.Bold = bold
        Me.Italic = italic
    End Sub

    Public Function Style() As FontStyle
        Dim fs As FontStyle = FontStyle.Regular
        If Bold Then fs = fs Or FontStyle.Bold
        If Italic Then fs = fs Or FontStyle.Italic
        Return fs
    End Function

    Public Function Clone() As FontChoice
        Return New FontChoice(Family, Bold, Italic)
    End Function

    Public Overrides Function ToString() As String
        Return If(Family, "") & If(Bold, " Bold", "") & If(Italic, " Italic", "")
    End Function

    Public Overrides Function Equals(obj As Object) As Boolean
        Dim o = TryCast(obj, FontChoice)
        If o Is Nothing Then Return False
        Return String.Equals(Family, o.Family, StringComparison.OrdinalIgnoreCase) AndAlso Bold = o.Bold AndAlso Italic = o.Italic
    End Function

    Public Overrides Function GetHashCode() As Integer
        Return ToString().ToLowerInvariant().GetHashCode()
    End Function
End Class

''' <summary>One line of the job text with its own size, font and alignment.</summary>
Public Class TextLine
    Public Property Text As String = ""
    ''' <summary>Letter size in inches (em or cap height depending on CarveSettings.SizeBy).</summary>
    Public Property SizeIn As Double = 1.0
    Public Property Align As TextAlign = TextAlign.Left
    ''' <summary>Font for this line (Nothing = the large-size font).</summary>
    Public Property Font As FontChoice

    Public Sub New()
    End Sub

    Public Sub New(text As String, sizeIn As Double, align As TextAlign, Optional font As FontChoice = Nothing)
        Me.Text = text
        Me.SizeIn = sizeIn
        Me.Align = align
        Me.Font = font
    End Sub

    ''' <summary>Splits plain text into lines, all at the large size and font, left aligned.</summary>
    Public Shared Function FromPlainText(text As String, s As CarveSettings) As List(Of TextLine)
        Dim result As New List(Of TextLine)
        If text Is Nothing Then Return result
        For Each ln In text.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(ControlChars.Lf)
            result.Add(New TextLine(ln, s.SizeLargeIn, TextAlign.Left, s.FontLarge))
        Next
        Return result
    End Function

    ''' <summary>Stable text key used to detect whether the job content changed.</summary>
    Public Shared Function KeyOf(lines As IEnumerable(Of TextLine)) As String
        Dim sb As New System.Text.StringBuilder()
        For Each l In lines
            sb.Append(l.Text).Append("|"c).Append(l.SizeIn.ToString("R", Globalization.CultureInfo.InvariantCulture)).Append("|"c).Append(CInt(l.Align)).Append("|"c).Append(If(l.Font Is Nothing, "", l.Font.ToString())).Append(ControlChars.Lf)
        Next
        Return sb.ToString()
    End Function
End Class

Public Enum MillingDirection
    ''' <summary>Cutter rotation pulls into the uncut material (default for routers).</summary>
    Climb
    ''' <summary>Cutter rotation opposes the feed; sometimes preferred to reduce tear-out.</summary>
    Conventional
End Enum

Public Enum OutputUnits
    Inches
    Millimeters
End Enum

Public Enum SizeMode
    ''' <summary>Font size is the em height (the font's design size).</summary>
    EmHeight
    ''' <summary>Font size is the height of a capital H.</summary>
    CapHeight
End Enum

Public Enum VerticalAlign
    Top
    Middle
    Bottom
End Enum

Public Enum SimResolution
    ''' <summary>0.003" cells.</summary>
    Fine
    ''' <summary>0.005" cells.</summary>
    Medium
    ''' <summary>0.010" cells.</summary>
    Coarse
End Enum

Public Enum CutOrder
    ''' <summary>Top line first; each line's letters left to right.</summary>
    LineByLine
    ''' <summary>Top line first; alternate lines run right to left to shorten the hop between lines.</summary>
    Serpentine
    ''' <summary>All letters left to right regardless of line (shortest X travel, long Y hops).</summary>
    LeftToRight
End Enum

''' <summary>Lists the installed font families as a drop-down in the PropertyGrid.</summary>
Public Class FontFamilyNameConverter
    Inherits StringConverter

    Public Overrides Function GetStandardValuesSupported(context As ITypeDescriptorContext) As Boolean
        Return True
    End Function

    Public Overrides Function GetStandardValuesExclusive(context As ITypeDescriptorContext) As Boolean
        ' Allow typing a name too, in case a font is installed per-user.
        Return False
    End Function

    Public Overrides Function GetStandardValues(context As ITypeDescriptorContext) As StandardValuesCollection
        Dim names As New List(Of String)
        Using coll As New InstalledFontCollection()
            For Each fam In coll.Families
                names.Add(fam.Name)
            Next
        End Using
        names.Sort(StringComparer.CurrentCultureIgnoreCase)
        Return New StandardValuesCollection(names)
    End Function
End Class

''' <summary>
''' Everything the user can change about the job. Edited live in the PropertyGrid,
''' cloned before each background generation run.
''' </summary>
Public Class CarveSettings

    ' ---------------------------------------------------------------- Blank
    <Category("0. Blank"), DisplayName("Blank width X (in)"),
     Description("Width of the stock blank. Lines are justified Left / Center / Right across this width.")>
    Public Property BlankWidthIn As Double = 6.0

    <Category("0. Blank"), DisplayName("Blank height Y (in)"),
     Description("Height of the stock blank. The text block is placed Top / Middle / Bottom within it.")>
    Public Property BlankHeightIn As Double = 3.0

    <Category("0. Blank"), DisplayName("Blank origin X (in)"),
     Description("Machine X of the blank's lower-left corner. Use 0 when you zero the machine at the lower-left corner, or -width/2 when you zero at the center.")>
    Public Property BlankOriginX As Double = 0.0

    <Category("0. Blank"), DisplayName("Blank origin Y (in)"),
     Description("Machine Y of the blank's lower-left corner.")>
    Public Property BlankOriginY As Double = 0.0

    <Category("0. Blank"), DisplayName("Margin (in)"),
     Description("Distance kept between the blank's edges and left/right-justified or top/bottom-placed text.")>
    Public Property MarginIn As Double = 0.25

    <Category("0. Blank"), DisplayName("Vertical placement"),
     Description("Where the whole text block sits on the blank: against the top margin, centered, or against the bottom margin.")>
    Public Property VerticalPlacement As VerticalAlign = VerticalAlign.Middle

    <Category("0. Blank"), DisplayName("Text offset X (in)"),
     Description("Fine adjustment added to the justified text position.")>
    Public Property TextOffsetX As Double = 0.0

    <Category("0. Blank"), DisplayName("Text offset Y (in)"),
     Description("Fine adjustment added to the placed text position.")>
    Public Property TextOffsetY As Double = 0.0

    ' ---------------------------------------------------------------- Text
    <Category("1. Text"), DisplayName("Size 1 - Large (in)"),
     Description("Letter size used by lines formatted 'Large' in the editor.")>
    Public Property SizeLargeIn As Double = 1.0

    <Category("1. Text"), DisplayName("Size 1 - Large font"),
     Description("Font for lines formatted 'Large'. Expand to pick the family and style, or use Font > Choose Font... with the caret on such a line.")>
    Public Property FontLarge As New FontChoice("Arial")

    <Category("1. Text"), DisplayName("Size 2 - Medium (in)"),
     Description("Letter size used by lines formatted 'Medium' in the editor.")>
    Public Property SizeMediumIn As Double = 0.6

    <Category("1. Text"), DisplayName("Size 2 - Medium font")>
    Public Property FontMedium As New FontChoice("Arial")

    <Category("1. Text"), DisplayName("Size 3 - Small (in)"),
     Description("Letter size used by lines formatted 'Small' in the editor.")>
    Public Property SizeSmallIn As Double = 0.35

    <Category("1. Text"), DisplayName("Size 3 - Small font")>
    Public Property FontSmall As New FontChoice("Arial")

    ''' <summary>Font of a size slot.</summary>
    Public Function FontFor(slot As SizeSlot) As FontChoice
        Select Case slot
            Case SizeSlot.Medium : Return FontMedium
            Case SizeSlot.Small : Return FontSmall
            Case Else : Return FontLarge
        End Select
    End Function

    ' Compatibility accessors: the large-size font (used by plain-text generation and tests).
    <Browsable(False), JsonIgnore>
    Public Property FontFamilyName As String
        Get
            Return FontLarge.Family
        End Get
        Set(value As String)
            FontLarge.Family = value
        End Set
    End Property

    <Browsable(False), JsonIgnore>
    Public Property Bold As Boolean
        Get
            Return FontLarge.Bold
        End Get
        Set(value As Boolean)
            FontLarge.Bold = value
        End Set
    End Property

    <Browsable(False), JsonIgnore>
    Public Property Italic As Boolean
        Get
            Return FontLarge.Italic
        End Get
        Set(value As Boolean)
            FontLarge.Italic = value
        End Set
    End Property

    <Category("1. Text"), DisplayName("Size means"),
     Description("EmHeight: the font's design size (capitals are about 70% of it). CapHeight: a capital H is exactly this tall.")>
    Public Property SizeBy As SizeMode = SizeMode.CapHeight

    <Category("1. Text"), DisplayName("Line spacing"),
     Description("Multiplier applied to the font's natural line height for multi-line text.")>
    Public Property LineSpacing As Double = 1.0

    ''' <summary>Inch size of a size slot.</summary>
    Public Function SizeOf(slot As SizeSlot) As Double
        Select Case slot
            Case SizeSlot.Medium : Return SizeMediumIn
            Case SizeSlot.Small : Return SizeSmallIn
            Case Else : Return SizeLargeIn
        End Select
    End Function

    <Category("1. Text"), DisplayName("Curve tolerance (in)"),
     Description("Chord error allowed when flattening glyph curves into line segments.")>
    Public Property CurveTolerance As Double = 0.0005

    ' ---------------------------------------------------------------- Tool
    <Category("2. Tool"), DisplayName("Diameter (in)"),
     Description("Largest cutting diameter of the V-bit.")>
    Public Property ToolDiameterIn As Double = 0.25

    <Category("2. Tool"), DisplayName("Included angle (deg)"),
     Description("Full included angle of the V-bit tip (90 for a 90-degree V-bit).")>
    Public Property IncludedAngleDeg As Double = 90.0

    <Category("2. Tool"), DisplayName("Tip flat (in)"),
     Description("Diameter of the flat at the very tip of the V-bit (0 for a sharp point). Measure it; many 1/4"" bits have 0.005-0.02"".")>
    Public Property TipFlatIn As Double = 0.0

    <Category("2. Tool"), DisplayName("Max usable depth (in)"), [ReadOnly](True), JsonIgnore,
     Description("Depth at which the V-bit reaches its full diameter: (radius - tip flat / 2) / tan(half angle).")>
    Public ReadOnly Property MaxToolDepthIn As Double
        Get
            Dim half = Math.Max(1.0, Math.Min(179.0, IncludedAngleDeg)) * Math.PI / 360.0
            Return Math.Round(Math.Max(0.0, ToolDiameterIn / 2.0 - Math.Max(0.0, TipFlatIn) / 2.0) / Math.Tan(half), 6)
        End Get
    End Property

    ' ---------------------------------------------------------------- Carve
    <Category("3. Carve"), DisplayName("Flat depth (in)"),
     Description("Maximum carve depth. Areas wider than the V can reach at this depth are cleared flat here. Clamped to the tool's max usable depth; keep a little margin below it.")>
    Public Property FlatDepth As Double = 0.12

    <Category("3. Carve"), DisplayName("Roughing depth step (in)"),
     Description("Vertical distance between the constant-depth roughing contours.")>
    Public Property DepthStep As Double = 0.02

    <Category("3. Carve"), DisplayName("Roughing allowance (in)"),
     Description("Skin left by the roughing contours so they never rub the finished bevel. Removed by the centerline finishing pass.")>
    Public Property RoughAllowance As Double = 0.004

    <Category("3. Carve"), DisplayName("Centerline finishing pass"),
     Description("Final variable-depth pass along the center of each stroke: cuts the ridge, the sharp corners and the finished bevel exactly. Turn off only for quick previews.")>
    Public Property FinishPass As Boolean = True

    <Category("3. Carve"), DisplayName("Finishing resolution (in)"),
     Description("Spacing of the boundary samples used to trace the centerline. Smaller = smoother, slower to compute.")>
    Public Property FinishResolution As Double = 0.002

    <Category("3. Carve"), DisplayName("Floor stepover (in)"),
     Description("Spacing between flat-depth floor clearing passes. Scallop height is about half of this on straight runs and up to the stepover at corners.")>
    Public Property ClearStepover As Double = 0.006

    <Category("3. Carve"), DisplayName("Milling direction"),
     Description("Direction of the closed contours relative to the material still to be removed (inside the loop). Both flanks of a V cut are engaged, so this mostly affects the roughing contours.")>
    Public Property Direction As MillingDirection = MillingDirection.Climb

    ' ---------------------------------------------------------------- Machine
    <Category("4. Machine"), DisplayName("Safe Z (in)"),
     Description("Height above the stock top used for rapid moves between letters and at the start and end.")>
    Public Property SafeZ As Double = 0.25

    <Category("4. Machine"), DisplayName("Clearance Z (in)"),
     Description("Height above the stock top used for short rapid moves inside one letter.")>
    Public Property ClearanceZ As Double = 0.05

    <Category("4. Machine"), DisplayName("Stock thickness (in)"),
     Description("Used only to warn when the flat depth would cut through the stock.")>
    Public Property StockThickness As Double = 0.75

    <Category("4. Machine"), DisplayName("Feed rate (in/min)")>
    Public Property FeedRate As Double = 40.0

    <Category("4. Machine"), DisplayName("Plunge rate (in/min)")>
    Public Property PlungeRate As Double = 15.0

    <Category("4. Machine"), DisplayName("Rapid rate (in/min)"),
     Description("Used only for the cycle-time estimate.")>
    Public Property RapidRate As Double = 150.0

    <Category("4. Machine"), DisplayName("Spindle RPM")>
    Public Property SpindleRpm As Integer = 16000

    <Category("4. Machine"), DisplayName("Spindle dwell (s)"),
     Description("G4 pause after M3 so the spindle reaches speed before the first plunge (seconds; 0 = none). FluidNC and GRBL read P in seconds.")>
    Public Property SpindleDwellSeconds As Double = 2.0

    <Category("4. Machine"), DisplayName("Coolant M8 / M9"),
     Description("Write M8 (flood on) on its own line before the spindle starts and M9 (off) after M5.")>
    Public Property CoolantOn As Boolean = True

    <Category("4. Machine"), DisplayName("Cut order"),
     Description("LineByLine: finish each line of text (top line first, left to right) before the next. Serpentine: same but alternate lines run right to left. LeftToRight: every letter by X position, which hops between lines.")>
    Public Property Order As CutOrder = CutOrder.LineByLine

    ' ---------------------------------------------------------------- Simulation
    <Category("5. Simulation"), DisplayName("Precision"),
     Description("Cell size of the material-removal heightmap: Fine 0.003"", Medium 0.005"", Coarse 0.010"". Finer costs GPU memory (blank area / cell size squared, 4 bytes per cell).")>
    Public Property SimResolution As SimResolution = SimResolution.Medium

    <Category("5. Simulation"), DisplayName("Max texture size (hardware)"), [ReadOnly](True), JsonIgnore,
     Description("Largest texture edge this graphics card supports. The heightmap is coarsened automatically when the blank needs more cells than this.")>
    Public ReadOnly Property SimMaxTextureSize As Integer
        Get
            Return HardwareMaxTextureSize
        End Get
    End Property

    ''' <summary>Filled in by the OpenGL view once the context exists.</summary>
    Public Shared Property HardwareMaxTextureSize As Integer = 0

    ''' <summary>Heightmap cell size in inches for the chosen precision.</summary>
    Public ReadOnly Property SimCellSize As Double
        Get
            Select Case SimResolution
                Case SimResolution.Fine : Return 0.003
                Case SimResolution.Coarse : Return 0.01
                Case Else : Return 0.005
            End Select
        End Get
    End Property

    <Category("4. Machine"), DisplayName("G-code units"),
     Description("Units written to the G-code file (G20 inches or G21 millimeters). The job is always designed in inches.")>
    Public Property Units As OutputUnits = OutputUnits.Inches

    ''' <summary>Copy for use on a worker thread while the grid keeps editing the original.</summary>
    Public Function Clone() As CarveSettings
        Dim c = DirectCast(MemberwiseClone(), CarveSettings)
        c.FontLarge = FontLarge.Clone()
        c.FontMedium = FontMedium.Clone()
        c.FontSmall = FontSmall.Clone()
        Return c
    End Function

    ''' <summary>The flat depth actually used: never deeper than the tool can cut.</summary>
    <JsonIgnore>
    Public ReadOnly Property EffectiveFlatDepth As Double
        Get
            Return Math.Min(Math.Max(FlatDepth, 0.0), MaxToolDepthIn)
        End Get
    End Property

    Public Function FontStyleValue() As FontStyle
        Return FontLarge.Style()
    End Function

    ''' <summary>Returns a list of problems that would prevent generation, or an empty list.</summary>
    Public Function Validate() As List(Of String)
        Dim errs As New List(Of String)
        If String.IsNullOrWhiteSpace(FontLarge.Family) OrElse String.IsNullOrWhiteSpace(FontMedium.Family) OrElse String.IsNullOrWhiteSpace(FontSmall.Family) Then
            errs.Add("Every size needs a font family.")
        End If
        If BlankWidthIn <= 0 OrElse BlankHeightIn <= 0 Then errs.Add("Blank width and height must be positive.")
        If MarginIn < 0 Then errs.Add("Margin cannot be negative.")
        If SizeLargeIn <= 0 OrElse SizeMediumIn <= 0 OrElse SizeSmallIn <= 0 Then errs.Add("All three letter sizes must be positive.")
        If ToolDiameterIn <= 0 Then errs.Add("Tool diameter must be positive.")
        If IncludedAngleDeg <= 0 OrElse IncludedAngleDeg >= 180 Then errs.Add("Included angle must be between 0 and 180 degrees.")
        If DepthStep <= 0 Then errs.Add("Roughing depth step must be positive.")
        If FlatDepth <= 0 Then errs.Add("Flat depth must be positive.")
        If ClearStepover <= 0 Then errs.Add("Floor stepover must be positive.")
        If RoughAllowance < 0 Then errs.Add("Roughing allowance cannot be negative.")
        If FinishResolution <= 0 Then errs.Add("Finishing resolution must be positive.")
        If TipFlatIn < 0 OrElse TipFlatIn >= ToolDiameterIn Then errs.Add("Tip flat must be between 0 and the tool diameter.")
        If SafeZ <= 0 Then errs.Add("Safe Z must be above the stock (positive).")
        If ClearanceZ <= 0 OrElse ClearanceZ > SafeZ Then errs.Add("Clearance Z must be positive and not above Safe Z.")
        If FeedRate <= 0 OrElse PlungeRate <= 0 Then errs.Add("Feed and plunge rates must be positive.")
        If SpindleRpm <= 0 Then errs.Add("Spindle RPM must be positive.")
        If SpindleDwellSeconds < 0 Then errs.Add("Spindle dwell cannot be negative.")
        If CurveTolerance <= 0 Then errs.Add("Curve tolerance must be positive.")
        If LineSpacing <= 0 Then errs.Add("Line spacing must be positive.")
        Return errs
    End Function
End Class
