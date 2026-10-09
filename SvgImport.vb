' ============================================================================
'  SvgImport.vb
'  Reads an SVG file into clean polygons (inches, Y up) ready for the carve
'  engine. Supported: <path> (all commands, absolute and relative, arcs),
'  <rect> (with rx/ry), <circle>, <ellipse>, <line>, <polyline>, <polygon>,
'  <g> with nested transforms, <use> of those elements, viewBox / width /
'  height units, fill-rule (nonzero / evenodd) per element, and stroke-only
'  art (converted to outlines by offsetting the stroke width). Not supported:
'  <text> (convert to paths in your drawing program), clip paths, gradients.
' ============================================================================

Imports System.Globalization
Imports System.Text.RegularExpressions
Imports System.Xml.Linq
Imports Clipper2Lib

''' <summary>Result of an SVG import: normalized polygons with their lower-left corner at (0,0).</summary>
Public Class SvgShapeSet
    ''' <summary>Outers positive, holes negative, inches, Y up, lower-left at the origin.</summary>
    Public Property Paths As New PathsD()
    Public Property Width As Double
    Public Property Height As Double
    Public Property Warnings As New List(Of String)
    Public Property ElementCount As Integer
End Class

''' <summary>2x3 affine matrix (SVG convention: a c e / b d f).</summary>
Public Structure Affine
    Public A, B, C, D, E, F As Double

    Public Shared ReadOnly Property Identity As Affine
        Get
            Return New Affine With {.A = 1, .D = 1}
        End Get
    End Property

    ''' <summary>this * other (apply other first, then this), matching SVG nesting.</summary>
    Public Function Times(o As Affine) As Affine
        Return New Affine With {
            .A = A * o.A + C * o.B, .B = B * o.A + D * o.B,
            .C = A * o.C + C * o.D, .D = B * o.C + D * o.D,
            .E = A * o.E + C * o.F + E, .F = B * o.E + D * o.F + F}
    End Function

    Public Function Apply(x As Double, y As Double) As PointD
        Return New PointD(A * x + C * y + E, B * x + D * y + F)
    End Function

    ''' <summary>Approximate uniform scale factor (sqrt of |determinant|), used for flattening tolerances.</summary>
    Public ReadOnly Property Scale As Double
        Get
            Return Math.Sqrt(Math.Abs(A * D - B * C))
        End Get
    End Property
End Structure

Public Module SvgImport

    Private ReadOnly Ci As CultureInfo = CultureInfo.InvariantCulture
    Private ReadOnly NumberRx As New Regex("[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", RegexOptions.Compiled)

    ''' <summary>Loads an SVG file. Throws on unreadable XML; geometry problems become warnings.</summary>
    Public Function Load(path As String, curveToleranceIn As Double) As SvgShapeSet
        Return Parse(IO.File.ReadAllText(path), curveToleranceIn)
    End Function

    ''' <summary>Parses SVG text.</summary>
    Public Function Parse(svgText As String, curveToleranceIn As Double) As SvgShapeSet
        Dim result As New SvgShapeSet()
        Dim doc As XDocument = XDocument.Parse(svgText, LoadOptions.None)
        Dim root As XElement = doc.Root
        If root Is Nothing OrElse root.Name.LocalName <> "svg" Then
            Throw New FormatException("The file is not an SVG document (no <svg> root element).")
        End If

        ' ---- document units: user unit -> inches ----------------------------
        Dim vb As Double() = ParseViewBox(Attr(root, "viewBox"))
        Dim widthIn As Double = ParseLength(Attr(root, "width"), Double.NaN)
        Dim heightIn As Double = ParseLength(Attr(root, "height"), Double.NaN)
        Dim unitsPerIn As Double     ' user units per inch
        Dim originX As Double = 0, originY As Double = 0
        If vb IsNot Nothing AndAlso vb(2) > 0 AndAlso vb(3) > 0 Then
            originX = vb(0) : originY = vb(1)
            If Not Double.IsNaN(widthIn) AndAlso widthIn > 0 Then
                unitsPerIn = vb(2) / widthIn
            ElseIf Not Double.IsNaN(heightIn) AndAlso heightIn > 0 Then
                unitsPerIn = vb(3) / heightIn
            Else
                unitsPerIn = 96.0          ' CSS px
            End If
        Else
            unitsPerIn = 96.0              ' no viewBox: user units are px
        End If
        Dim toInches As Affine = New Affine With {.A = 1 / unitsPerIn, .D = 1 / unitsPerIn, .E = -originX / unitsPerIn, .F = -originY / unitsPerIn}

        ' ---- walk the tree ----------------------------------------------------
        Dim ctx As New ParseContext With {
            .Tolerance = Math.Max(0.0001, curveToleranceIn),
            .Result = result,
            .Defs = New Dictionary(Of String, XElement)}
        For Each el In root.Descendants()
            Dim id = Attr(el, "id")
            If Not String.IsNullOrEmpty(id) AndAlso Not ctx.Defs.ContainsKey(id) Then ctx.Defs(id) = el
        Next
        Dim shapes As New PathsD()
        WalkElement(root, toInches, New StyleState(), ctx, shapes, 0)

        If shapes.Count = 0 Then
            result.Warnings.Add("No fillable or stroked shapes were found in the SVG.")
            Return result
        End If

        ' Union everything (already individually normalized), flip Y, move to the origin.
        Dim merged = Clipper.Union(shapes, Nothing, FillRule.NonZero, GlyphOutline.ClipperPrecision)
        Dim flipped As New PathsD(merged.Count)
        For Each p In merged
            Dim q As New PathD(p.Count)
            For Each pt In p
                q.Add(New PointD(pt.x, -pt.y))
            Next
            flipped.Add(q)
        Next
        ' Flipping reverses orientation; re-normalize so outers are positive again.
        merged = Clipper.Union(flipped, Nothing, FillRule.NonZero, GlyphOutline.ClipperPrecision)
        Dim b = Clipper.GetBounds(merged)
        result.Paths = Clipper.TranslatePaths(merged, -b.left, -b.top)
        result.Width = b.right - b.left
        result.Height = b.bottom - b.top
        Return result
    End Function

    ' =====================================================================
    '  tree walking
    ' =====================================================================

    Private Class ParseContext
        Public Tolerance As Double
        Public Result As SvgShapeSet
        Public Defs As Dictionary(Of String, XElement)
    End Class

    ''' <summary>Inherited presentation state.</summary>
    Private Structure StyleState
        Public Fill As String          ' Nothing = inherit default (black)
        Public Stroke As String
        Public StrokeWidth As Double   ' user units; NaN = unset (1)
        Public FillRuleEvenOdd As Boolean
        Public Hidden As Boolean

        Public Function With_(el As XElement) As StyleState
            Dim s = Me
            Dim st = ParseStyle(el)
            Dim v As String = Nothing
            If st.TryGetValue("fill", v) Then s.Fill = v
            If st.TryGetValue("stroke", v) Then s.Stroke = v
            If st.TryGetValue("stroke-width", v) Then
                Dim w = ParseNumber(v)
                If Not Double.IsNaN(w) Then s.StrokeWidth = w
            End If
            If st.TryGetValue("fill-rule", v) Then s.FillRuleEvenOdd = (v.Trim().ToLowerInvariant() = "evenodd")
            If st.TryGetValue("display", v) AndAlso v.Trim().ToLowerInvariant() = "none" Then s.Hidden = True
            If st.TryGetValue("visibility", v) AndAlso v.Trim().ToLowerInvariant() = "hidden" Then s.Hidden = True
            Return s
        End Function
    End Structure

    Private Sub WalkElement(el As XElement, m As Affine, inherited As StyleState, ctx As ParseContext, shapes As PathsD, depth As Integer)
        If depth > 64 Then Return
        Dim name = el.Name.LocalName
        Dim style = inherited.With_(el)
        If style.Hidden Then Return
        Dim local = m.Times(ParseTransform(Attr(el, "transform")))

        Select Case name
            Case "svg", "g", "a", "switch"
                For Each child In el.Elements()
                    WalkElement(child, local, style, ctx, shapes, depth + 1)
                Next
            Case "defs", "symbol", "clipPath", "mask", "marker", "pattern", "metadata", "title", "desc", "style", "linearGradient", "radialGradient", "filter"
                ' Not rendered directly.
            Case "use"
                Dim href = Attr(el, "href")
                If String.IsNullOrEmpty(href) Then href = el.Attributes().Where(Function(a) a.Name.LocalName = "href").Select(Function(a) a.Value).FirstOrDefault()
                If Not String.IsNullOrEmpty(href) AndAlso href.StartsWith("#") Then
                    Dim target As XElement = Nothing
                    If ctx.Defs.TryGetValue(href.Substring(1), target) Then
                        Dim ux = ParseNumber(Attr(el, "x")), uy = ParseNumber(Attr(el, "y"))
                        Dim shift As New Affine With {.A = 1, .D = 1, .E = If(Double.IsNaN(ux), 0, ux), .F = If(Double.IsNaN(uy), 0, uy)}
                        WalkElement(target, local.Times(shift), style, ctx, shapes, depth + 1)
                    End If
                End If
            Case "text", "tspan"
                If depth >= 0 AndAlso name = "text" Then ctx.Result.Warnings.Add("Text elements are not imported; convert text to paths (outlines) in your drawing program.")
            Case "image"
                ctx.Result.Warnings.Add("Embedded images are ignored.")
            Case "path", "rect", "circle", "ellipse", "line", "polyline", "polygon"
                Dim subpaths As List(Of SubPath) = ElementGeometry(el, name, local, ctx)
                If subpaths Is Nothing OrElse subpaths.Count = 0 Then Return
                ctx.Result.ElementCount += 1
                AddShape(subpaths, style, local, shapes)
        End Select
    End Sub

    ''' <summary>A flattened subpath in inches (document orientation, Y down).</summary>
    Private Class SubPath
        Public Points As New PathD()
        Public Closed As Boolean
    End Class

    ''' <summary>Turns fill and/or stroke of an element into normalized polygons.</summary>
    Private Sub AddShape(subpaths As List(Of SubPath), style As StyleState, m As Affine, shapes As PathsD)
        Dim fill = If(style.Fill, "black").Trim().ToLowerInvariant()
        Dim stroke = If(style.Stroke, "none").Trim().ToLowerInvariant()
        Dim hasFill = fill <> "none" AndAlso fill <> "transparent"
        Dim hasStroke = stroke <> "none" AndAlso stroke <> "transparent"

        If hasFill Then
            Dim raw As New PathsD()
            For Each sp In subpaths
                If sp.Points.Count >= 3 Then raw.Add(sp.Points)       ' fill closes open subpaths implicitly
            Next
            If raw.Count > 0 Then
                Dim rule = If(style.FillRuleEvenOdd, FillRule.EvenOdd, FillRule.NonZero)
                Dim norm = Clipper.Union(raw, Nothing, rule, GlyphOutline.ClipperPrecision)
                shapes.AddRange(norm)
            End If
        End If

        If hasStroke Then
            Dim w = If(Double.IsNaN(style.StrokeWidth), 1.0, style.StrokeWidth) * m.Scale   ' user units -> inches
            If w > 0 Then
                Dim closedPaths As New PathsD(), openPaths As New PathsD()
                For Each sp In subpaths
                    If sp.Points.Count < 2 Then Continue For
                    If sp.Closed AndAlso sp.Points.Count >= 3 Then closedPaths.Add(sp.Points) Else openPaths.Add(sp.Points)
                Next
                If closedPaths.Count > 0 Then
                    shapes.AddRange(Clipper.InflatePaths(closedPaths, w / 2, JoinType.Round, EndType.Joined, 2.0, GlyphOutline.ClipperPrecision, 0.0005))
                End If
                If openPaths.Count > 0 Then
                    shapes.AddRange(Clipper.InflatePaths(openPaths, w / 2, JoinType.Round, EndType.Round, 2.0, GlyphOutline.ClipperPrecision, 0.0005))
                End If
            End If
        End If
    End Sub

    ' =====================================================================
    '  element geometry (returns flattened subpaths in inches)
    ' =====================================================================

    Private Function ElementGeometry(el As XElement, name As String, m As Affine, ctx As ParseContext) As List(Of SubPath)
        Dim tolUser As Double = ctx.Tolerance / Math.Max(m.Scale, 0.000000001)   ' tolerance in user units
        Select Case name
            Case "path"
                Return ParsePathData(Attr(el, "d"), m, tolUser, ctx)
            Case "rect"
                Dim x = Num(el, "x", 0), y = Num(el, "y", 0), w = Num(el, "width", 0), h = Num(el, "height", 0)
                Dim rx = Num(el, "rx", Double.NaN), ry = Num(el, "ry", Double.NaN)
                If Double.IsNaN(rx) AndAlso Double.IsNaN(ry) Then rx = 0 : ry = 0
                If Double.IsNaN(rx) Then rx = ry
                If Double.IsNaN(ry) Then ry = rx
                rx = Math.Min(Math.Abs(rx), w / 2) : ry = Math.Min(Math.Abs(ry), h / 2)
                If w <= 0 OrElse h <= 0 Then Return Nothing
                Dim d As String
                If rx > 0 AndAlso ry > 0 Then
                    d = String.Format(Ci, "M{0},{1} H{2} A{3},{4} 0 0 1 {5},{6} V{7} A{3},{4} 0 0 1 {2},{8} H{9} A{3},{4} 0 0 1 {10},{7} V{6} A{3},{4} 0 0 1 {9},{1} Z",
                                      x + rx, y, x + w - rx, rx, ry, x + w, y + ry, y + h - ry, y + h, x + rx, x)
                Else
                    d = String.Format(Ci, "M{0},{1} H{2} V{3} H{0} Z", x, y, x + w, y + h)
                End If
                Return ParsePathData(d, m, tolUser, ctx)
            Case "circle"
                Dim cx = Num(el, "cx", 0), cy = Num(el, "cy", 0), r = Num(el, "r", 0)
                If r <= 0 Then Return Nothing
                Return EllipseSubpath(cx, cy, r, r, m, tolUser)
            Case "ellipse"
                Dim cx = Num(el, "cx", 0), cy = Num(el, "cy", 0), rx = Num(el, "rx", 0), ry = Num(el, "ry", 0)
                If rx <= 0 OrElse ry <= 0 Then Return Nothing
                Return EllipseSubpath(cx, cy, rx, ry, m, tolUser)
            Case "line"
                Dim sp As New SubPath()
                sp.Points.Add(m.Apply(Num(el, "x1", 0), Num(el, "y1", 0)))
                sp.Points.Add(m.Apply(Num(el, "x2", 0), Num(el, "y2", 0)))
                Return New List(Of SubPath) From {sp}
            Case "polyline", "polygon"
                Dim nums = Numbers(Attr(el, "points"))
                Dim sp As New SubPath With {.Closed = (name = "polygon")}
                For i = 0 To nums.Count - 2 Step 2
                    sp.Points.Add(m.Apply(nums(i), nums(i + 1)))
                Next
                Return If(sp.Points.Count >= 2, New List(Of SubPath) From {sp}, Nothing)
        End Select
        Return Nothing
    End Function

    Private Function EllipseSubpath(cx As Double, cy As Double, rx As Double, ry As Double, m As Affine, tolUser As Double) As List(Of SubPath)
        Dim r = Math.Max(rx, ry)
        Dim n As Integer = SegmentsForArc(r, 2 * Math.PI, tolUser)
        Dim sp As New SubPath With {.Closed = True}
        For i = 0 To n - 1
            Dim a = 2 * Math.PI * i / n
            sp.Points.Add(m.Apply(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)))
        Next
        Return New List(Of SubPath) From {sp}
    End Function

    ''' <summary>Segments needed so the chord error stays under the tolerance.</summary>
    Private Function SegmentsForArc(radius As Double, sweep As Double, tol As Double) As Integer
        If radius <= tol Then Return Math.Max(4, CInt(Math.Ceiling(Math.Abs(sweep) / (Math.PI / 4))))
        Dim theta = 2 * Math.Acos(Math.Max(-1.0, Math.Min(1.0, 1 - tol / radius)))
        If theta <= 0 Then theta = 0.01
        Return Math.Max(4, Math.Min(2000, CInt(Math.Ceiling(Math.Abs(sweep) / theta))))
    End Function

    ' =====================================================================
    '  path data
    ' =====================================================================

    Private Function ParsePathData(d As String, m As Affine, tolUser As Double, ctx As ParseContext) As List(Of SubPath)
        Dim result As New List(Of SubPath)
        If String.IsNullOrWhiteSpace(d) Then Return result

        Dim tokens = TokenizePath(d)
        Dim i As Integer = 0
        Dim cmd As Char = ControlChars.NullChar
        Dim cur As New PointD(0, 0)         ' current point (user units)
        Dim start As New PointD(0, 0)       ' subpath start
        Dim lastCtrl As New PointD(0, 0)    ' last control point for S/T
        Dim lastCmd As Char = ControlChars.NullChar
        Dim sp As SubPath = Nothing

        Dim flushPoint = Sub(p As PointD)
                             If sp Is Nothing Then
                                 sp = New SubPath()
                                 result.Add(sp)
                                 sp.Points.Add(m.Apply(start.x, start.y))
                             End If
                             sp.Points.Add(m.Apply(p.x, p.y))
                         End Sub

        While i < tokens.Count
            Dim t = tokens(i)
            If t.IsCommand Then
                cmd = t.Command
                i += 1
                If cmd = "Z"c OrElse cmd = "z"c Then
                    If sp IsNot Nothing Then sp.Closed = True
                    cur = start
                    sp = Nothing
                    lastCmd = cmd
                    Continue While
                End If
            ElseIf cmd = ControlChars.NullChar Then
                ctx.Result.Warnings.Add("Path data does not start with a command; skipped.")
                Return result
            End If
            ' Implicit repetition: after M comes L, after m comes l.
            Dim rel As Boolean = Char.IsLower(cmd)
            Dim c As Char = Char.ToUpperInvariant(cmd)
            Dim need As Integer
            Select Case c
                Case "M"c, "L"c, "T"c : need = 2
                Case "H"c, "V"c : need = 1
                Case "C"c : need = 6
                Case "S"c, "Q"c : need = 4
                Case "A"c : need = 7
                Case Else
                    ctx.Result.Warnings.Add("Unknown path command '" & cmd & "'; rest of the path skipped.")
                    Return result
            End Select
            If i + need > tokens.Count OrElse tokens.Skip(i).Take(need).Any(Function(x) x.IsCommand) Then
                ctx.Result.Warnings.Add("Path command '" & cmd & "' has too few numbers; rest of the path skipped.")
                Return result
            End If
            Dim v(need - 1) As Double
            For k = 0 To need - 1
                v(k) = tokens(i + k).Value
            Next
            i += need

            Select Case c
                Case "M"c
                    Dim p = If(rel, New PointD(cur.x + v(0), cur.y + v(1)), New PointD(v(0), v(1)))
                    start = p : cur = p
                    sp = Nothing
                    cmd = If(rel, "l"c, "L"c)
                Case "L"c
                    Dim p = If(rel, New PointD(cur.x + v(0), cur.y + v(1)), New PointD(v(0), v(1)))
                    flushPoint(p) : cur = p
                Case "H"c
                    Dim p = New PointD(If(rel, cur.x + v(0), v(0)), cur.y)
                    flushPoint(p) : cur = p
                Case "V"c
                    Dim p = New PointD(cur.x, If(rel, cur.y + v(0), v(0)))
                    flushPoint(p) : cur = p
                Case "C"c, "S"c
                    Dim c1 As PointD, c2 As PointD, p As PointD
                    If c = "C"c Then
                        c1 = If(rel, New PointD(cur.x + v(0), cur.y + v(1)), New PointD(v(0), v(1)))
                        c2 = If(rel, New PointD(cur.x + v(2), cur.y + v(3)), New PointD(v(2), v(3)))
                        p = If(rel, New PointD(cur.x + v(4), cur.y + v(5)), New PointD(v(4), v(5)))
                    Else
                        Dim prevCubic = (Char.ToUpperInvariant(lastCmd) = "C"c OrElse Char.ToUpperInvariant(lastCmd) = "S"c)
                        c1 = If(prevCubic, New PointD(2 * cur.x - lastCtrl.x, 2 * cur.y - lastCtrl.y), cur)
                        c2 = If(rel, New PointD(cur.x + v(0), cur.y + v(1)), New PointD(v(0), v(1)))
                        p = If(rel, New PointD(cur.x + v(2), cur.y + v(3)), New PointD(v(2), v(3)))
                    End If
                    For Each q In FlattenCubic(cur, c1, c2, p, tolUser)
                        flushPoint(q)
                    Next
                    lastCtrl = c2 : cur = p
                Case "Q"c, "T"c
                    Dim c1 As PointD, p As PointD
                    If c = "Q"c Then
                        c1 = If(rel, New PointD(cur.x + v(0), cur.y + v(1)), New PointD(v(0), v(1)))
                        p = If(rel, New PointD(cur.x + v(2), cur.y + v(3)), New PointD(v(2), v(3)))
                    Else
                        Dim prevQuad = (Char.ToUpperInvariant(lastCmd) = "Q"c OrElse Char.ToUpperInvariant(lastCmd) = "T"c)
                        c1 = If(prevQuad, New PointD(2 * cur.x - lastCtrl.x, 2 * cur.y - lastCtrl.y), cur)
                        p = If(rel, New PointD(cur.x + v(0), cur.y + v(1)), New PointD(v(0), v(1)))
                    End If
                    ' Quadratic -> cubic.
                    Dim cc1 As New PointD(cur.x + 2.0 / 3.0 * (c1.x - cur.x), cur.y + 2.0 / 3.0 * (c1.y - cur.y))
                    Dim cc2 As New PointD(p.x + 2.0 / 3.0 * (c1.x - p.x), p.y + 2.0 / 3.0 * (c1.y - p.y))
                    For Each q In FlattenCubic(cur, cc1, cc2, p, tolUser)
                        flushPoint(q)
                    Next
                    lastCtrl = c1 : cur = p
                Case "A"c
                    Dim p = If(rel, New PointD(cur.x + v(5), cur.y + v(6)), New PointD(v(5), v(6)))
                    For Each q In FlattenArc(cur, v(0), v(1), v(2), v(3) <> 0, v(4) <> 0, p, tolUser)
                        flushPoint(q)
                    Next
                    cur = p
            End Select
            lastCmd = c
            If Not (c = "C"c OrElse c = "S"c OrElse c = "Q"c OrElse c = "T"c) Then lastCtrl = cur
            ' Keep the relative/absolute flavour for implicit repeats.
            If c <> "M"c Then cmd = If(rel, Char.ToLowerInvariant(c), c)
        End While
        Return result
    End Function

    Private Structure PathToken
        Public IsCommand As Boolean
        Public Command As Char
        Public Value As Double
    End Structure

    ''' <summary>Splits path data into commands and numbers; arc flags may be glued together ("00-2").</summary>
    Private Function TokenizePath(d As String) As List(Of PathToken)
        Dim tokens As New List(Of PathToken)
        Dim i As Integer = 0
        Dim pendingArcNumbers As Integer = 0      ' numbers remaining in the current arc group (7 per arc)
        While i < d.Length
            Dim ch = d(i)
            If Char.IsLetter(ch) AndAlso ch <> "e"c AndAlso ch <> "E"c Then
                tokens.Add(New PathToken With {.IsCommand = True, .Command = ch})
                pendingArcNumbers = If(ch = "A"c OrElse ch = "a"c, 7, 0)
                i += 1
            ElseIf Char.IsWhiteSpace(ch) OrElse ch = ","c Then
                i += 1
            Else
                ' Arc flags are single digits that may not be separated from the next number.
                If pendingArcNumbers = 4 OrElse pendingArcNumbers = 3 Then
                    If ch = "0"c OrElse ch = "1"c Then
                        tokens.Add(New PathToken With {.Value = If(ch = "1"c, 1, 0)})
                        pendingArcNumbers -= 1
                        i += 1
                        Continue While
                    End If
                End If
                Dim mt = NumberRx.Match(d, i)
                If Not mt.Success OrElse mt.Index <> i Then
                    i += 1       ' skip junk
                    Continue While
                End If
                tokens.Add(New PathToken With {.Value = Double.Parse(mt.Value, NumberStyles.Float, Ci)})
                i += mt.Length
                If pendingArcNumbers > 0 Then
                    pendingArcNumbers -= 1
                    If pendingArcNumbers = 0 Then pendingArcNumbers = 7   ' implicit repeated arcs
                End If
            End If
        End While
        Return tokens
    End Function

    ''' <summary>Adaptive De Casteljau flattening; returns the points after the start.</summary>
    Private Function FlattenCubic(p0 As PointD, p1 As PointD, p2 As PointD, p3 As PointD, tol As Double) As List(Of PointD)
        Dim pts As New List(Of PointD)
        FlattenCubicRec(p0, p1, p2, p3, tol, pts, 0)
        pts.Add(p3)
        Return pts
    End Function

    Private Sub FlattenCubicRec(p0 As PointD, p1 As PointD, p2 As PointD, p3 As PointD, tol As Double, pts As List(Of PointD), depth As Integer)
        ' Flat enough when both control points are within tol of the chord.
        Dim d1 = DistToSegment(p1, p0, p3), d2 = DistToSegment(p2, p0, p3)
        If (d1 <= tol AndAlso d2 <= tol) OrElse depth >= 16 Then Return
        Dim p01 = Mid(p0, p1), p12 = Mid(p1, p2), p23 = Mid(p2, p3)
        Dim p012 = Mid(p01, p12), p123 = Mid(p12, p23)
        Dim pm = Mid(p012, p123)
        FlattenCubicRec(p0, p01, p012, pm, tol, pts, depth + 1)
        pts.Add(pm)
        FlattenCubicRec(pm, p123, p23, p3, tol, pts, depth + 1)
    End Sub

    Private Function Mid(a As PointD, b As PointD) As PointD
        Return New PointD((a.x + b.x) / 2, (a.y + b.y) / 2)
    End Function

    Private Function DistToSegment(p As PointD, a As PointD, b As PointD) As Double
        Dim dx = b.x - a.x, dy = b.y - a.y
        Dim len2 = dx * dx + dy * dy
        If len2 < 0.000000000001 Then Return Math.Sqrt((p.x - a.x) ^ 2 + (p.y - a.y) ^ 2)
        Dim t = Math.Max(0, Math.Min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / len2))
        Return Math.Sqrt((p.x - (a.x + t * dx)) ^ 2 + (p.y - (a.y + t * dy)) ^ 2)
    End Function

    ''' <summary>SVG elliptical arc (endpoint form) -> points after the start (W3C implementation notes F.6.5).</summary>
    Private Function FlattenArc(p0 As PointD, rx As Double, ry As Double, rotDeg As Double, largeArc As Boolean, sweep As Boolean, p1 As PointD, tol As Double) As List(Of PointD)
        Dim pts As New List(Of PointD)
        If (p0.x = p1.x AndAlso p0.y = p1.y) Then Return pts
        rx = Math.Abs(rx) : ry = Math.Abs(ry)
        If rx = 0 OrElse ry = 0 Then
            pts.Add(p1)
            Return pts
        End If
        Dim phi = rotDeg * Math.PI / 180
        Dim cphi = Math.Cos(phi), sphi = Math.Sin(phi)
        Dim dx2 = (p0.x - p1.x) / 2, dy2 = (p0.y - p1.y) / 2
        Dim x1p = cphi * dx2 + sphi * dy2
        Dim y1p = -sphi * dx2 + cphi * dy2
        Dim lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry)
        If lambda > 1 Then
            rx *= Math.Sqrt(lambda) : ry *= Math.Sqrt(lambda)
        End If
        Dim num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p
        Dim den = rx * rx * y1p * y1p + ry * ry * x1p * x1p
        Dim coef = If(den = 0, 0, Math.Sqrt(Math.Max(0, num / den)))
        If largeArc = sweep Then coef = -coef
        Dim cxp = coef * rx * y1p / ry
        Dim cyp = -coef * ry * x1p / rx
        Dim cx = cphi * cxp - sphi * cyp + (p0.x + p1.x) / 2
        Dim cy = sphi * cxp + cphi * cyp + (p0.y + p1.y) / 2
        Dim theta1 = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx)
        Dim dtheta = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx) - theta1
        If sweep AndAlso dtheta < 0 Then dtheta += 2 * Math.PI
        If Not sweep AndAlso dtheta > 0 Then dtheta -= 2 * Math.PI
        Dim n = SegmentsForArc(Math.Max(rx, ry), dtheta, tol)
        For k = 1 To n
            Dim th = theta1 + dtheta * k / n
            Dim ex = rx * Math.Cos(th), ey = ry * Math.Sin(th)
            pts.Add(New PointD(cphi * ex - sphi * ey + cx, sphi * ex + cphi * ey + cy))
        Next
        pts(pts.Count - 1) = p1
        Return pts
    End Function

    ' =====================================================================
    '  attributes, styles, transforms, units
    ' =====================================================================

    Private Function Attr(el As XElement, name As String) As String
        Dim a = el.Attribute(name)
        Return If(a Is Nothing, Nothing, a.Value)
    End Function

    Private Function Num(el As XElement, name As String, def As Double) As Double
        Dim v = ParseLengthUser(Attr(el, name))
        Return If(Double.IsNaN(v), def, v)
    End Function

    Private Function ParseNumber(s As String) As Double
        If String.IsNullOrWhiteSpace(s) Then Return Double.NaN
        Dim mt = NumberRx.Match(s)
        If Not mt.Success Then Return Double.NaN
        Return Double.Parse(mt.Value, NumberStyles.Float, Ci)
    End Function

    ''' <summary>Length in user units (units other than px are converted at 96 px/in).</summary>
    Private Function ParseLengthUser(s As String) As Double
        If String.IsNullOrWhiteSpace(s) Then Return Double.NaN
        Dim v = ParseNumber(s)
        If Double.IsNaN(v) Then Return v
        Dim u = s.Trim().ToLowerInvariant()
        If u.EndsWith("mm") Then Return v * 96 / 25.4
        If u.EndsWith("cm") Then Return v * 96 / 2.54
        If u.EndsWith("in") Then Return v * 96
        If u.EndsWith("pt") Then Return v * 96 / 72
        If u.EndsWith("pc") Then Return v * 16
        Return v
    End Function

    ''' <summary>Root width/height in inches (NaN when absent or a percentage).</summary>
    Private Function ParseLength(s As String, def As Double) As Double
        If String.IsNullOrWhiteSpace(s) Then Return def
        Dim t = s.Trim().ToLowerInvariant()
        If t.EndsWith("%") Then Return def
        Dim v = ParseNumber(t)
        If Double.IsNaN(v) Then Return def
        If t.EndsWith("mm") Then Return v / 25.4
        If t.EndsWith("cm") Then Return v / 2.54
        If t.EndsWith("in") Then Return v
        If t.EndsWith("pt") Then Return v / 72
        If t.EndsWith("pc") Then Return v / 6
        Return v / 96          ' px (or unitless)
    End Function

    Private Function ParseViewBox(s As String) As Double()
        If String.IsNullOrWhiteSpace(s) Then Return Nothing
        Dim n = Numbers(s)
        If n.Count < 4 Then Return Nothing
        Return New Double() {n(0), n(1), n(2), n(3)}
    End Function

    Private Function Numbers(s As String) As List(Of Double)
        Dim list As New List(Of Double)
        If String.IsNullOrWhiteSpace(s) Then Return list
        For Each mt As Match In NumberRx.Matches(s)
            list.Add(Double.Parse(mt.Value, NumberStyles.Float, Ci))
        Next
        Return list
    End Function

    ''' <summary>Presentation attributes plus the style="" attribute (style wins).</summary>
    Private Function ParseStyle(el As XElement) As Dictionary(Of String, String)
        Dim d As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each key In New String() {"fill", "stroke", "stroke-width", "fill-rule", "display", "visibility"}
            Dim v = Attr(el, key)
            If v IsNot Nothing Then d(key) = v
        Next
        Dim style = Attr(el, "style")
        If Not String.IsNullOrWhiteSpace(style) Then
            For Each part In style.Split(";"c)
                Dim idx = part.IndexOf(":"c)
                If idx > 0 Then d(part.Substring(0, idx).Trim()) = part.Substring(idx + 1).Trim()
            Next
        End If
        Return d
    End Function

    Private ReadOnly TransformRx As New Regex("(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)", RegexOptions.Compiled Or RegexOptions.IgnoreCase)

    Private Function ParseTransform(s As String) As Affine
        Dim m = Affine.Identity
        If String.IsNullOrWhiteSpace(s) Then Return m
        For Each mt As Match In TransformRx.Matches(s)
            Dim fn = mt.Groups(1).Value.ToLowerInvariant()
            Dim a = Numbers(mt.Groups(2).Value)
            Dim t = Affine.Identity
            Select Case fn
                Case "matrix"
                    If a.Count >= 6 Then t = New Affine With {.A = a(0), .B = a(1), .C = a(2), .D = a(3), .E = a(4), .F = a(5)}
                Case "translate"
                    t = New Affine With {.A = 1, .D = 1, .E = If(a.Count > 0, a(0), 0), .F = If(a.Count > 1, a(1), 0)}
                Case "scale"
                    Dim sx = If(a.Count > 0, a(0), 1)
                    t = New Affine With {.A = sx, .D = If(a.Count > 1, a(1), sx)}
                Case "rotate"
                    Dim ang = If(a.Count > 0, a(0), 0) * Math.PI / 180
                    Dim r = New Affine With {.A = Math.Cos(ang), .B = Math.Sin(ang), .C = -Math.Sin(ang), .D = Math.Cos(ang)}
                    If a.Count >= 3 Then
                        Dim toC = New Affine With {.A = 1, .D = 1, .E = a(1), .F = a(2)}
                        Dim back = New Affine With {.A = 1, .D = 1, .E = -a(1), .F = -a(2)}
                        t = toC.Times(r).Times(back)
                    Else
                        t = r
                    End If
                Case "skewx"
                    t = New Affine With {.A = 1, .D = 1, .C = Math.Tan(If(a.Count > 0, a(0), 0) * Math.PI / 180)}
                Case "skewy"
                    t = New Affine With {.A = 1, .D = 1, .B = Math.Tan(If(a.Count > 0, a(0), 0) * Math.PI / 180)}
            End Select
            m = m.Times(t)
        Next
        Return m
    End Function
End Module
