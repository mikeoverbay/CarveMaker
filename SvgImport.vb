' ============================================================================
'  SvgImport.vb
'  Reads an SVG file into clean polygons (inches, Y up) ready for the carve
'  engine. Supported: <path> (all commands, absolute and relative, arcs),
'  <rect> (with rx/ry), <circle>, <ellipse>, <line>, <polyline>, <polygon>,
'  <g> / nested <svg> / <switch> with nested transforms, <use> of elements,
'  groups and <symbol>s, viewBox + width/height units + preserveAspectRatio,
'  presentation attributes, inline style, simple <style> sheets (tag, .class,
'  #id selectors), fill-rule (nonzero / evenodd) per element, and stroke-only
'  art converted to outlines (stroke width, caps and joins honoured).
'  Not supported: <text> (convert to paths in your drawing program), clip
'  paths, masks, gradients/opacity (every painted shape is carved).
'
'  Pipeline per element: flatten in user units -> fill: transform the points,
'  union with the element's fill rule; stroke: offset in user units, transform
'  the outline -> all shapes unioned, Y flipped, moved to the origin.
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
    ''' <summary>Number of elements that contributed geometry.</summary>
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

    Public Shared Function Translation(x As Double, y As Double) As Affine
        Return New Affine With {.A = 1, .D = 1, .E = x, .F = y}
    End Function

    Public Shared Function Scaling(sx As Double, sy As Double) As Affine
        Return New Affine With {.A = sx, .D = sy}
    End Function

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

    ''' <summary>Largest stretch factor of the linear part (for tolerances).</summary>
    Public ReadOnly Property MaxScale As Double
        Get
            Dim s = A * A + B * B + C * C + D * D
            Dim det = A * D - B * C
            Dim disc = Math.Sqrt(Math.Max(0, s * s - 4 * det * det))
            Return Math.Sqrt(Math.Max(0.000000000001, (s + disc) / 2))
        End Get
    End Property
End Structure

