' ============================================================================
'  GlyphOutline.vb
'  Turns formatted text lines + a font into a clean set of polygons (outers +
'  holes) in machine coordinates (inches, X right, Y up) using GDI+ glyph
'  outlines and a Clipper2 non-zero union to normalize orientation and merge
'  overlapping glyphs (common in script fonts).
' ============================================================================

Imports System.Drawing.Drawing2D
Imports Clipper2Lib

Public Module GlyphOutline

    ''' <summary>
    ''' Decimal places Clipper uses when scaling inch coordinates to integers.
    ''' 5 => 0.00001" resolution; plenty for a CNC router and far from overflow.
    ''' </summary>
    Public Const ClipperPrecision As Integer = 5

    ''' <summary>GDI+ em size used while extracting; coordinates are rescaled to inches afterwards.</summary>
    Private Const WorkEm As Single = 1000.0F

    ''' <summary>
    ''' Builds the normalized text shape. Each line has its own size and alignment;
    ''' lines are stacked top to bottom with the font's natural line height.
    ''' Returns an empty PathsD for blank text. Throws ArgumentException when the font
    ''' family is not installed.
    ''' </summary>
    ''' <summary>Per-font values computed once per BuildShape call.</summary>
    Private Class FontInfo
        Public Family As FontFamily
        Public Style As FontStyle
        Public EmPerSize As Double = 1.0     ' em height per inch of "size"
        Public NaturalLine As Double = 1.2   ' line pitch in em multiples
    End Class

    Public Function BuildShape(lines As IList(Of TextLine), s As CarveSettings, warnings As List(Of String)) As PathsD
        Dim unused As List(Of Double) = Nothing
        Return BuildShape(lines, s, warnings, unused)
    End Function

    ''' <summary>
    ''' As above, and also returns the machine-Y centre of every text line (top line
    ''' first) so letters can be assigned back to their line for cut ordering.
    ''' </summary>
    Public Function BuildShape(lines As IList(Of TextLine), s As CarveSettings, warnings As List(Of String),
                               ByRef lineCenters As List(Of Double)) As PathsD
        Dim result As New PathsD()
        lineCenters = New List(Of Double)
        If lines Is Nothing OrElse lines.Count = 0 Then Return result
        Dim centersDown As New List(Of Double)   ' GDI+ y-down line centres, inches

        ' Fonts are resolved once per distinct family/style and disposed at the end.
        Dim fonts As New Dictionary(Of String, FontInfo)(StringComparer.OrdinalIgnoreCase)
        Try
            ' Raw contours (GDI+ y-down coordinates, inches) grouped per line for alignment.
            Dim perLine As New List(Of List(Of PathD))
            Dim lineMinX As New List(Of Double)
            Dim lineMaxX As New List(Of Double)
            Dim lineTop As Double = 0

            For li = 0 To lines.Count - 1
                Dim ln = lines(li)
                Dim fi As FontInfo = ResolveFont(If(ln.Font, s.FontLarge), s, fonts, warnings)
                Dim family As FontFamily = fi.Family
                Dim style As FontStyle = fi.Style
                Dim naturalLine As Double = fi.NaturalLine
                Dim em As Double = Math.Max(0.01, ln.SizeIn) * fi.EmPerSize
                Dim scale As Double = em / WorkEm
                Dim flatness As Single = CSng(Math.Max(0.01, s.CurveTolerance / scale))
                Dim contours As New List(Of PathD)
                Dim minX As Double = Double.MaxValue, maxX As Double = Double.MinValue
                ' Soft line breaks (vertical tab) and tabs have no glyphs: treat them as spaces.
                Dim lineText As String = If(ln.Text, "").Replace(vbCr, "").Replace(vbLf, "").Replace(ChrW(11), " "c).Replace(vbTab, " ").TrimEnd()
                If lineText.Length > 0 Then
                    Using gp As New GraphicsPath(FillMode.Winding)
                        ' GenericTypographic wraps a process-wide native object: always work on an
                        ' independent copy. Typographic layout = no 1/6 em padding, true advances.
                        Using sf As New StringFormat(StringFormat.GenericTypographic)
                            sf.FormatFlags = sf.FormatFlags Or StringFormatFlags.NoWrap Or StringFormatFlags.NoClip
                            gp.AddString(lineText, family, CInt(style), WorkEm, New PointF(0, 0), sf)
                        End Using
                        If gp.PointCount > 0 Then
                            gp.Flatten(Nothing, flatness)
                            For Each fig In SplitFigures(gp)
                                Dim pd As New PathD(fig.Count)
                                For Each p In fig
                                    Dim x As Double = p.X * scale
                                    Dim y As Double = p.Y * scale + lineTop
                                    pd.Add(New PointD(x, y))
                                    If x < minX Then minX = x
                                    If x > maxX Then maxX = x
                                Next
                                If pd.Count >= 3 Then contours.Add(pd)
                            Next
                        End If
                    End Using
                End If
                perLine.Add(contours)
                lineMinX.Add(If(contours.Count = 0, 0.0, minX))
                lineMaxX.Add(If(contours.Count = 0, 0.0, maxX))
                ' Next line starts one natural line height (of THIS line's size) lower.
                Dim advance As Double = em * naturalLine * s.LineSpacing
                centersDown.Add(lineTop + advance / 2.0)
                lineTop += advance
            Next

            ' Horizontal justification of each line across the blank (between the margins).
            Dim blankLeft As Double = s.BlankOriginX
            Dim blankRight As Double = s.BlankOriginX + s.BlankWidthIn
            Dim blankBottom As Double = s.BlankOriginY
            Dim blankTop As Double = s.BlankOriginY + s.BlankHeightIn
            Dim margin As Double = Math.Max(0.0, s.MarginIn)

            Dim raw As New PathsD()
            For li = 0 To perLine.Count - 1
                If perLine(li).Count = 0 Then Continue For
                Dim w As Double = lineMaxX(li) - lineMinX(li)
                Dim lineLeft As Double
                Select Case lines(li).Align
                    Case TextAlign.Center : lineLeft = blankLeft + (s.BlankWidthIn - w) / 2.0
                    Case TextAlign.Right : lineLeft = blankRight - margin - w
                    Case Else : lineLeft = blankLeft + margin
                End Select
                Dim shift As Double = lineLeft - lineMinX(li) + s.TextOffsetX
                For Each c In perLine(li)
                    Dim moved As New PathD(c.Count)
                    For Each p In c
                        ' Flip to CNC Y-up while we are here.
                        moved.Add(New PointD(p.x + shift, -p.y))
                    Next
                    raw.Add(moved)
                Next
            Next
            If raw.Count = 0 Then Return result

            ' Non-zero union: fonts wind holes opposite to their parent contour (TrueType
            ' outers one way, CFF fonts the other, a few CFF fonts even vary per glyph),
            ' and some fonts build glyphs from overlapping same-direction pieces. A non-zero
            ' fill handles all of that, merges glyphs that overlap (script fonts), and
            ' returns clean polygons with outers positive and holes negative.
            ' NOTE: the 2-argument Union overload silently uses 2 decimal places.
            result = Clipper.Union(raw, Nothing, FillRule.NonZero, ClipperPrecision)
            If result.Count = 0 Then Return result

            ' Vertical placement of the whole block on the blank.
            ' (Clipper's RectD uses screen names: top = minimum Y, bottom = maximum Y.)
            Dim b As RectD = Clipper.GetBounds(result)
            Dim blockMinY As Double = b.top
            Dim blockMaxY As Double = b.bottom
            Dim blockH As Double = blockMaxY - blockMinY
            Dim targetMinY As Double
            Select Case s.VerticalPlacement
                Case VerticalAlign.Top : targetMinY = blankTop - margin - blockH
                Case VerticalAlign.Bottom : targetMinY = blankBottom + margin
                Case Else : targetMinY = blankBottom + (s.BlankHeightIn - blockH) / 2.0
            End Select
            Dim dy As Double = targetMinY - blockMinY + s.TextOffsetY
            If Math.Abs(dy) > 0.0000001 Then
                result = Clipper.TranslatePaths(result, 0, dy)
            End If
            For Each c In centersDown
                lineCenters.Add(-c + dy)
            Next

            ' Tell the user when the text does not fit on the blank.
            b = Clipper.GetBounds(result)
            Dim ci = Globalization.CultureInfo.InvariantCulture
            If b.left < blankLeft - 0.0001 OrElse b.right > blankRight + 0.0001 Then
                warnings.Add(String.Format(ci, "Text runs off the {0} edge of the blank (text X {1:0.00} to {2:0.00}, blank {3:0.00} to {4:0.00}).",
                                           If(b.left < blankLeft - 0.0001, "left", "right"), b.left, b.right, blankLeft, blankRight))
            End If
            If b.top < blankBottom - 0.0001 OrElse b.bottom > blankTop + 0.0001 Then
                warnings.Add(String.Format(ci, "Text runs off the {0} edge of the blank (text Y {1:0.00} to {2:0.00}, blank {3:0.00} to {4:0.00}).",
                                           If(b.top < blankBottom - 0.0001, "bottom", "top"), b.top, b.bottom, blankBottom, blankTop))
            End If
        Finally
            For Each fi In fonts.Values
                fi.Family.Dispose()
            Next
        End Try
        Return result
    End Function

    ''' <summary>Loads (or reuses) the family for a font choice and computes its metrics.</summary>
    Private Function ResolveFont(choice As FontChoice, s As CarveSettings, cache As Dictionary(Of String, FontInfo),
                                 warnings As List(Of String)) As FontInfo
        Dim key As String = choice.ToString()
        Dim fi As FontInfo = Nothing
        If cache.TryGetValue(key, fi) Then Return fi

        Dim family As FontFamily
        Try
            family = New FontFamily(choice.Family)
        Catch ex As ArgumentException
            Throw New ArgumentException("Font '" & choice.Family & "' is not installed.", ex)
        End Try

        fi = New FontInfo With {.Family = family}
        fi.Style = PickStyle(family, choice.Style(), warnings)
        ' Size -> em conversion (cap-height sizing measures a capital H once).
        If s.SizeBy = SizeMode.CapHeight Then
            Dim capRatio As Double = MeasureCapRatio(family, fi.Style)
            If capRatio > 0.05 Then
                fi.EmPerSize = 1.0 / capRatio
            Else
                warnings.Add("Font '" & family.Name & "' has no capital H outline; its sizes are used as em heights.")
            End If
        End If
        Dim emHeight As Double = family.GetEmHeight(fi.Style)
        fi.NaturalLine = family.GetLineSpacing(fi.Style) / emHeight
        cache(key) = fi
        Return fi
    End Function

    ''' <summary>Height of a capital H as a fraction of the em size (0 when the font has no H outline).</summary>
    Private Function MeasureCapRatio(family As FontFamily, style As FontStyle) As Double
        Using gp As New GraphicsPath(FillMode.Winding)
            Using sf As New StringFormat(StringFormat.GenericTypographic)
                gp.AddString("H", family, CInt(style), WorkEm, New PointF(0, 0), sf)
            End Using
            If gp.PointCount = 0 Then Return 0
            gp.Flatten(Nothing, 1.0F)
            Return gp.GetBounds().Height / WorkEm
        End Using
    End Function

    ''' <summary>Chooses the requested style if the family has it, otherwise the first available one.</summary>
    Private Function PickStyle(family As FontFamily, wanted As FontStyle, warnings As List(Of String)) As FontStyle
        If family.IsStyleAvailable(wanted) Then Return wanted
        For Each candidate In New FontStyle() {FontStyle.Regular, FontStyle.Bold, FontStyle.Italic, FontStyle.Bold Or FontStyle.Italic}
            If family.IsStyleAvailable(candidate) Then
                warnings.Add("Font '" & family.Name & "' has no " & wanted.ToString() & " style; using " & candidate.ToString() & ".")
                Return candidate
            End If
        Next
        Return wanted
    End Function

    ''' <summary>Splits a flattened GraphicsPath into closed polygons (duplicate points removed).</summary>
    Public Function SplitFigures(gp As GraphicsPath) As List(Of List(Of PointF))
        Dim figures As New List(Of List(Of PointF))
        Dim pts As PointF() = gp.PathPoints
        Dim types As Byte() = gp.PathTypes
        Dim current As List(Of PointF) = Nothing

        For i = 0 To pts.Length - 1
            Dim t As Byte = types(i)
            Dim kind As Byte = CByte(t And CByte(PathPointType.PathTypeMask))
            If kind = CByte(PathPointType.Start) Then
                FinishFigure(figures, current)
                current = New List(Of PointF)()
            End If
            If current Is Nothing Then current = New List(Of PointF)()
            If current.Count = 0 OrElse Not SamePoint(current(current.Count - 1), pts(i)) Then
                current.Add(pts(i))
            End If
            If (t And CByte(PathPointType.CloseSubpath)) <> 0 Then
                FinishFigure(figures, current)
                current = Nothing
            End If
        Next
        FinishFigure(figures, current)
        Return figures
    End Function

    Private Sub FinishFigure(figures As List(Of List(Of PointF)), fig As List(Of PointF))
        If fig Is Nothing Then Return
        ' Drop an explicit closing point equal to the first one.
        While fig.Count > 1 AndAlso SamePoint(fig(0), fig(fig.Count - 1))
            fig.RemoveAt(fig.Count - 1)
        End While
        If fig.Count >= 3 Then figures.Add(fig)
    End Sub

    Private Function SamePoint(a As PointF, b As PointF) As Boolean
        Return Math.Abs(a.X - b.X) < 0.0001F AndAlso Math.Abs(a.Y - b.Y) < 0.0001F
    End Function

    ''' <summary>Converts a Clipper path to display points.</summary>
    Public Function ToPts(p As PathD) As List(Of Pt2)
        Dim l As New List(Of Pt2)(p.Count)
        For Each q In p
            l.Add(New Pt2(q.x, q.y))
        Next
        Return l
    End Function
End Module
