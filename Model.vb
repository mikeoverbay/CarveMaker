' ============================================================================
'  Model.vb
'  Shared data types for CarveMaker: geometry primitives, toolpath
'  containers and the user-editable job settings shown in the PropertyGrid.
'  All linear units are INCHES unless a member name says otherwise.
'  Z = 0 is the top of the stock; cutting depths are negative Z values.
' ============================================================================

Imports System.ComponentModel
Imports System.Drawing.Design
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
    ''' <summary>End-mill pocket clearing ring.</summary>
    Pocket
    ''' <summary>End-mill (or V-bit) profile loop.</summary>
    Profile
End Enum

''' <summary>A run of moves cut with one tool for one operation (the job is a list of these).</summary>
Public Class ToolpathSegment
    Public Property Name As String = ""
    Public Property Operation As CutOperation
    Public Property Tool As ToolDefinition
    ''' <summary>Spindle speed for this segment (S word).</summary>
    Public Property SpindleRpm As Double = 16000
    Public Property FirstMove As Integer
    Public Property MoveCount As Integer
    Public Property EstimatedMinutes As Double
    ''' <summary>Deepest Z of the segment (negative).</summary>
    Public Property MinZ As Double
End Class

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
    ''' <summary>Moves grouped by operation and tool, in machining order.</summary>
    Public Property Segments As New List(Of ToolpathSegment)

    ''' <summary>Bounds of the text block alone (for dragging it in the view); zero-size when there is no text.</summary>
    Public Property TextMinX As Double
    Public Property TextMinY As Double
    Public Property TextMaxX As Double
    Public Property TextMaxY As Double

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

Public Enum SimMeshDetail
    ''' <summary>One vertex per heightmap cell, up to 4 million cells.</summary>
    Full
    ''' <summary>Up to 1 million cells.</summary>
    Half
    ''' <summary>Up to 250 thousand cells: for integrated graphics.</summary>
    Low
End Enum

Public Enum CutOrder
    ''' <summary>Top line first; each line's letters left to right.</summary>
    LineByLine
    ''' <summary>Top line first; alternate lines run right to left to shorten the hop between lines.</summary>
    Serpentine
    ''' <summary>All letters left to right regardless of line (shortest X travel, long Y hops).</summary>
    LeftToRight
End Enum

''' <summary>What a drawing is: an imported SVG, or a shape or text drawn on the blank.</summary>
Public Enum DrawingKind
    Svg
    Rectangle
    Ellipse
    Polygon
    Star
    Area
    Text
End Enum

