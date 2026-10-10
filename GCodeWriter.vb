' ============================================================================
'  GCodeWriter.vb
'  Serializes a linked Toolpath (list of ToolMove) to plain RS-274 G-code.
'  The toolpath is always computed in inches; this writer converts to mm
'  when the settings ask for G21 output.
' ============================================================================

Imports System.Globalization
Imports System.Text

''' <summary>Consecutive segments cut with the same tool: one program file.</summary>
Public Class ToolGroup
    Public Property Tool As ToolDefinition
    Public Property Segments As New List(Of ToolpathSegment)

    Public ReadOnly Property EstimatedMinutes As Double
        Get
            Return Segments.Sum(Function(sg) sg.EstimatedMinutes)
        End Get
    End Property

    Public ReadOnly Property Operations As String
        Get
            Return String.Join("; ", Segments.Select(Function(sg) sg.Name))
        End Get
    End Property

    Public ReadOnly Property HasVCarve As Boolean
        Get
            Return Segments.Any(Function(sg) sg.Operation = CutOperation.VCarve)
        End Get
    End Property
End Class

Public Class GCodeWriter

    Private Const Inv As String = "en-US"
    Private Shared ReadOnly Ci As CultureInfo = CultureInfo.InvariantCulture

    ''' <summary>The job split where the tool changes (consecutive segments with the same tool stay together).</summary>
    Public Shared Function ToolGroups(tp As Toolpath) As List(Of ToolGroup)
        Dim groups As New List(Of ToolGroup)
        For Each sg In tp.Segments
            Dim last = groups.LastOrDefault()
            If last IsNot Nothing AndAlso last.Tool IsNot Nothing AndAlso sg.Tool IsNot Nothing AndAlso last.Tool.SameGeometry(sg.Tool) Then
                last.Segments.Add(sg)
            Else
                Dim g As New ToolGroup With {.Tool = sg.Tool}
                g.Segments.Add(sg)
                groups.Add(g)
            End If
        Next
        Return groups
    End Function

    ''' <summary>Builds the complete G-code program text for the whole job (one tool).</summary>
    Public Shared Function Write(tp As Toolpath, s As CarveSettings, sourceText As String) As String
        Return Write(tp, s, sourceText, Nothing, 1, 1)
    End Function

    ''' <summary>Builds the program for one tool group (Nothing: every move), file index of count.</summary>
    Public Shared Function Write(tp As Toolpath, s As CarveSettings, sourceText As String, group As ToolGroup, index As Integer, count As Integer) As String
        Dim moveList As IEnumerable(Of ToolMove)
        If group Is Nothing Then
            moveList = tp.Moves
        Else
            moveList = group.Segments.SelectMany(Function(sg) tp.Moves.Skip(sg.FirstMove).Take(sg.MoveCount))
        End If
        Dim tool As ToolDefinition = If(group?.Tool, If(tp.Segments.FirstOrDefault()?.Tool, s.CarveTool))
        Dim hasVCarve As Boolean = If(group Is Nothing, tp.Segments.Count = 0 OrElse tp.Segments.Any(Function(sg) sg.Operation = CutOperation.VCarve), group.HasVCarve)
        Dim minutes As Double = If(group Is Nothing, tp.EstimatedMinutes, group.EstimatedMinutes)
        Dim sb As New StringBuilder(Math.Max(1024, tp.Moves.Count * 24))
        Dim scale As Double = If(s.Units = OutputUnits.Millimeters, 25.4, 1.0)
        Dim unitName As String = If(s.Units = OutputUnits.Millimeters, "mm", "in")

        ' ---- header -------------------------------------------------------
        sb.AppendLine("%")
        If count > 1 Then
            sb.AppendLine("(CarveMaker job, program " & index.ToString(Ci) & " of " & count.ToString(Ci) & ")")
            sb.AppendLine("(Fit this tool and re-zero Z on the top of the stock before running)")
        Else
            sb.AppendLine(If(hasVCarve AndAlso tp.Segments.Count <= 1, "(CarveMaker V-carve)", "(CarveMaker job)"))
        End If
        sb.AppendLine("(Text: " & Comment(sourceText) & ")")
        sb.AppendLine("(Fonts: L=" & Comment(s.FontLarge.ToString()) & " " & F(s.SizeLargeIn * scale) &
                      ", M=" & Comment(s.FontMedium.ToString()) & " " & F(s.SizeMediumIn * scale) &
                      ", S=" & Comment(s.FontSmall.ToString()) & " " & F(s.SizeSmallIn * scale) &
                      " " & unitName & " " & If(s.SizeBy = SizeMode.CapHeight, "cap height", "em") & ")")
        If tp.Segments.Count > 1 OrElse (tp.Segments.Count = 1 AndAlso tp.Segments(0).Operation <> CutOperation.VCarve) Then
            sb.AppendLine("(Operations: " & Comment(If(group Is Nothing, String.Join("; ", tp.Segments.Select(Function(sg) sg.Name)), group.Operations)) & ")")
        End If
        Dim toolText = Comment(If(tool?.AsciiName(), "V-bit")) & " - " & F(If(tool Is Nothing, s.ToolDiameterIn, tool.Diameter) * scale) & " " & unitName & " dia"
        If tool IsNot Nothing AndAlso tool.UsesAngle Then toolText &= ", " & F(tool.AngleDeg) & " deg included"
        sb.AppendLine("(Tool: " & toolText & ")")
        If hasVCarve Then
            sb.AppendLine("(Max depth " & F(s.EffectiveFlatDepth * scale) & " " & unitName &
                          ", depth step " & F(s.DepthStep * scale) & ", clearing stepover " & F(s.ClearStepover * scale) & ")")
        Else
            Dim deepest = moveList.Select(Function(mv) mv.Target.Z).DefaultIfEmpty(0).Min()
            sb.AppendLine("(Max depth " & F(-deepest * scale) & " " & unitName & ")")
        End If
        sb.AppendLine("(Blank " & F(s.BlankWidthIn * scale) & " x " & F(s.BlankHeightIn * scale) & " " & unitName &
                      ", lower-left corner at X" & F(s.BlankOriginX * scale) & " Y" & F(s.BlankOriginY * scale) & ")")
        sb.AppendLine("(Z0 = top of stock. Text extents X" & F(tp.MinX * scale) & " to X" & F(tp.MaxX * scale) &
                      ", Y" & F(tp.MinY * scale) & " to Y" & F(tp.MaxY * scale) & ")")
        sb.AppendLine("(Estimated time " & F(minutes) & " min" & If(group Is Nothing, ", " & tp.Contours.Count.ToString(Ci) & " passes", "") & ")")
        sb.AppendLine(If(s.Units = OutputUnits.Millimeters, "G21", "G20") & " G90 G17 G94 G40 G49 G54")
        If s.ParkZAtEnd Then
            ' Fully up in machine coordinates before anything moves, then back to the work offset.
            sb.AppendLine("G0 G53 Z0 (retract to home)")
            sb.AppendLine("G54")
        Else
            sb.AppendLine("G0 Z" & F(s.SafeZ * scale))
        End If
        If s.CoolantOn Then sb.AppendLine("M8")
        sb.AppendLine("S" & s.SpindleRpm.ToString(Ci) & " M3")
        If s.SpindleDwellSeconds > 0 Then sb.AppendLine("G4 P" & s.SpindleDwellSeconds.ToString("0.###", Ci))

        ' ---- motion -------------------------------------------------------
        Dim lastFeed As Double = -1
        Dim lastKind As MoveKind = MoveKind.Rapid
        Dim haveLast As Boolean = False
        Dim last As Pt3
        Dim feedIn As Double = s.FeedRate * scale
        Dim plungeIn As Double = s.PlungeRate * scale

        For Each mv In moveList
            Dim t = mv.Target
            If Not haveLast Then
                ' First positioning move: travel in XY while fully up, then come down to the
                ' safe height on its own line. Never move XY and Z together on a rapid.
                sb.AppendLine("G0 X" & F(t.X * scale) & " Y" & F(t.Y * scale))
                sb.AppendLine("G0 Z" & F(t.Z * scale))
                last = t
                haveLast = True
                lastKind = mv.Kind
                Continue For
            End If
            Dim line As New StringBuilder(32)
            Select Case mv.Kind
                Case MoveKind.Rapid
                    line.Append("G0")
                Case Else
                    line.Append("G1")
            End Select

            ' Only emit axes that changed (modal G-code), always emit all on the first move.
            If Not haveLast OrElse Math.Abs(t.X - last.X) > 0.000001 Then line.Append(" X").Append(F(t.X * scale))
            If Not haveLast OrElse Math.Abs(t.Y - last.Y) > 0.000001 Then line.Append(" Y").Append(F(t.Y * scale))
            If Not haveLast OrElse Math.Abs(t.Z - last.Z) > 0.000001 Then line.Append(" Z").Append(F(t.Z * scale))

            If mv.Kind <> MoveKind.Rapid Then
                Dim fr As Double = If(mv.Feed > 0, mv.Feed * scale, If(mv.Kind = MoveKind.Plunge, plungeIn, feedIn))
                If Math.Abs(fr - lastFeed) > 0.0001 Then
                    line.Append(" F").Append(F(fr))
                    lastFeed = fr
                End If
            End If

            ' Skip no-op lines (e.g. two identical targets in a row).
            If line.Length > 2 OrElse mv.Kind <> lastKind Then
                sb.AppendLine(line.ToString())
            End If
            last = t
            haveLast = True
            lastKind = mv.Kind
        Next

        ' ---- footer -------------------------------------------------------
        If Not haveLast OrElse last.Z < s.SafeZ - 0.000001 Then
            sb.AppendLine("G0 Z" & F(s.SafeZ * scale))
        End If
        If s.ParkZAtEnd Then sb.AppendLine("G0 G53 Z0")
        sb.AppendLine("M5")
        If s.CoolantOn Then sb.AppendLine("M9")
        sb.AppendLine("G0 X0 Y0")
        sb.AppendLine("M30")
        sb.AppendLine("%")
        Return sb.ToString()
    End Function

    ''' <summary>Fixed 4-decimal, invariant-culture number with no trailing garbage.</summary>
    Public Shared Function F(v As Double) As String
        ' Avoid "-0.0000"
        If Math.Abs(v) < 0.00005 Then v = 0
        Return v.ToString("0.0000", Ci)
    End Function

    ''' <summary>Makes a string safe inside a parenthesised G-code comment.</summary>
    Private Shared Function Comment(text As String) As String
        If text Is Nothing Then Return ""
        Dim t = text.Replace(vbCrLf, " / ").Replace(vbLf, " / ").Replace(vbCr, " / ")
        t = t.Replace("("c, "["c).Replace(")"c, "]"c).Replace("%"c, " "c).Replace(";"c, ","c)
        ' Printable 7-bit ASCII only: some controls alarm on control or non-ASCII bytes.
        Dim sb As New StringBuilder(t.Length)
        For Each ch In t
            sb.Append(If(ch >= " "c AndAlso ch <= "~"c, ch, " "c))
        Next
        t = sb.ToString()
        If t.Length > 120 Then t = t.Substring(0, 117) & "..."
        Return t
    End Function
End Class