Public Module SvgImport

    Private ReadOnly Ci As CultureInfo = CultureInfo.InvariantCulture
    Private ReadOnly NumberRx As New Regex("[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", RegexOptions.Compiled)
    ''' <summary>Coordinates beyond this (inches) are treated as corrupt (Clipper scales by 1e5 into Int64).</summary>
    Private Const MaxCoordinateIn As Double = 1000000.0

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

        Dim ctx As New ParseContext With {
            .Tolerance = Math.Max(0.0001, curveToleranceIn),
            .Result = result,
            .Defs = New Dictionary(Of String, XElement),
            .UseStack = New HashSet(Of XElement),
            .Sheet = New Stylesheet()}
        For Each el In root.Descendants()
            Dim id = Attr(el, "id")
            If Not String.IsNullOrEmpty(id) AndAlso Not ctx.Defs.ContainsKey(id) Then ctx.Defs(id) = el
            If el.Name.LocalName = "style" Then ctx.Sheet.AddCss(el.Value, result.Warnings)
        Next

        ' ---- document units: user unit -> inches ----------------------------
        Dim vb As Double() = ParseViewBox(Attr(root, "viewBox"))
        Dim widthIn As Double = ParseLength(Attr(root, "width"), Double.NaN)
        Dim heightIn As Double = ParseLength(Attr(root, "height"), Double.NaN)
        Dim toInches As Affine
        If vb IsNot Nothing AndAlso vb(2) > 0 AndAlso vb(3) > 0 Then
            Dim vw As Double = widthIn, vh As Double = heightIn
            If Double.IsNaN(vw) AndAlso Double.IsNaN(vh) Then
                vw = vb(2) / 96.0 : vh = vb(3) / 96.0                 ' px
            ElseIf Double.IsNaN(vw) Then
                vw = vb(2) * vh / vb(3)
            ElseIf Double.IsNaN(vh) Then
                vh = vb(3) * vw / vb(2)
            End If
            toInches = ViewportTransform(vb, vw, vh, Attr(root, "preserveAspectRatio"))
        Else
            toInches = Affine.Scaling(1 / 96.0, 1 / 96.0)            ' no viewBox: user units are px
        End If

        ' ---- walk the tree ----------------------------------------------------
        Dim shapes As New PathsD()
        WalkChildren(root, toInches, StyleState.Initial.With_(root, ctx), ctx, shapes, 0)   ' root presentation attributes inherit too

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
        merged = Clipper.Union(flipped, Nothing, FillRule.NonZero, GlyphOutline.ClipperPrecision)
        Dim b = Clipper.GetBounds(merged)
        result.Paths = Clipper.TranslatePaths(merged, -b.left, -b.top)
        result.Width = b.right - b.left
        result.Height = b.bottom - b.top
        Return result
    End Function

    ''' <summary>viewBox -> viewport (vw x vh, in target units) with preserveAspectRatio.</summary>
    Private Function ViewportTransform(vb As Double(), vw As Double, vh As Double, par As String) As Affine
        Dim sx = vw / vb(2), sy = vh / vb(3)
        Dim align As String = "xmidymid", mode As String = "meet"
        If Not String.IsNullOrWhiteSpace(par) Then
            Dim parts = par.Trim().ToLowerInvariant().Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
            If parts.Length > 0 Then align = parts(0)
            If parts.Length > 1 Then mode = parts(1)
        End If
        Dim tx As Double = 0, ty As Double = 0
        If align <> "none" Then
            Dim s = If(mode = "slice", Math.Max(sx, sy), Math.Min(sx, sy))
            sx = s : sy = s
            Dim fx As Double = 0.5, fy As Double = 0.5
            If align.StartsWith("xmin") Then fx = 0 Else If align.StartsWith("xmax") Then fx = 1
            If align.EndsWith("ymin") Then fy = 0 Else If align.EndsWith("ymax") Then fy = 1
            tx = (vw - vb(2) * s) * fx
            ty = (vh - vb(3) * s) * fy
        End If
        Return Affine.Translation(tx, ty).Times(Affine.Scaling(sx, sy)).Times(Affine.Translation(-vb(0), -vb(1)))
    End Function

    ' =====================================================================
    '  tree walking
    ' =====================================================================

    Private Class ParseContext
        Public Tolerance As Double
        Public Result As SvgShapeSet
        Public Defs As Dictionary(Of String, XElement)
        Public UseStack As HashSet(Of XElement)
        Public Sheet As Stylesheet
        Public TextWarned As Boolean
    End Class

    ''' <summary>Minimal CSS: rules keyed by simple selectors (tag, .class, #id, tag.class, *).</summary>
    Private Class Stylesheet
        Private ReadOnly _rules As New List(Of KeyValuePair(Of String, Dictionary(Of String, String)))
        Private _warnedComplex As Boolean

        Public ReadOnly Property IsEmpty As Boolean
            Get
                Return _rules.Count = 0
            End Get
        End Property

        Public Sub AddCss(css As String, warnings As List(Of String))
            If String.IsNullOrWhiteSpace(css) Then Return
            css = Regex.Replace(css, "/\*.*?\*/", "", RegexOptions.Singleline)
            css = Regex.Replace(css, "@[^{]*\{(?:[^{}]*\{[^{}]*\})*[^{}]*\}", "")   ' drop @media / @font-face blocks
            For Each m As Match In Regex.Matches(css, "([^{}]+)\{([^{}]*)\}")
                Dim decls As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                For Each part In m.Groups(2).Value.Split(";"c)
                    Dim idx = part.IndexOf(":"c)
                    If idx > 0 Then decls(part.Substring(0, idx).Trim()) = part.Substring(idx + 1).Trim()
                Next
                If decls.Count = 0 Then Continue For
                For Each sel In m.Groups(1).Value.Split(","c)
                    Dim s = sel.Trim()
                    If s.Length = 0 Then Continue For
                    If s.IndexOfAny(New Char() {" "c, ">"c, "+"c, "~"c, ":"c, "["c}) >= 0 Then
                        If Not _warnedComplex Then
                            warnings.Add("Some CSS selectors in <style> are not supported (only tag, .class and #id); those rules are ignored.")
                            _warnedComplex = True
                        End If
                        Continue For
                    End If
                    _rules.Add(New KeyValuePair(Of String, Dictionary(Of String, String))(s, decls))
                Next
            Next
        End Sub

        ''' <summary>Declarations that apply to an element, in cascade order (tag, class, id; later rules win).</summary>
        Public Sub ApplyTo(el As XElement, into As Dictionary(Of String, String))
            If _rules.Count = 0 Then Return
            Dim tag = el.Name.LocalName
            Dim id = Attr(el, "id")
            Dim classes = If(Attr(el, "class"), "").Split(New Char() {" "c, ControlChars.Tab, ControlChars.Lf, ControlChars.Cr}, StringSplitOptions.RemoveEmptyEntries)
            For pass = 0 To 2
                For Each rule In _rules
                    Dim sel = rule.Key
                    Dim matches As Boolean = False
                    Select Case pass
                        Case 0 : matches = (sel = "*" OrElse sel = tag)
                        Case 1
                            If sel.Contains("."c) Then
                                Dim dot = sel.IndexOf("."c)
                                Dim selTag = sel.Substring(0, dot)
                                Dim selCls = sel.Substring(dot + 1)
                                matches = (selTag.Length = 0 OrElse selTag = tag OrElse selTag = "*") AndAlso classes.Contains(selCls)
                            End If
                        Case 2 : matches = sel.StartsWith("#") AndAlso id IsNot Nothing AndAlso sel.Substring(1) = id
                    End Select
                    If matches Then
                        For Each kv In rule.Value
                            into(kv.Key) = kv.Value
                        Next
                    End If
                Next
            Next
        End Sub
    End Class

    ''' <summary>Inherited presentation state.</summary>
    Private Structure StyleState
        Public Fill As String          ' Nothing = default (black)
        Public Stroke As String        ' Nothing = none
        Public StrokeWidth As Double   ' user units; NaN = unset (1)
        Public FillRuleEvenOdd As Boolean
        Public DisplayNone As Boolean  ' sticky: subtree not rendered
        Public VisibilityHidden As Boolean   ' inherited but children may set visible again
        Public LineCap As String       ' butt (default), round, square
        Public LineJoin As String      ' miter (default), round, bevel
        Public MiterLimit As Double    ' NaN = 4

        ''' <summary>State at the root: nothing set. Needed because a structure's doubles default to 0, not NaN.</summary>
        Public Shared ReadOnly Property Initial As StyleState
            Get
                Return New StyleState With {.StrokeWidth = Double.NaN, .MiterLimit = Double.NaN}
            End Get
        End Property

        Public Function With_(el As XElement, ctx As ParseContext) As StyleState
            Dim s = Me
            Dim st = ParseStyle(el, ctx.Sheet)
            Dim v As String = Nothing
            If st.TryGetValue("fill", v) AndAlso Not IsInherit(v) Then s.Fill = v
            If st.TryGetValue("stroke", v) AndAlso Not IsInherit(v) Then s.Stroke = v
            If st.TryGetValue("stroke-width", v) AndAlso Not IsInherit(v) Then
                Dim w = ParseLengthUser(v)
                If Not Double.IsNaN(w) Then s.StrokeWidth = w
            End If
            If st.TryGetValue("fill-rule", v) Then
                Dim r = v.Trim().ToLowerInvariant()
                If r = "evenodd" Then s.FillRuleEvenOdd = True
                If r = "nonzero" Then s.FillRuleEvenOdd = False
            End If
            If st.TryGetValue("stroke-linecap", v) AndAlso Not IsInherit(v) Then s.LineCap = v.Trim().ToLowerInvariant()
            If st.TryGetValue("stroke-linejoin", v) AndAlso Not IsInherit(v) Then s.LineJoin = v.Trim().ToLowerInvariant()
            If st.TryGetValue("stroke-miterlimit", v) AndAlso Not IsInherit(v) Then
                Dim ml = ParseNumber(v)
                If Not Double.IsNaN(ml) AndAlso ml >= 1 Then s.MiterLimit = ml
            End If
            If st.TryGetValue("display", v) AndAlso v.Trim().ToLowerInvariant() = "none" Then s.DisplayNone = True
            If st.TryGetValue("visibility", v) Then
                Dim vis = v.Trim().ToLowerInvariant()
                If vis = "hidden" OrElse vis = "collapse" Then s.VisibilityHidden = True
                If vis = "visible" Then s.VisibilityHidden = False
            End If
            Return s
        End Function

        Private Shared Function IsInherit(v As String) As Boolean
            Return v IsNot Nothing AndAlso v.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase)
        End Function
    End Structure

    Private Sub WalkChildren(parent As XElement, m As Affine, style As StyleState, ctx As ParseContext, shapes As PathsD, depth As Integer)
        For Each child In parent.Elements()
            WalkElement(child, m, style, ctx, shapes, depth + 1)
        Next
    End Sub

    Private Sub WalkElement(el As XElement, m As Affine, inherited As StyleState, ctx As ParseContext, shapes As PathsD, depth As Integer)
        If depth > 64 Then Return
        Dim name = el.Name.LocalName
        Dim style = inherited.With_(el, ctx)
        If style.DisplayNone Then Return
        Dim local = m.Times(ParseTransform(Attr(el, "transform")))

        Select Case name
            Case "g", "a"
                WalkChildren(el, local, style, ctx, shapes, depth)
            Case "svg"
                ' Nested viewport: x/y offset plus viewBox scaling when width/height are given.
                Dim inner = local.Times(Affine.Translation(Num(el, "x", 0), Num(el, "y", 0)))
                Dim vb = ParseViewBox(Attr(el, "viewBox"))
                Dim w = Num(el, "width", Double.NaN), h = Num(el, "height", Double.NaN)
                If vb IsNot Nothing AndAlso vb(2) > 0 AndAlso vb(3) > 0 AndAlso Not Double.IsNaN(w) AndAlso Not Double.IsNaN(h) AndAlso w > 0 AndAlso h > 0 Then
                    inner = inner.Times(ViewportTransform(vb, w, h, Attr(el, "preserveAspectRatio")))
                End If
                WalkChildren(el, inner, style, ctx, shapes, depth)
            Case "switch"
                For Each child In el.Elements()
                    If SwitchChildApplies(child) Then
                        WalkElement(child, local, style, ctx, shapes, depth + 1)
                        Exit For
                    End If
                Next
            Case "defs", "symbol", "clipPath", "mask", "marker", "pattern", "metadata", "title", "desc", "style", "linearGradient", "radialGradient", "filter"
                ' Not rendered directly.
            Case "use"
                WalkUse(el, local, style, ctx, shapes, depth)
            Case "text"
                If Not ctx.TextWarned Then
                    ctx.Result.Warnings.Add("Text elements are not imported; convert text to paths (outlines) in your drawing program.")
                    ctx.TextWarned = True
                End If
            Case "image"
                ctx.Result.Warnings.Add("Embedded images are ignored.")
            Case "path", "rect", "circle", "ellipse", "line", "polyline", "polygon"
                If style.VisibilityHidden Then Return
                Dim tolUser As Double = ctx.Tolerance / local.MaxScale
                Dim subpaths As List(Of SubPath) = ElementGeometry(el, name, tolUser, ctx)
                If subpaths Is Nothing OrElse subpaths.Count = 0 Then Return
                If AddShape(subpaths, style, local, tolUser, ctx, shapes) Then ctx.Result.ElementCount += 1
        End Select
    End Sub

    ''' <summary>First child of a &lt;switch&gt; whose conditional attributes pass.</summary>
    Private Function SwitchChildApplies(child As XElement) As Boolean
        Dim ext = Attr(child, "requiredExtensions")
        If ext IsNot Nothing AndAlso ext.Trim().Length > 0 Then Return False
        Dim feat = Attr(child, "requiredFeatures")
        If feat IsNot Nothing AndAlso feat.Trim().Length = 0 Then Return False
        Dim lang = Attr(child, "systemLanguage")
        If lang IsNot Nothing Then
            Dim want As New List(Of String) From {"en"}
            Try
                want.Add(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant())
            Catch
            End Try
            Dim ok = False
            For Each l In lang.Split(","c)
                Dim code = l.Trim().ToLowerInvariant()
                If code.Length = 0 Then Continue For
                Dim prefix = code.Split("-"c)(0)
                If want.Contains(prefix) Then ok = True
            Next
            If Not ok Then Return False
        End If
        Return True
    End Function

    Private Sub WalkUse(el As XElement, local As Affine, style As StyleState, ctx As ParseContext, shapes As PathsD, depth As Integer)
        Dim href = Attr(el, "href")
        If String.IsNullOrEmpty(href) Then href = el.Attributes().Where(Function(a) a.Name.LocalName = "href").Select(Function(a) a.Value).FirstOrDefault()
        If String.IsNullOrEmpty(href) OrElse Not href.StartsWith("#") Then
            ctx.Result.Warnings.Add("A <use> element without a local #id reference was ignored.")
            Return
        End If
        Dim target As XElement = Nothing
        If Not ctx.Defs.TryGetValue(href.Substring(1), target) Then
            ctx.Result.Warnings.Add("<use " & href & "> points at an element that does not exist; ignored.")
            Return
        End If
        ' Cycle guard: the target may not be an ancestor of the use, nor already instantiating.
        If ctx.UseStack.Contains(target) OrElse el.Ancestors().Contains(target) OrElse target Is el Then
            ctx.Result.Warnings.Add("<use " & href & "> references itself (directly or through an ancestor); ignored.")
            Return
        End If
        Dim ux = Num(el, "x", 0), uy = Num(el, "y", 0)
        Dim placed = local.Times(Affine.Translation(ux, uy))
        ctx.UseStack.Add(target)
        Try
            Dim tname = target.Name.LocalName
            If tname = "symbol" OrElse tname = "svg" Then
                Dim inner = placed
                Dim vb = ParseViewBox(Attr(target, "viewBox"))
                Dim w = Num(el, "width", Double.NaN), h = Num(el, "height", Double.NaN)
                If Double.IsNaN(w) Then w = Num(target, "width", Double.NaN)
                If Double.IsNaN(h) Then h = Num(target, "height", Double.NaN)
                If vb IsNot Nothing AndAlso vb(2) > 0 AndAlso vb(3) > 0 AndAlso Not Double.IsNaN(w) AndAlso Not Double.IsNaN(h) AndAlso w > 0 AndAlso h > 0 Then
                    inner = inner.Times(ViewportTransform(vb, w, h, Attr(target, "preserveAspectRatio")))
                End If
                Dim symStyle = style.With_(target, ctx)
                inner = inner.Times(ParseTransform(Attr(target, "transform")))
                WalkChildren(target, inner, symStyle, ctx, shapes, depth + 1)
            Else
                WalkElement(target, placed, style, ctx, shapes, depth + 1)
            End If
        Finally
            ctx.UseStack.Remove(target)
        End Try
    End Sub

    ''' <summary>A flattened subpath in user units (document orientation, Y down).</summary>
    Private Class SubPath
        Public Points As New PathD()
        Public Closed As Boolean
    End Class

    ''' <summary>Turns fill and/or stroke of an element into normalized polygons (inches). Returns True when geometry was added.</summary>
    Private Function AddShape(subpaths As List(Of SubPath), style As StyleState, m As Affine, tolUser As Double, ctx As ParseContext, shapes As PathsD) As Boolean
        Dim fill = If(style.Fill, "black").Trim().ToLowerInvariant()
        Dim stroke = If(style.Stroke, "none").Trim().ToLowerInvariant()
        Dim hasFill = fill <> "none" AndAlso fill <> "transparent"
        Dim hasStroke = stroke <> "none" AndAlso stroke <> "transparent"
        Dim added As Boolean = False

        If hasFill Then
            Dim raw As New PathsD()
            For Each sp In subpaths
                If sp.Points.Count >= 3 Then raw.Add(Transform(sp.Points, m))     ' fill closes open subpaths implicitly
            Next
            If raw.Count > 0 Then
                If Not CoordinatesSane(raw, ctx) Then Return False
                Dim rule = If(style.FillRuleEvenOdd, FillRule.EvenOdd, FillRule.NonZero)
                Dim norm = Clipper.Union(raw, Nothing, rule, GlyphOutline.ClipperPrecision)
                If norm.Count > 0 Then
                    shapes.AddRange(norm)
                    added = True
                End If
            End If
        End If

        If hasStroke Then
            Dim w = If(Double.IsNaN(style.StrokeWidth), 1.0, style.StrokeWidth)     ' user units
            If w > 0 Then
                Dim cap = If(style.LineCap, "butt")
                Dim endType As EndType = If(cap = "round", EndType.Round, If(cap = "square", EndType.Square, EndType.Butt))
                Dim joinName = If(style.LineJoin, "miter")
                Dim joinType As JoinType = If(joinName = "round", JoinType.Round, If(joinName = "bevel", JoinType.Bevel, JoinType.Miter))
                Dim miter As Double = If(Double.IsNaN(style.MiterLimit), 4.0, style.MiterLimit)
                Dim closedPaths As New PathsD(), openPaths As New PathsD()
                For Each sp In subpaths
                    If sp.Points.Count < 1 Then Continue For
                    If Extent(sp.Points) < 0.000000001 Then
                        ' Zero-length: only round/square caps draw a dot (SVG spec).
                        If cap = "butt" Then Continue For
                        openPaths.Add(New PathD() From {sp.Points(0)})
                    ElseIf sp.Closed AndAlso sp.Points.Count >= 3 Then
                        closedPaths.Add(sp.Points)
                    Else
                        openPaths.Add(sp.Points)
                    End If
                Next
                ' Offset in user units (so non-uniform transforms stretch the stroke correctly), then transform.
                Dim arcTol As Double = Math.Max(tolUser, 0.00001)
                Dim outlines As New PathsD()
                If closedPaths.Count > 0 Then
                    outlines.AddRange(Clipper.InflatePaths(closedPaths, w / 2, joinType, EndType.Joined, miter, GlyphOutline.ClipperPrecision, arcTol))
                End If
                If openPaths.Count > 0 Then
                    outlines.AddRange(Clipper.InflatePaths(openPaths, w / 2, joinType, endType, miter, GlyphOutline.ClipperPrecision, arcTol))
                End If
                If outlines.Count > 0 Then
                    Dim placed As New PathsD(outlines.Count)
                    For Each p In outlines
                        placed.Add(Transform(p, m))
                    Next
                    If Not CoordinatesSane(placed, ctx) Then Return added
                    Dim norm = Clipper.Union(placed, Nothing, FillRule.NonZero, GlyphOutline.ClipperPrecision)
                    If norm.Count > 0 Then
                        shapes.AddRange(norm)
                        added = True
                    End If
                End If
            End If
        End If
        Return added
    End Function

    Private Function Transform(p As PathD, m As Affine) As PathD
        Dim q As New PathD(p.Count)
        For Each pt In p
            q.Add(m.Apply(pt.x, pt.y))
        Next
        Return q
    End Function

    Private Function Extent(p As PathD) As Double
        If p.Count = 0 Then Return 0
        Dim minX = Double.MaxValue, minY = Double.MaxValue, maxX = Double.MinValue, maxY = Double.MinValue
        For Each pt In p
            minX = Math.Min(minX, pt.x) : maxX = Math.Max(maxX, pt.x)
            minY = Math.Min(minY, pt.y) : maxY = Math.Max(maxY, pt.y)
        Next
        Return Math.Max(maxX - minX, maxY - minY)
    End Function

    ''' <summary>Rejects geometry with non-finite or absurd coordinates (would overflow Clipper's integer scaling).</summary>
    Private Function CoordinatesSane(paths As PathsD, ctx As ParseContext) As Boolean
        For Each p In paths
            For Each pt In p
                If Double.IsNaN(pt.x) OrElse Double.IsNaN(pt.y) OrElse Double.IsInfinity(pt.x) OrElse Double.IsInfinity(pt.y) OrElse
                   Math.Abs(pt.x) > MaxCoordinateIn OrElse Math.Abs(pt.y) > MaxCoordinateIn Then
                    ctx.Result.Warnings.Add("An element with coordinates beyond " & MaxCoordinateIn.ToString("0", Ci) & " inches (or non-numeric) was ignored.")
                    Return False
                End If
            Next
        Next
        Return True
    End Function

    ' =====================================================================
    '  element geometry (flattened subpaths in USER units)
    ' =====================================================================

    Private Function ElementGeometry(el As XElement, name As String, tolUser As Double, ctx As ParseContext) As List(Of SubPath)
        Select Case name
            Case "path"
                Return ParsePathData(Attr(el, "d"), tolUser, ctx)
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
                Return ParsePathData(d, tolUser, ctx)
            Case "circle"
                Dim cx = Num(el, "cx", 0), cy = Num(el, "cy", 0), r = Num(el, "r", 0)
                If r <= 0 Then Return Nothing
                Return EllipseSubpath(cx, cy, r, r, tolUser)
            Case "ellipse"
                Dim cx = Num(el, "cx", 0), cy = Num(el, "cy", 0), rx = Num(el, "rx", 0), ry = Num(el, "ry", 0)
                If rx <= 0 OrElse ry <= 0 Then Return Nothing
                Return EllipseSubpath(cx, cy, rx, ry, tolUser)
            Case "line"
                Dim sp As New SubPath()
                sp.Points.Add(New PointD(Num(el, "x1", 0), Num(el, "y1", 0)))
                sp.Points.Add(New PointD(Num(el, "x2", 0), Num(el, "y2", 0)))
                Return New List(Of SubPath) From {sp}
            Case "polyline", "polygon"
                Dim nums = Numbers(Attr(el, "points"))
                Dim sp As New SubPath With {.Closed = (name = "polygon")}
                For i = 0 To nums.Count - 2 Step 2
                    sp.Points.Add(New PointD(nums(i), nums(i + 1)))
                Next
                Return If(sp.Points.Count >= 2, New List(Of SubPath) From {sp}, Nothing)
        End Select
        Return Nothing
    End Function

    Private Function EllipseSubpath(cx As Double, cy As Double, rx As Double, ry As Double, tolUser As Double) As List(Of SubPath)
        Dim n As Integer = SegmentsForArc(Math.Max(rx, ry), 2 * Math.PI, tolUser)
        Dim sp As New SubPath With {.Closed = True}
        For i = 0 To n - 1
            Dim a = 2 * Math.PI * i / n
            sp.Points.Add(New PointD(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)))
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

    Private Function ParsePathData(d As String, tolUser As Double, ctx As ParseContext) As List(Of SubPath)
        Dim result As New List(Of SubPath)
        If String.IsNullOrWhiteSpace(d) Then Return result

        Dim tokens = TokenizePath(d, ctx)
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
                                 sp.Points.Add(start)
                             End If
                             sp.Points.Add(p)
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
            ElseIf cmd = "Z"c OrElse cmd = "z"c Then
                ctx.Result.Warnings.Add("Numbers after Z are not allowed in path data; rest of the path skipped.")
                Return result
            End If
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
    Private Function TokenizePath(d As String, ctx As ParseContext) As List(Of PathToken)
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
                    i += 1
                    Continue While
                End If
                Dim value As Double = Double.Parse(mt.Value, NumberStyles.Float, Ci)
                If Double.IsInfinity(value) OrElse Double.IsNaN(value) Then
                    ctx.Result.Warnings.Add("Path data contains a number that is out of range; rest of the path skipped.")
                    Return tokens.Take(Math.Max(0, LastCommandIndex(tokens))).ToList()
                End If
                tokens.Add(New PathToken With {.Value = value})
                i += mt.Length
                If pendingArcNumbers > 0 Then
                    pendingArcNumbers -= 1
                    If pendingArcNumbers = 0 Then pendingArcNumbers = 7
                End If
            End If
        End While
        Return tokens
    End Function

    Private Function LastCommandIndex(tokens As List(Of PathToken)) As Integer
        For i = tokens.Count - 1 To 0 Step -1
            If tokens(i).IsCommand Then Return i
        Next
        Return 0
    End Function

    ''' <summary>Adaptive De Casteljau flattening; returns the points after the start.</summary>
    Private Function FlattenCubic(p0 As PointD, p1 As PointD, p2 As PointD, p3 As PointD, tol As Double) As List(Of PointD)
        Dim pts As New List(Of PointD)
        FlattenCubicRec(p0, p1, p2, p3, tol, pts, 0)
        pts.Add(p3)
        Return pts
    End Function

    Private Sub FlattenCubicRec(p0 As PointD, p1 As PointD, p2 As PointD, p3 As PointD, tol As Double, pts As List(Of PointD), depth As Integer)
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

    ''' <summary>Presentation attributes, then stylesheet rules, then the style="" attribute (last wins).</summary>
    Private Function ParseStyle(el As XElement, sheet As Stylesheet) As Dictionary(Of String, String)
        Dim d As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each key In New String() {"fill", "stroke", "stroke-width", "fill-rule", "display", "visibility", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit"}
            Dim v = Attr(el, key)
            If v IsNot Nothing Then d(key) = v
        Next
        sheet.ApplyTo(el, d)
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
                    t = Affine.Translation(If(a.Count > 0, a(0), 0), If(a.Count > 1, a(1), 0))
                Case "scale"
                    Dim sx = If(a.Count > 0, a(0), 1)
                    t = Affine.Scaling(sx, If(a.Count > 1, a(1), sx))
                Case "rotate"
                    Dim ang = If(a.Count > 0, a(0), 0) * Math.PI / 180
                    Dim r = New Affine With {.A = Math.Cos(ang), .B = Math.Sin(ang), .C = -Math.Sin(ang), .D = Math.Cos(ang)}
                    If a.Count >= 3 Then
                        t = Affine.Translation(a(1), a(2)).Times(r).Times(Affine.Translation(-a(1), -a(2)))
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
