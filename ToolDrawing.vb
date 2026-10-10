' ============================================================================
'  ToolDrawing.vb
'  Side view of a tool, to scale: flutes (light steel with helix marks), the
'  shank stub above them (darker), the centre line, and dimensions for the
'  diameter, flute length, end radius and angle that the tool type uses.
'  Also renders the small type icons for the tool list.
' ============================================================================

Imports System.ComponentModel
Imports System.Drawing.Drawing2D
Imports System.Drawing.Text

Public Class ToolDrawing
    Inherits Control

    Private _tool As ToolDefinition

    Private Shared ReadOnly DimColor As Color = Color.FromArgb(0, 92, 175)
    Private Shared ReadOnly OutlineColor As Color = Color.FromArgb(45, 48, 52)

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
        BackColor = Color.White
    End Sub

    ''' <summary>The tool to draw (Nothing shows a hint).</summary>
    <Browsable(False), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Tool As ToolDefinition
        Get
            Return _tool
        End Get
        Set(value As ToolDefinition)
            _tool = value
            Invalidate()
        End Set
    End Property

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(BackColor)
        If _tool Is Nothing OrElse Not (_tool.Diameter > 0) OrElse Not (_tool.Length > 0) Then
            TextRenderer.DrawText(g, "Select or create a tool", Font, ClientRectangle, SystemColors.GrayText,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            Return
        End If
        Try
            Draw(g, ClientRectangle, _tool, True, Font)
        Catch ex As Exception
            TextRenderer.DrawText(g, "Cannot draw this tool: " & ex.Message, Font, ClientRectangle, Color.Firebrick,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
        End Try
    End Sub

    ''' <summary>Draws the tool to fit the rectangle, with or without dimensions.</summary>
    Public Shared Sub Draw(g As Graphics, bounds As Rectangle, tool As ToolDefinition, withDimensions As Boolean, font As Font)
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit

        Dim flute = tool.FluteProfile(32)
        Dim d2 = tool.Diameter / 2.0
        Dim L = tool.Length
        Dim top = L + tool.ShankStub

        ' Room for the dimension text around the tool.
        Dim mL, mR, mT, mB As Integer
        If withDimensions Then
            mL = 30 : mR = 150 : mT = 56 : mB = 64
        Else
            mL = 1 : mR = 1 : mT = 1 : mB = 1
        End If
        Dim area As New RectangleF(bounds.X + mL, bounds.Y + mT, Math.Max(4, bounds.Width - mL - mR), Math.Max(4, bounds.Height - mT - mB))
        Dim s As Double = Math.Min(area.Width / (2 * d2), area.Height / top)
        Dim cx As Single = area.X + area.Width / 2.0F
        Dim baseY As Single = CSng(area.Y + (area.Height + top * s) / 2.0)
        Dim P = Function(pr As Double, ph As Double) New PointF(CSng(cx + pr * s), CSng(baseY - ph * s))

        ' Flute outline: right side up, then the left side back down.
        Dim fluteRight = flute.Select(Function(q) P(q.R, q.H)).ToList()
        Dim flutePoly As New List(Of PointF)(fluteRight)
        For i = flute.Count - 1 To 0 Step -1
            flutePoly.Add(P(-flute(i).R, flute(i).H))
        Next
        Dim shankRect As New RectangleF(CSng(cx - d2 * s), CSng(baseY - top * s), CSng(2 * d2 * s), CSng(tool.ShankStub * s))

        ' Shank (darker, round shading).
        If shankRect.Width >= 1 AndAlso shankRect.Height >= 1 Then
            Using b As New LinearGradientBrush(New RectangleF(shankRect.X - 1, shankRect.Y, shankRect.Width + 2, shankRect.Height), Color.Black, Color.Black, LinearGradientMode.Horizontal)
                b.InterpolationColors = Blend3(Color.FromArgb(88, 92, 98), Color.FromArgb(178, 182, 188))
                g.FillRectangle(b, shankRect)
            End Using
        End If

        ' Flutes (light steel, helix marks clipped to the outline).
        Using path As New GraphicsPath()
            path.AddPolygon(flutePoly.ToArray())
            Dim fb = path.GetBounds()
            If fb.Width >= 1 AndAlso fb.Height >= 1 Then
                Using b As New LinearGradientBrush(New RectangleF(fb.X - 1, fb.Y, fb.Width + 2, fb.Height + 1), Color.Black, Color.Black, LinearGradientMode.Horizontal)
                    b.InterpolationColors = Blend3(Color.FromArgb(132, 142, 152), Color.FromArgb(236, 240, 244))
                    g.FillPath(b, path)
                End Using
                If withDimensions Then
                    Dim saved = g.Save()
                    g.SetClip(path)
                    Dim pitch = Math.Max(6.0F, CSng(d2 * s * 0.9))
                    Using hp As New Pen(Color.FromArgb(70, 40, 50, 60), Math.Max(1.0F, pitch * 0.12F))
                        Dim y = fb.Bottom + fb.Width
                        While y > fb.Top - fb.Width
                            g.DrawLine(hp, fb.Left - 2, y, fb.Right + 2, y - fb.Width * 0.55F)
                            y -= pitch
                        End While
                    End Using
                    g.Restore(saved)
                End If
            End If
            Using pen As New Pen(OutlineColor, If(withDimensions, 1.6F, 1.0F))
                pen.LineJoin = LineJoin.Round
                g.DrawPath(pen, path)
                If shankRect.Width >= 1 Then g.DrawRectangle(pen, shankRect.X, shankRect.Y, shankRect.Width, shankRect.Height)
            End Using
        End Using

        If Not withDimensions Then Return

        ' Centre line.
        Using cl As New Pen(Color.FromArgb(150, 150, 150), 1.0F)
            cl.DashStyle = DashStyle.DashDot
            g.DrawLine(cl, cx, shankRect.Y - 12, cx, baseY + 12)
        End Using

        Using dimPen As New Pen(DimColor, 1.2F), extPen As New Pen(Color.FromArgb(150, DimColor), 1.0F), txt As New SolidBrush(DimColor)
            Dim arrows As New AdjustableArrowCap(4, 6, True)
            dimPen.CustomStartCap = arrows
            dimPen.CustomEndCap = arrows

            ' Diameter, above the shank.
            Dim yD = shankRect.Y - 20
            g.DrawLine(extPen, shankRect.Left, shankRect.Y - 3, shankRect.Left, yD - 5)
            g.DrawLine(extPen, shankRect.Right, shankRect.Y - 3, shankRect.Right, yD - 5)
            g.DrawLine(dimPen, shankRect.Left, yD, shankRect.Right, yD)
            DrawLabel(g, "Ø " & InchFormat.DimText(tool.Diameter), font, txt, New PointF(cx, yD - 4), ContentAlignment.BottomCenter)

            ' Flute length, on the right from the tip to the top of the flutes.
            Dim xL = cx + CSng(d2 * s) + 26
            Dim yTip = baseY, yTop = CSng(baseY - L * s)
            Dim tipX = CSng(cx + flute(0).R * s)
            g.DrawLine(extPen, tipX + 3, yTip, xL + 5, yTip)
            g.DrawLine(extPen, fluteRight(fluteRight.Count - 1).X + 3, yTop, xL + 5, yTop)
            If yTip - yTop > 14 Then
                g.DrawLine(dimPen, xL, yTop, xL, yTip)
            Else
                Using tick As New Pen(DimColor, 1.2F)
                    g.DrawLine(tick, xL, yTop - 10, xL, yTip + 10)
                End Using
            End If
            DrawLabel(g, "Flute " & InchFormat.DimText(L), font, txt, New PointF(xL + 6, (yTip + yTop) / 2), ContentAlignment.MiddleLeft)

            ' End radius, with a leader from the arc centre.
            Dim r = tool.EffectiveEndRadius
            If tool.UsesRadius AndAlso r > 0 Then
                Dim ccR As Double = If(tool.UsesAngle, 0.0, Math.Max(0.0, d2 - r))
                Dim ccH As Double = r
                Dim th As Double = If(tool.UsesAngle, -Math.PI / 2 + 0.35, -Math.PI / 4)
                Dim c = P(ccR, ccH)
                Dim onArc = P(ccR + r * Math.Cos(th), ccH + r * Math.Sin(th))
                Dim dx = onArc.X - c.X, dy = onArc.Y - c.Y
                Dim len = CSng(Math.Max(1.0, Math.Sqrt(dx * dx + dy * dy)))
                Dim outPt As New PointF(onArc.X + dx / len * 34, onArc.Y + dy / len * 34)
                Using lead As New Pen(DimColor, 1.2F)
                    lead.CustomStartCap = New AdjustableArrowCap(4, 6, True)
                    g.DrawLine(lead, onArc, outPt)
                    g.DrawLine(lead, onArc, c)
                End Using
                g.FillEllipse(txt, c.X - 2, c.Y - 2, 4, 4)
                Dim label = If(tool.Type = ToolType.BallNose, "R " & InchFormat.DimText(r) & " (ball)", "R " & InchFormat.DimText(r))
                DrawLabel(g, label, font, txt, New PointF(outPt.X + 3, outPt.Y + 2), ContentAlignment.TopLeft)
            End If

            ' Included angle. Sharp tips: an arc between the flanks at the point. Tapered ball
            ' nose: the cone's apex lies far below the ball, so mark the per-side angle
            ' between the axis direction and the flank, on the flank itself.
            If tool.UsesAngle Then
                Dim a = Math.Max(0.1, Math.Min(179.9, tool.AngleDeg)) * Math.PI / 360.0
                Dim aDeg = CSng(a * 180 / Math.PI)
                Dim coneTop = Math.Min(tool.FullDiameterHeight, L)
                Using arcPen As New Pen(DimColor, 1.4F)
                    arcPen.CustomStartCap = New AdjustableArrowCap(3, 5, True)
                    arcPen.CustomEndCap = New AdjustableArrowCap(3, 5, True)
                    If r > 0 Then
                        Dim hT = r * (1 - Math.Sin(a)), rT = r * Math.Cos(a)
                        Dim pivotH = hT + (coneTop - hT) * 0.25
                        Dim pivot = P(-(rT + (pivotH - hT) * Math.Tan(a)), pivotH)
                        Dim rho = CSng(Math.Max(40.0, Math.Min(90.0, (coneTop - pivotH) * s * 0.5)))
                        Using refPen As New Pen(Color.FromArgb(160, DimColor), 1.0F)
                            refPen.DashStyle = DashStyle.Dash
                            g.DrawLine(refPen, pivot.X, pivot.Y, pivot.X, pivot.Y - rho - 6)
                        End Using
                        g.DrawArc(arcPen, pivot.X - rho, pivot.Y - rho, 2 * rho, 2 * rho, -90 - aDeg, aDeg)
                        Dim side = (tool.AngleDeg / 2).ToString("0.##", Globalization.CultureInfo.InvariantCulture)
                        DrawLabel(g, side & "° per side (" & tool.AngleDeg.ToString("0.##", Globalization.CultureInfo.InvariantCulture) & "° included)", font, txt, New PointF(pivot.X - 8, pivot.Y - rho - 4), ContentAlignment.BottomRight)
                    Else
                        Dim apex = P(0, 0)
                        Dim rho = CSng(Math.Max(22.0, Math.Min(70.0, coneTop * s * 0.55)))
                        g.DrawArc(arcPen, apex.X - rho, apex.Y - rho, 2 * rho, 2 * rho, -90 - aDeg, 2 * aDeg)
                    End If
                End Using
                Dim incl = tool.AngleDeg.ToString("0.##", Globalization.CultureInfo.InvariantCulture)
                Dim half = (tool.AngleDeg / 2).ToString("0.##", Globalization.CultureInfo.InvariantCulture)
                If r <= 0 Then
                    Dim labelY = Math.Min(baseY + 10, bounds.Bottom - font.Height - 4)
                    DrawLabel(g, incl & "° included (" & half & "° per side)", font, txt, New PointF(cx, labelY), ContentAlignment.TopCenter)
                End If
            End If
        End Using
    End Sub

    Private Shared Function Blend3(edge As Color, mid As Color) As ColorBlend
        Return New ColorBlend(3) With {.Colors = {edge, mid, edge}, .Positions = {0.0F, 0.42F, 1.0F}}
    End Function

    Private Shared Sub DrawLabel(g As Graphics, text As String, font As Font, brush As Brush, at As PointF, align As ContentAlignment)
        Dim sz = g.MeasureString(text, font)
        Dim x = at.X, y = at.Y
        Select Case align
            Case ContentAlignment.BottomCenter : x -= sz.Width / 2 : y -= sz.Height
            Case ContentAlignment.TopCenter : x -= sz.Width / 2
            Case ContentAlignment.MiddleLeft : y -= sz.Height / 2
            Case ContentAlignment.BottomRight : x -= sz.Width : y -= sz.Height
        End Select
        Using bg As New SolidBrush(Color.FromArgb(225, Color.White))
            g.FillRectangle(bg, x, y, sz.Width, sz.Height)
        End Using
        g.DrawString(text, font, brush, x, y)
    End Sub

    ''' <summary>Small silhouette of a type for the tool list.</summary>
    Public Shared Function RenderIcon(type As ToolType, size As Integer) As Bitmap
        Dim t As ToolDefinition
        Select Case type
            Case ToolType.FlatEndMill : t = ToolDefinition.Create(type, 1, 1.1)
            Case ToolType.BallNose : t = ToolDefinition.Create(type, 1, 1.1)
            Case ToolType.BullNose : t = ToolDefinition.Create(type, 1, 1.1, 0.25)
            Case ToolType.TaperedBallNose : t = ToolDefinition.Create(type, 1, 1.6, 0.18, 30)
            Case ToolType.Drill : t = ToolDefinition.Create(type, 1, 1.1, 0, 118)
            Case Else : t = ToolDefinition.MakeVBit(1, 70)
        End Select
        Dim bmp As New Bitmap(size, size, Imaging.PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.Transparent)
            Draw(g, New Rectangle(0, 0, size, size), t, False, SystemFonts.DefaultFont)
        End Using
        Return bmp
    End Function
End Class
