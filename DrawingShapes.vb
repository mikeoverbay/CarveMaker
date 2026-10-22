' ============================================================================
'  DrawingShapes.vb
'  Natural outlines of the shapes and text drawn in the view: rectangles
'  (optionally rounded), ellipses, regular polygons, stars, free-form areas
'  and text. Every outline comes back normalized like an imported SVG:
'  outers positive, holes negative, inches, Y up, lower-left at the origin.
'  Also writes an outline as a small SVG (for project files read by older
'  versions, which only know imported drawings).
' ============================================================================

Imports System.Globalization
Imports System.Text
Imports Clipper2Lib

Public Module DrawingShapes

    ''' <summary>Builds the natural outline of a drawn object.</summary>
    Public Function Build(o As DesignObject, curveTolerance As Double) As SvgShapeSet
        Dim tol = Math.Max(0.0001, curveTolerance)
        Dim warnings As New List(Of String)
        Dim raw As PathsD
        Dim firstLineY As Double = Double.NaN
        Select Case o.Kind
            Case DrawingKind.Rectangle : raw = RoundedRectangle(Math.Max(0.01, o.Width), Math.Max(0.01, o.Height), o.CornerRadius, tol)
            Case DrawingKind.Ellipse : raw = Ellipse(Math.Max(0.01, o.Width), Math.Max(0.01, o.Height), tol)
            Case DrawingKind.Polygon : raw = RegularPolygon(o.Sides)
            Case DrawingKind.Star : raw = Star(o.StarPoints, o.StarInnerPercent)
            Case DrawingKind.Area : raw = Area(o.AreaPoints)
            Case DrawingKind.Text : raw = TextOutline(o, tol, warnings, firstLineY)
            Case Else : raw = New PathsD()
        End Select
        Dim offsetY As Double
        Dim result = Normalize(raw, warnings, offsetY)
        If Not Double.IsNaN(firstLineY) AndAlso result.Height > 0 Then result.FirstLineY = firstLineY + offsetY
        Return result
    End Function

    ''' <summary>Width / height of a kind's outline with its default settings (for Shift-constrained drawing).</summary>
    Public Function DefaultAspect(kind As DrawingKind) As Double
        Select Case kind
            Case DrawingKind.Polygon, DrawingKind.Star
                Dim unused As Double
                Dim s = Normalize(If(kind = DrawingKind.Polygon, RegularPolygon(New DesignObject().Sides),
                                     Star(New DesignObject().StarPoints, New DesignObject().StarInnerPercent)), New List(Of String), unused)
                If s.Width > 0 AndAlso s.Height > 0 Then Return s.Width / s.Height
        End Select
        Return 1.0
    End Function

    ''' <summary>Segments for an arc of the given radius and sweep so the chord error stays within tol.</summary>
    Private Function ArcSegments(radius As Double, tol As Double, sweep As Double) As Integer
        If radius <= tol Then Return 1
        Dim stepAngle = 2 * Math.Acos(Math.Max(-1.0, Math.Min(1.0, 1 - tol / radius)))
        If stepAngle <= 0 Then Return 1000
        Return Math.Max(1, Math.Min(1000, CInt(Math.Ceiling(sweep / stepAngle))))
    End Function

    Private Function RoundedRectangle(w As Double, h As Double, radius As Double, tol As Double) As PathsD
        Dim r = Math.Max(0.0, Math.Min(radius, Math.Min(w, h) / 2))
        Dim p As New PathD()
        If r < 0.0001 Then
            p.Add(New PointD(0, 0)) : p.Add(New PointD(w, 0)) : p.Add(New PointD(w, h)) : p.Add(New PointD(0, h))
        Else
            Dim n = ArcSegments(r, tol, Math.PI / 2)
            ' Corner centres counter-clockwise from the lower-right, each arc a quarter turn.
            Dim centres = {New PointD(w - r, r), New PointD(w - r, h - r), New PointD(r, h - r), New PointD(r, r)}
            For k = 0 To 3
                Dim a0 = -Math.PI / 2 + k * Math.PI / 2
                For i = 0 To n
                    Dim a = a0 + i * (Math.PI / 2) / n
                    p.Add(New PointD(centres(k).x + r * Math.Cos(a), centres(k).y + r * Math.Sin(a)))
                Next
            Next
        End If
        Return New PathsD From {p}
    End Function

    Private Function Ellipse(w As Double, h As Double, tol As Double) As PathsD
        Dim rx = w / 2, ry = h / 2
        ' A multiple of 4 puts vertices on the axis ends, so the outline's box is the ellipse's box.
        Dim n = Math.Max(12, ArcSegments(Math.Max(rx, ry), tol, 2 * Math.PI))
        n = ((n + 3) \ 4) * 4
        Dim p As New PathD(n)
        For i = 0 To n - 1
            Dim a = 2 * Math.PI * i / n
            p.Add(New PointD(rx + rx * Math.Cos(a), ry + ry * Math.Sin(a)))
        Next
        Return New PathsD From {p}
    End Function

    ''' <summary>Regular polygon with a flat bottom edge (unit circumradius; scaled into the box later).</summary>
    Private Function RegularPolygon(sides As Integer) As PathsD
        Dim n = Math.Max(3, Math.Min(64, sides))
        Dim p As New PathD(n)
        For i = 0 To n - 1
            Dim a = -Math.PI / 2 + Math.PI / n + 2 * Math.PI * i / n
            p.Add(New PointD(Math.Cos(a), Math.Sin(a)))
        Next
        Return New PathsD From {p}
    End Function

    ''' <summary>Star with one point straight up (unit outer radius).</summary>
    Private Function Star(points As Integer, innerPercent As Double) As PathsD
        Dim n = Math.Max(3, Math.Min(64, points))
        Dim inner = Math.Max(10.0, Math.Min(95.0, innerPercent)) / 100.0
        Dim p As New PathD(2 * n)
        For i = 0 To 2 * n - 1
            Dim a = Math.PI / 2 + Math.PI * i / n
            Dim r = If(i Mod 2 = 0, 1.0, inner)
            p.Add(New PointD(r * Math.Cos(a), r * Math.Sin(a)))
        Next
        Return New PathsD From {p}
    End Function

    Private Function Area(coords As List(Of Double)) As PathsD
        Dim result As New PathsD()
        If coords Is Nothing OrElse coords.Count < 6 Then Return result
        Dim p As New PathD(coords.Count \ 2)
        For i = 0 To coords.Count - 2 Step 2
            p.Add(New PointD(coords(i), coords(i + 1)))
        Next
        result.Add(p)
        Return result
    End Function

    ''' <summary>
    ''' The text's glyph outlines: capital letters LetterHeight tall, lines aligned to each other.
    ''' firstLineY gets the top of the first line's layout cell (font metrics only: independent of the
    ''' letters typed and of the line spacing).
    ''' </summary>
    Private Function TextOutline(o As DesignObject, tol As Double, warnings As List(Of String), ByRef firstLineY As Double) As PathsD
        Dim text = If(o.Text, "").Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
        If text.Trim().Length = 0 Then Return New PathsD()
        Dim font As New FontChoice(If(String.IsNullOrWhiteSpace(o.FontName), "Arial", o.FontName), o.Bold, o.Italic)
        ' A huge blank centred on the origin: the layout code then only aligns the lines
        ' with each other, and nothing "runs off the blank".
        Dim s As New CarveSettings With {
            .BlankOriginX = -5000, .BlankOriginY = -5000, .BlankWidthIn = 10000, .BlankHeightIn = 10000,
            .MarginIn = 0, .TextOffsetX = 0, .TextOffsetY = 0, .VerticalPlacement = VerticalAlign.Middle,
            .SizeBy = SizeMode.CapHeight, .LineSpacing = Math.Max(0.5, Math.Min(5.0, o.LineSpacing)),
            .CurveTolerance = tol, .FontLarge = font}
        Dim height = Math.Max(0.02, Math.Min(100.0, o.LetterHeight))
        Dim lines = text.Split(ControlChars.Lf).Select(Function(l) New TextLine(l, height, o.Align, font)).ToList()
        Dim centres As List(Of Double) = Nothing
        Return GlyphOutline.BuildShape(lines, s, warnings, centres, firstLineY)
    End Function

    ''' <summary>Non-zero union (clean orientation, overlaps merged), moved to the origin (offsetY: the Y shift applied).</summary>
    Private Function Normalize(raw As PathsD, warnings As List(Of String), ByRef offsetY As Double) As SvgShapeSet
        Dim result As New SvgShapeSet()
        result.Warnings.AddRange(warnings)
        If raw Is Nothing OrElse raw.Count = 0 Then Return result
        Dim u = Clipper.Union(raw, Nothing, FillRule.NonZero, GlyphOutline.ClipperPrecision)
        If u.Count = 0 Then Return result
        ' (Clipper's RectD uses screen names: top = minimum Y.)
        Dim b = Clipper.GetBounds(u)
        result.Paths = Clipper.TranslatePaths(u, -b.left, -b.top)
        offsetY = -b.top
        result.Width = b.right - b.left
        result.Height = b.bottom - b.top
        result.ElementCount = 1
        Return result
    End Function

    ''' <summary>The outline as an SVG document in inches (Y flipped to SVG's downward axis).</summary>
    Public Function ToSvg(shape As SvgShapeSet) As String
        Dim ci = CultureInfo.InvariantCulture
        Dim w = Math.Max(0.0001, shape.Width), h = Math.Max(0.0001, shape.Height)
        Dim sb As New StringBuilder()
        sb.AppendFormat(ci, "<svg xmlns=""http://www.w3.org/2000/svg"" width=""{0:0.#####}in"" height=""{1:0.#####}in"" viewBox=""0 0 {0:0.#####} {1:0.#####}"">", w, h)
        sb.Append("<path fill-rule=""nonzero"" d=""")
        For Each p In shape.Paths
            If p.Count < 3 Then Continue For
            For i = 0 To p.Count - 1
                sb.Append(If(i = 0, "M", "L")).AppendFormat(ci, "{0:0.#####} {1:0.#####} ", p(i).x, h - p(i).y)
            Next
            sb.Append("Z ")
        Next
        sb.Append("""/></svg>")
        Return sb.ToString()
    End Function
End Module