''' <summary>
''' A drawing placed on the blank: an imported SVG (its text embedded so the project
''' file is self-contained) or a shape or text drawn in the view. Either way the
''' natural outline (Shape) is cached per instance and scaled into the placed box.
''' </summary>
Public Class DesignObject
    <Category("Object"), DisplayName("Name")>
    Public Property Name As String = "Drawing"

    <Category("Object"), DisplayName("Type"), [ReadOnly](True), Description("An imported SVG drawing, or a shape or text drawn on the blank.")>
    Public Property Kind As DrawingKind = DrawingKind.Svg

    <Category("Object"), DisplayName("Source file"), [ReadOnly](True)>
    Public Property SvgFileName As String = ""

    <Browsable(False)>
    Public Property SvgContent As String = ""

    ''' <summary>Drawn kinds: what the outline saved in SvgContent was made from (see SavedOutlineKey).</summary>
    <Browsable(False)>
    Public Property OutlineKey As String = ""

    <Category("Placement"), DisplayName("X (in)"), Description("Left edge of the drawing's bounding box in machine coordinates.")>
    Public Property X As Double = 0.0

    <Category("Placement"), DisplayName("Y (in)"), Description("Bottom edge of the drawing's bounding box in machine coordinates.")>
    Public Property Y As Double = 0.0

    <Category("Placement"), DisplayName("Width (in)")>
    Public Property Width As Double = 1.0

    <Category("Placement"), DisplayName("Height (in)")>
    Public Property Height As Double = 1.0

    <Category("Placement"), DisplayName("Rotation (deg)"), Description("Counter-clockwise rotation about the centre of the bounding box. Drag the round handle above the selection to turn it.")>
    Public Property RotationDeg As Double = 0.0

    <Category("Placement"), DisplayName("Lock aspect ratio"), Description("Changing width or height keeps the drawing's proportions. When off, the side handles stretch it one way.")>
    Public Property LockAspect As Boolean = True

    <Category("Placement"), DisplayName("Mirror"), Description("Flip the drawing left-to-right (for stamps or reverse carving).")>
    Public Property Mirror As Boolean = False

    <Category("Object"), DisplayName("Visible"), Description("Hidden objects are not carved.")>
    Public Property Visible As Boolean = True

    ' ---- drawn shapes -------------------------------------------------------

    <Category("Shape"), DisplayName("Corner radius (in)"), Description("Rounds the rectangle's corners (0 = square corners).")>
    Public Property CornerRadius As Double = 0.0

    <Category("Shape"), DisplayName("Sides"), Description("Number of sides of the polygon (3 to 64).")>
    Public Property Sides As Integer = 6

    <Category("Shape"), DisplayName("Points"), Description("Number of points of the star (3 to 64).")>
    Public Property StarPoints As Integer = 5

    <Category("Shape"), DisplayName("Inner radius (%)"), Description("Depth of the star's inner corners as a percentage of its outer radius (10 to 95).")>
    Public Property StarInnerPercent As Double = 50.0

    ''' <summary>Corners of a free-form area at its natural size (inches, Y up): x0, y0, x1, y1, ...</summary>
    <Browsable(False)>
    Public Property AreaPoints As New List(Of Double)

    ' ---- drawn text ---------------------------------------------------------

    <Category("Text"), DisplayName("Text"), Editor(GetType(System.ComponentModel.Design.MultilineStringEditor), GetType(UITypeEditor)),
     Description("The words to carve; click ... for several lines. Double-click the text in the view to type in place.")>
    Public Property Text As String = ""

    <Category("Text"), DisplayName("Font"), TypeConverter(GetType(FontFamilyNameConverter)), Description("Installed font family.")>
    Public Property FontName As String = "Arial"

    <Category("Text"), DisplayName("Bold")>
    Public Property Bold As Boolean = False

    <Category("Text"), DisplayName("Italic")>
    Public Property Italic As Boolean = False

    <Category("Text"), DisplayName("Alignment"), Description("How the lines line up with each other.")>
    Public Property Align As TextAlign = TextAlign.Left

    <Category("Text"), DisplayName("Letter height (in)"), Description("Height of a capital letter. Dragging a corner of the text in the view changes it.")>
    Public Property LetterHeight As Double = 1.0

    <Category("Text"), DisplayName("Line spacing"), Description("Distance between lines as a multiple of the font's natural line height.")>
    Public Property LineSpacing As Double = 1.0

    ''' <summary>How this drawing is machined (shown as the Toolpath rows of the Drawings tab).</summary>
    <Browsable(False)>
    Public Property Machining As New Machining()

    ''' <summary>Natural outline (lower-left at 0,0); Nothing until built.</summary>
    <Browsable(False), JsonIgnore>
    Public Property Shape As SvgShapeSet

    <Browsable(False), JsonIgnore>
    Public Property ParseError As String

    ' Inputs the cached Shape was built from (see ShapeKey).
    Private _shapeKey As String
    Private _usesSavedOutline As Boolean

    ''' <summary>True when a drawn shape could not be built here and carves the outline saved with the project.</summary>
    <Browsable(False), JsonIgnore>
    Public ReadOnly Property UsesSavedOutline As Boolean
        Get
            Return _usesSavedOutline
        End Get
    End Property

    ''' <summary>Natural width / height of the drawing in inches (1 when unknown).</summary>
    <Browsable(False), JsonIgnore>
    Public ReadOnly Property NaturalAspect As Double
        Get
            If Shape Is Nothing OrElse Shape.Width <= 0 OrElse Shape.Height <= 0 Then Return 1.0
            Return Shape.Width / Shape.Height
        End Get
    End Property

    ''' <summary>True for drawings made in the view rather than imported.</summary>
    <Browsable(False), JsonIgnore>
    Public ReadOnly Property IsDrawn As Boolean
        Get
            Return Kind <> DrawingKind.Svg
        End Get
    End Property

    ''' <summary>Which rows the Drawings tab shows for this kind of drawing.</summary>
    Public Function ShowsProperty(propertyName As String) As Boolean
        Select Case propertyName
            Case NameOf(SvgFileName) : Return Kind = DrawingKind.Svg
            Case NameOf(CornerRadius) : Return Kind = DrawingKind.Rectangle
            Case NameOf(Sides) : Return Kind = DrawingKind.Polygon
            Case NameOf(StarPoints), NameOf(StarInnerPercent) : Return Kind = DrawingKind.Star
            Case NameOf(Text), NameOf(FontName), NameOf(Bold), NameOf(Italic), NameOf(Align), NameOf(LetterHeight), NameOf(LineSpacing)
                Return Kind = DrawingKind.Text
            Case NameOf(LockAspect) : Return Kind <> DrawingKind.Text   ' text always scales evenly
        End Select
        Return True
    End Function

    ''' <summary>
    ''' Everything the natural outline depends on. Rectangles and ellipses are built at
    ''' their placed size (true corner radius, even curve tolerance); the other kinds
    ''' are built once and scaled into the box.
    ''' </summary>
    Public Function ShapeKey(curveTolerance As Double) As String
        Dim ci = CultureInfo.InvariantCulture
        Dim tol = curveTolerance.ToString("R", ci)
        Select Case Kind
            Case DrawingKind.Rectangle
                Return String.Join("|", "rect", tol, Width.ToString("R", ci), Height.ToString("R", ci), CornerRadius.ToString("R", ci))
            Case DrawingKind.Ellipse
                Return String.Join("|", "ellipse", tol, Width.ToString("R", ci), Height.ToString("R", ci))
            Case DrawingKind.Polygon
                Return "polygon|" & Sides.ToString(ci)
            Case DrawingKind.Star
                Return String.Join("|", "star", StarPoints.ToString(ci), StarInnerPercent.ToString("R", ci))
            Case DrawingKind.Area
                Return "area|" & String.Join(",", If(AreaPoints, New List(Of Double)).Select(Function(v) v.ToString("R", ci)))
            Case DrawingKind.Text
                Return String.Join("|", "text", tol, If(FontName, ""), Bold, Italic, Align, LetterHeight.ToString("R", ci), LineSpacing.ToString("R", ci), TextWithLf())
            Case Else
                Return "svg|" & tol
        End Select
    End Function

    ''' <summary>
    ''' The inputs that decide a drawn shape's outline apart from its size (the saved outline
    ''' is scaled into the box): text, font, style, alignment and spacing for text.
    ''' </summary>
    Public Function SavedOutlineKey() As String
        If Kind = DrawingKind.Text Then
            Return String.Join("|", "text", If(FontName, ""), Bold, Italic, Align, LineSpacing.ToString("R", CultureInfo.InvariantCulture), TextWithLf())
        End If
        Return ShapeKey(0)
    End Function

    ''' <summary>The text with every line break as LF (the grid's editor writes CRLF, the in-place editor LF).</summary>
    Private Function TextWithLf() As String
        Return If(Text, "").Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
    End Function

    ''' <summary>Builds (or parses) the natural outline when anything it depends on changed.</summary>
    Public Sub EnsureParsed(curveTolerance As Double)
        ' One floor for every caller (the view clamps too), so they share the cached outline.
        Dim tol = Math.Max(0.0001, curveTolerance)
        Dim key = ShapeKey(tol)
        If key = _shapeKey AndAlso (Shape IsNot Nothing OrElse ParseError IsNot Nothing) Then Return
        _shapeKey = key
        Shape = Nothing
        ParseError = Nothing
        _usesSavedOutline = False
        Try
            Shape = If(Kind = DrawingKind.Svg, SvgImport.Parse(SvgContent, tol), DrawingShapes.Build(Me, tol))
        Catch ex As Exception
            ParseError = ex.Message
            ' A drawn shape that cannot be built on this PC (a font it lacks) carves the outline
            ' saved with the project, as long as the drawing has not been changed since.
            If IsDrawn AndAlso Not String.IsNullOrEmpty(SvgContent) AndAlso OutlineKey = SavedOutlineKey() Then
                Try
                    Dim saved = SvgImport.Parse(SvgContent, tol)
                    If saved.Paths.Count > 0 Then
                        saved.Warnings.Insert(0, ex.Message & " Carving the outline saved with the project.")
                        Shape = saved
                        ParseError = Nothing
                        _usesSavedOutline = True
                    End If
                Catch
                    ' Keep the original error.
                End Try
            End If
        End Try
    End Sub

    ''' <summary>Forces a rebuild (after the tolerance changed).</summary>
    Public Sub InvalidateShape()
        Shape = Nothing
        ParseError = Nothing
        _shapeKey = Nothing
    End Sub

    ''' <summary>Keeps shape and text values in their valid ranges.</summary>
    ''' <summary>
    ''' Puts back defaults for missing or impossible values (a damaged file, a NaN typed in the
    ''' grid) without changing anything that is merely small.
    ''' </summary>
    Public Sub Repair()
        If Name Is Nothing Then Name = "Drawing"
        If SvgContent Is Nothing Then SvgContent = ""
        If OutlineKey Is Nothing Then OutlineKey = ""
        If Text Is Nothing Then Text = ""
        If String.IsNullOrWhiteSpace(FontName) Then FontName = "Arial"
        If AreaPoints Is Nothing Then AreaPoints = New List(Of Double)
        If AreaPoints.Any(Function(v) Not Double.IsFinite(v)) Then AreaPoints.Clear()
        If Not Double.IsFinite(X) Then X = 0
        If Not Double.IsFinite(Y) Then Y = 0
        If Not Double.IsFinite(Width) OrElse Width <= 0 Then Width = 1
        If Not Double.IsFinite(Height) OrElse Height <= 0 Then Height = 1
        If Not Double.IsFinite(RotationDeg) Then RotationDeg = 0
        If Not Double.IsFinite(CornerRadius) Then CornerRadius = 0
        If Not Double.IsFinite(StarInnerPercent) Then StarInnerPercent = 50
        If Not Double.IsFinite(LetterHeight) Then LetterHeight = 1
        If Not Double.IsFinite(LineSpacing) Then LineSpacing = 1
    End Sub

    Public Sub Normalize()
        Repair()
        Text = TextWithLf()
        Sides = Math.Max(3, Math.Min(64, Sides))
        StarPoints = Math.Max(3, Math.Min(64, StarPoints))
        StarInnerPercent = Math.Max(10.0, Math.Min(95.0, StarInnerPercent))
        CornerRadius = Math.Max(0.0, CornerRadius)
        LetterHeight = Math.Max(0.02, Math.Min(100.0, LetterHeight))
        LineSpacing = Math.Max(0.5, Math.Min(5.0, LineSpacing))
        ' A text box is exactly its lettering, which can be smaller than the usual minimum (a dot).
        Dim minSize = If(Kind = DrawingKind.Text, 0.0001, 0.01)
        Width = Math.Max(minSize, Width)
        Height = Math.Max(minSize, Height)
    End Sub

    ''' <summary>Sizes the object to its natural size, or to fit a box, keeping proportions.</summary>
    Public Sub FitTo(maxW As Double, maxH As Double)
        Dim a = NaturalAspect
        Dim w = maxW, h = maxW / a
        If h > maxH Then
            h = maxH : w = maxH * a
        End If
        If Kind = DrawingKind.Text AndAlso Width > 0 Then LetterHeight = Math.Round(LetterHeight * w / Width, 4)
        Width = Math.Round(Math.Max(0.01, w), 4)
        Height = Math.Round(Math.Max(0.01, h), 4)
    End Sub

    ''' <summary>
    ''' Text: makes the box the size of the lettering. The side the lines hang from (left,
    ''' middle or right by alignment) and the first line stay where they are: the first line's
    ''' position comes from the font's metrics, so typing a letter with an ascender or descender,
    ''' or changing the line spacing, does not move the text. previous is the outline the box was sized for (default: the
    ''' cached one, which is still the old outline when only the text changed).
    ''' </summary>
    Public Sub FitTextBox(curveTolerance As Double, Optional previous As SvgShapeSet = Nothing)
        If Kind <> DrawingKind.Text Then Return
        Dim before = If(previous, Shape)
        EnsureParsed(curveTolerance)
        If Shape Is Nothing OrElse Shape.Width <= 0 OrElse Shape.Height <= 0 Then Return
        Dim ax = If(Align = TextAlign.Left, -0.5, If(Align = TextAlign.Right, 0.5, 0.0))
        If Mirror Then ax = -ax
        Dim byLine = before IsNot Nothing AndAlso before.Height > 0 AndAlso Not Double.IsNaN(before.FirstLineY) AndAlso Not Double.IsNaN(Shape.FirstLineY)
        Dim anchorY = If(byLine, before.FirstLineY * Height / before.Height - Height / 2, Height / 2)
        Dim anchor = LocalToWorld(ax * Width, anchorY)
        If _usesSavedOutline Then
            ' A saved outline is not at the letter height: keep the height, take its proportions.
            Width = Math.Round(Height * Shape.Width / Shape.Height, 4)
        Else
            Width = Math.Round(Shape.Width, 4)
            Height = Math.Round(Shape.Height, 4)
        End If
        PlaceLocal(ax * Width, If(byLine, Shape.FirstLineY * Height / Shape.Height - Height / 2, Height / 2), anchor)
    End Sub

    ''' <summary>Scales the box by k about the top corner the text hangs from (text carving a saved outline).</summary>
    Public Sub ScaleBoxFromTop(k As Double)
        If k <= 0 OrElse k = 1 Then Return
        Dim ax = If(Align = TextAlign.Left, -0.5, If(Align = TextAlign.Right, 0.5, 0.0))
        If Mirror Then ax = -ax
        Dim anchor = LocalToWorld(ax * Width, Height / 2)
        Width = Math.Round(Width * k, 4)
        Height = Math.Round(Height * k, 4)
        PlaceLocal(ax * Width, Height / 2, anchor)
    End Sub

    ''' <summary>
    ''' Text: scales the lettering by k (a corner drag). The box is rebuilt at the new
    ''' letter height and the given corner (0..3, see Corners) stays where it is.
    ''' </summary>
    Public Sub ScaleText(k As Double, fixedCorner As Integer, curveTolerance As Double)
        If Kind <> DrawingKind.Text OrElse k <= 0 Then Return
        Dim anchor = Corners()(fixedCorner And 3)
        LetterHeight = Math.Round(Math.Max(0.02, Math.Min(100.0, LetterHeight * k)), 4)
        EnsureParsed(curveTolerance)
        If Shape IsNot Nothing AndAlso Shape.Width > 0 AndAlso Shape.Height > 0 AndAlso Not _usesSavedOutline Then
            Width = Math.Round(Shape.Width, 4)
            Height = Math.Round(Shape.Height, 4)
        Else
            Width = Math.Round(Math.Max(0.01, Width * k), 4)
            Height = Math.Round(Math.Max(0.01, Height * k), 4)
        End If
        PlaceCorner(fixedCorner, anchor)
    End Sub

    ''' <summary>Machine point of a point in the box's own frame (origin at the centre, before rotation).</summary>
    Public Function LocalToWorld(lx As Double, ly As Double) As Pt2
        Dim cx = X + Width / 2, cy = Y + Height / 2
        Dim c = Math.Cos(RotationDeg * Math.PI / 180), s = Math.Sin(RotationDeg * Math.PI / 180)
        Return New Pt2(cx + lx * c - ly * s, cy + lx * s + ly * c)
    End Function

    ''' <summary>Moves the box so its local point (lx, ly) lands on a machine point.</summary>
    Public Sub PlaceLocal(lx As Double, ly As Double, target As Pt2)
        Dim now = LocalToWorld(lx, ly)
        X = Math.Round(X + target.X - now.X, 4)
        Y = Math.Round(Y + target.Y - now.Y, 4)
    End Sub

    ''' <summary>Moves the box so one of its corners (0..3, see Corners) lands on a machine point.</summary>
    Public Sub PlaceCorner(corner As Integer, target As Pt2)
        Dim i = corner And 3
        Dim lx = If(i = 1 OrElse i = 2, Width / 2, -Width / 2)
        Dim ly = If(i >= 2, Height / 2, -Height / 2)
        PlaceLocal(lx, ly, target)
    End Sub

    ''' <summary>Corners of the placed (rotated) bounding box, counter-clockwise from the lower-left.</summary>
    Public Function Corners() As Pt2()
        Dim r(3) As Pt2
        r(0) = LocalToWorld(-Width / 2, -Height / 2)
        r(1) = LocalToWorld(Width / 2, -Height / 2)
        r(2) = LocalToWorld(Width / 2, Height / 2)
        r(3) = LocalToWorld(-Width / 2, Height / 2)
        Return r
    End Function

    ''' <summary>True when the machine point lies inside the placed bounding box.</summary>
    Public Function HitTest(px As Double, py As Double) As Boolean
        Dim cx = X + Width / 2, cy = Y + Height / 2
        Dim c = Math.Cos(-RotationDeg * Math.PI / 180), s = Math.Sin(-RotationDeg * Math.PI / 180)
        Dim dx = px - cx, dy = py - cy
        Dim lx = dx * c - dy * s, ly = dx * s + dy * c
        Return Math.Abs(lx) <= Width / 2 AndAlso Math.Abs(ly) <= Height / 2
    End Function

    ''' <summary>The drawing's polygons scaled, mirrored, rotated and moved into place (machine inches).</summary>
    Public Function PlacedPaths(curveTolerance As Double) As Clipper2Lib.PathsD
        EnsureParsed(curveTolerance)
        Dim result As New Clipper2Lib.PathsD()
        If Shape Is Nothing OrElse Shape.Paths.Count = 0 OrElse Shape.Width <= 0 OrElse Shape.Height <= 0 Then Return result
        Dim sx = Width / Shape.Width, sy = Height / Shape.Height
        Dim cx = X + Width / 2, cy = Y + Height / 2
        Dim c = Math.Cos(RotationDeg * Math.PI / 180), s = Math.Sin(RotationDeg * Math.PI / 180)
        For Each p In Shape.Paths
            Dim q As New Clipper2Lib.PathD(p.Count)
            For Each pt In p
                ' Natural -> local (centred, scaled, mirrored) -> rotated -> placed.
                Dim lx = (pt.x - Shape.Width / 2) * sx
                Dim ly = (pt.y - Shape.Height / 2) * sy
                If Mirror Then lx = -lx
                q.Add(New Clipper2Lib.PointD(cx + lx * c - ly * s, cy + lx * s + ly * c))
            Next
            ' Mirroring flips orientation; the engine's union re-normalizes, but keep outers positive.
            If Mirror Then q.Reverse()
            result.Add(q)
        Next
        Return result
    End Function

    Public Function Clone() As DesignObject
        Dim o = DirectCast(MemberwiseClone(), DesignObject)
        o.Machining = If(Machining?.Clone(), New Machining())
        o.AreaPoints = New List(Of Double)(If(AreaPoints, New List(Of Double)))
        Return o
    End Function

    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

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
    <Category("1. Blank"), DisplayName("Blank width X (in)"),
     Description("Width of the stock blank. Lines are justified Left / Center / Right across this width.")>
    Public Property BlankWidthIn As Double = 6.0

    <Category("1. Blank"), DisplayName("Blank height Y (in)"),
     Description("Height of the stock blank. The text block is placed Top / Middle / Bottom within it.")>
    Public Property BlankHeightIn As Double = 3.0

    <Category("1. Blank"), DisplayName("Blank origin X (in)"),
     Description("Machine X of the blank's lower-left corner. Use 0 when you zero the machine at the lower-left corner, or -width/2 when you zero at the center.")>
    Public Property BlankOriginX As Double = 0.0

    <Category("1. Blank"), DisplayName("Blank origin Y (in)"),
     Description("Machine Y of the blank's lower-left corner.")>
    Public Property BlankOriginY As Double = 0.0

    <Category("1. Blank"), DisplayName("Margin (in)"),
     Description("Distance kept from the blank's edges: by left/right-justified and top/bottom-placed text, and by drawings sized with Fit to Blank.")>
    Public Property MarginIn As Double = 0.25

    <Category("2. Placement on the blank"), DisplayName("Vertical placement"),
     Description("Where the whole text block sits on the blank: against the top margin, centered, or against the bottom margin (Margin is under Machine Settings > 1. Blank).")>
    Public Property VerticalPlacement As VerticalAlign = VerticalAlign.Middle

    <Category("2. Placement on the blank"), DisplayName("Text offset X (in)"),
     Description("Fine adjustment added to the justified text position.")>
    Public Property TextOffsetX As Double = 0.0

    <Category("2. Placement on the blank"), DisplayName("Text offset Y (in)"),
     Description("Fine adjustment added to the placed text position.")>
    Public Property TextOffsetY As Double = 0.0

    ' ---------------------------------------------------------------- Text
    <Category("1. Sizes and fonts"), DisplayName("Size 1 - Large (in)"),
     Description("Letter size used by lines formatted 'Large' in the editor.")>
    Public Property SizeLargeIn As Double = 1.0

    <Category("1. Sizes and fonts"), DisplayName("Size 1 - Large font"),
     Description("Font for lines formatted 'Large'. Expand to pick the family and style, or use Font > Choose Font... with the caret on such a line.")>
    Public Property FontLarge As New FontChoice("Arial")

    <Category("1. Sizes and fonts"), DisplayName("Size 2 - Medium (in)"),
     Description("Letter size used by lines formatted 'Medium' in the editor.")>
    Public Property SizeMediumIn As Double = 0.6

    <Category("1. Sizes and fonts"), DisplayName("Size 2 - Medium font")>
    Public Property FontMedium As New FontChoice("Arial")

    <Category("1. Sizes and fonts"), DisplayName("Size 3 - Small (in)"),
     Description("Letter size used by lines formatted 'Small' in the editor.")>
    Public Property SizeSmallIn As Double = 0.35

    <Category("1. Sizes and fonts"), DisplayName("Size 3 - Small font")>
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

    <Category("1. Sizes and fonts"), DisplayName("Size means"),
     Description("EmHeight: the font's design size (capitals are about 70% of it). CapHeight: a capital H is exactly this tall.")>
    Public Property SizeBy As SizeMode = SizeMode.CapHeight

    <Category("1. Sizes and fonts"), DisplayName("Line spacing"),
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

    ''' <summary>How the text is machined (shown as the 3. Toolpath rows of the Text tab).</summary>
    <Browsable(False)>
    Public Property TextMachining As New Machining()

    ' ---------------------------------------------------------------- Tool
    ''' <summary>
    ''' The V-bit the V-carve engine uses. Each toolpath has its own tool now
    ''' (Machining.Tool, copied in here by Machining.ApplyTo); this property stays for the
    ''' engines and for projects saved before per-toolpath tools.
    ''' </summary>
    <Browsable(False)>
    Public Property CarveTool As ToolDefinition = ToolDefinition.DefaultVBit()

    ' Read-only views of the carving tool for the V-carve engine. Diameter and angle are
    ' still written to the project file so older CarveMaker versions open it with the
    ' right bit; on load they only matter for files that predate the tool library.
    <Browsable(False)>
    Public ReadOnly Property ToolDiameterIn As Double
        Get
            Return If(CarveTool Is Nothing, 0.25, CarveTool.Diameter)
        End Get
    End Property

    <Browsable(False)>
    Public ReadOnly Property IncludedAngleDeg As Double
        Get
            Return If(CarveTool Is Nothing, 90.0, CarveTool.AngleDeg)
        End Get
    End Property

    ''' <summary>Library tools have sharp V tips; the engine still supports a flat for future tool types.</summary>
    <Browsable(False), JsonIgnore>
    Public ReadOnly Property TipFlatIn As Double
        Get
            Return 0.0
        End Get
    End Property

    ''' <summary>Deepest CarveTool can carve (see Machining.MaxUsableDepth for the grid).</summary>
    <Browsable(False), JsonIgnore>
    Public ReadOnly Property MaxToolDepthIn As Double
        Get
            Return If(CarveTool Is Nothing, 0.0, Math.Round(CarveTool.UsableDepth, 6))
        End Get
    End Property

    ' ---------------------------------------------------------------- Carve
    <Category("2. Carve"), DisplayName("Flat depth (in)"),
     Description("Maximum V-carve depth. Areas wider than the V can reach at this depth are cleared flat here. Limited to each V-bit's max usable depth (shown in its toolpath rows); keep a little margin below it.")>
    Public Property FlatDepth As Double = 0.12

    <Category("2. Carve"), DisplayName("Roughing depth step (in)"),
     Description("Vertical distance between the constant-depth roughing contours.")>
    Public Property DepthStep As Double = 0.02

    <Category("2. Carve"), DisplayName("Roughing allowance (in)"),
     Description("Skin left by the roughing contours so they never rub the finished bevel. Removed by the centerline finishing pass.")>
    Public Property RoughAllowance As Double = 0.004

    <Category("2. Carve"), DisplayName("Centerline finishing pass"),
     Description("Final variable-depth pass along the center of each stroke: cuts the ridge, the sharp corners and the finished bevel exactly. Turn off only for quick previews.")>
    Public Property FinishPass As Boolean = True

    <Category("2. Carve"), DisplayName("Finishing resolution (in)"),
     Description("Spacing of the boundary samples used to trace the centerline. Smaller = smoother, slower to compute.")>
    Public Property FinishResolution As Double = 0.002

    <Category("2. Carve"), DisplayName("Floor stepover (in)"),
     Description("Spacing between flat-depth floor clearing passes. Scallop height is about half of this on straight runs and up to the stepover at corners.")>
    Public Property ClearStepover As Double = 0.006

    <Category("2. Carve"), DisplayName("Milling direction"),
     Description("Direction of the closed contours relative to the material still to be removed (inside the loop). Both flanks of a V cut are engaged, so this mostly affects the roughing contours.")>
    Public Property Direction As MillingDirection = MillingDirection.Climb

    <Category("2. Carve"), DisplayName("Curve tolerance (in)"),
     Description("Chord error allowed when flattening glyph curves into line segments.")>
    Public Property CurveTolerance As Double = 0.0005

    ' ---------------------------------------------------------------- Machine
    <Category("3. Machine"), DisplayName("Safe Z (in)"),
     Description("Height above the stock top used for rapid moves between letters and at the start and end.")>
    Public Property SafeZ As Double = 0.25

    <Category("3. Machine"), DisplayName("Clearance Z (in)"),
     Description("Height above the stock top used for short rapid moves inside one letter.")>
    Public Property ClearanceZ As Double = 0.05

    <Category("1. Blank"), DisplayName("Stock thickness (in)"),
     Description("Used only to warn when the flat depth would cut through the stock.")>
    Public Property StockThickness As Double = 0.75

    ''' <summary>Feeds and speed the engines use; set per toolpath by Machining.ApplyTo (kept for old projects).</summary>
    <Browsable(False)>
    Public Property FeedRate As Double = 40.0

    <Browsable(False)>
    Public Property PlungeRate As Double = 15.0

    <Category("3. Machine"), DisplayName("Rapid rate (in/min)"),
     Description("Used only for the cycle-time estimate.")>
    Public Property RapidRate As Double = 150.0

    <Browsable(False)>
    Public Property SpindleRpm As Integer = 16000

    <Category("3. Machine"), DisplayName("Spindle dwell (s)"),
     Description("G4 pause after M3 so the spindle reaches speed before the first plunge (seconds; 0 = none). FluidNC and GRBL read P in seconds.")>
    Public Property SpindleDwellSeconds As Double = 2.0

    <Category("3. Machine"), DisplayName("Retract to machine home (G53)"),
     Description("Write 'G0 G53 Z0' at the start (before the first XY move) and before M5 at the end, so the spindle is fully up whenever it travels. Needs a homed machine.")>
    Public Property ParkZAtEnd As Boolean = True

    <Category("3. Machine"), DisplayName("Dust collection M8 / M9"),
     Description("Write M8 on its own line before the spindle starts and M9 after M5. On this machine M8 switches the dust vacuum (the M7 socket is unused); on others it is flood coolant.")>
    Public Property CoolantOn As Boolean = True

    <Category("3. Machine"), DisplayName("Cut order"),
     Description("LineByLine: finish each line of text (top line first, left to right) before the next. Serpentine: same but alternate lines run right to left. LeftToRight: every letter by X position, which hops between lines.")>
    Public Property Order As CutOrder = CutOrder.LineByLine

    ' ---------------------------------------------------------------- Simulation
    <Category("4. Simulation"), DisplayName("Precision"),
     Description("Cell size of the material-removal heightmap: Fine 0.003"", Medium 0.005"", Coarse 0.010"". Finer costs GPU memory (blank area / cell size squared, 4 bytes per cell).")>
    Public Property SimResolution As SimResolution = SimResolution.Medium

    <Category("4. Simulation"), DisplayName("Max texture size (hardware)"), [ReadOnly](True), JsonIgnore,
     Description("Largest texture edge this graphics card supports. The heightmap is coarsened automatically when the blank needs more cells than this.")>
    Public ReadOnly Property SimMaxTextureSize As Integer
        Get
            Return HardwareMaxTextureSize
        End Get
    End Property

    ''' <summary>Filled in by the OpenGL view once the context exists.</summary>
    Public Shared Property HardwareMaxTextureSize As Integer = 0

    <Category("4. Simulation"), DisplayName("Display mesh"),
     Description("How finely the carved surface is drawn. Full: one vertex per heightmap cell, up to 4 million cells (discrete graphics card). Half: up to 1 million. Low: up to 250 thousand, for laptops with integrated graphics. Only the drawing changes, not the heightmap or its accuracy; the simulation bar shows ""drawn at"" when the mesh is lighter than the heightmap.")>
    Public Property SimMeshDetail As SimMeshDetail = SimMeshDetail.Full

    ''' <summary>Vertex budget (cells) of the drawn surface for the chosen display mesh.</summary>
    <Browsable(False), JsonIgnore>
    Public ReadOnly Property SimMeshCells As Double
        Get
            Select Case SimMeshDetail
                Case SimMeshDetail.Half : Return 1000000.0
                Case SimMeshDetail.Low : Return 250000.0
                Case Else : Return 4000000.0
            End Select
        End Get
    End Property

    ''' <summary>Heightmap cell size in inches for the chosen precision.</summary>
    <Browsable(False), JsonIgnore>
    Public ReadOnly Property SimCellSize As Double
        Get
            Select Case SimResolution
                Case SimResolution.Fine : Return 0.003
                Case SimResolution.Coarse : Return 0.01
                Case Else : Return 0.005
            End Select
        End Get
    End Property

    <Category("3. Machine"), DisplayName("G-code units"),
     Description("Units written to the G-code file (G20 inches or G21 millimeters). The job is always designed in inches.")>
    Public Property Units As OutputUnits = OutputUnits.Inches

    ''' <summary>Copy for use on a worker thread while the grid keeps editing the original.</summary>
    Public Function Clone() As CarveSettings
        Dim c = DirectCast(MemberwiseClone(), CarveSettings)
        c.FontLarge = FontLarge.Clone()
        c.FontMedium = FontMedium.Clone()
        c.FontSmall = FontSmall.Clone()
        c.CarveTool = If(CarveTool?.Clone(), ToolDefinition.DefaultVBit())
        c.TextMachining = If(TextMachining?.Clone(), New Machining())
        Return c
    End Function

    ''' <summary>The flat depth actually used: never deeper than the tool can cut.</summary>
    <Browsable(False), JsonIgnore>
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
        If DepthStep <= 0 Then errs.Add("Roughing depth step must be positive.")
        If FlatDepth <= 0 Then errs.Add("Flat depth must be positive.")
        If ClearStepover <= 0 Then errs.Add("Floor stepover must be positive.")
        If RoughAllowance < 0 Then errs.Add("Roughing allowance cannot be negative.")
        If FinishResolution <= 0 Then errs.Add("Finishing resolution must be positive.")
        If SafeZ <= 0 Then errs.Add("Safe Z must be above the stock (positive).")
        If ClearanceZ <= 0 OrElse ClearanceZ > SafeZ Then errs.Add("Clearance Z must be positive and not above Safe Z.")
        If SpindleDwellSeconds < 0 Then errs.Add("Spindle dwell cannot be negative.")
        If CurveTolerance <= 0 Then errs.Add("Curve tolerance must be positive.")
        If LineSpacing <= 0 Then errs.Add("Line spacing must be positive.")
        Return errs
    End Function
End Class
