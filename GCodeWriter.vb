' ============================================================================
'  GCodeWriter.vb
'  Serializes a linked Toolpath (list of ToolMove) to plain RS-274 G-code.
'  The toolpath is always computed in inches; this writer converts to mm
'  when the settings ask for G21 output.
' ============================================================================

Imports System.Globalization
Imports System.Text

Public Class GCodeWriter

    Private Const Inv As String = "en-US"
    Private Shared ReadOnly Ci As CultureInfo = CultureInfo.InvariantCulture

    ''' <summary>Builds the complete G-code program text.</summary>
    Public Shared Function Write(tp As Toolpath, s As CarveSettings, sourceText As String) As String
        Dim sb As New StringBuilder(Math.Max(1024, tp.Moves.Count * 24))
        Dim scale As Double = If(s.Units = OutputUnits.Millimeters, 25.4, 1.0)
        Dim unitName As String = If(s.Units = OutputUnits.Millimeters, "mm", "in")

        ' ---- header -------------------------------------------------------
        sb.AppendLine("%")
        sb.AppendLine("(Text_to_CNC_path V-carve)")
        sb.AppendLine("(Text: " & Comment(sourceText) & ")")
        sb.AppendLine("(Fonts: L=" & Comment(s.FontLarge.ToString()) & " " & F(s.SizeLargeIn * scale) &
                      ", M=" & Comment(s.FontMedium.ToString()) & " " & F(s.SizeMediumIn * scale) &
                      ", S=" & Comment(s.FontSmall.ToString()) & " " & F(s.SizeSmallIn * scale) &
                      " " & unitName & " " & If(s.SizeBy = SizeMode.CapHeight, "cap height", "em") & ")")
        sb.AppendLine("(Tool: V-bit " & F(s.ToolDiameterIn * scale) & " " & unitName & " dia, " &
                      F(s.IncludedAngleDeg) & " deg included)")
        sb.AppendLine("(Max depth " & F(s.EffectiveFlatDepth * scale) & " " & unitName &
                      ", depth step " & F(s.DepthStep * scale) & ", clearing stepover " & F(s.ClearStepover * scale) & ")")
        sb.AppendLine("(Blank " & F(s.BlankWidthIn * scale) & " x " & F(s.BlankHeightIn * scale) & " " & unitName &
                      ", lower-left corner at X" & F(s.BlankOriginX * scale) & " Y" & F(s.BlankOriginY * scale) & ")")
        sb.AppendLine("(Z0 = top of stock. Text extents X" & F(tp.MinX * scale) & " to X" & F(tp.MaxX * scale) &
                      ", Y" & F(tp.MinY * scale) & " to Y" & F(tp.MaxY * scale) & ")")
        sb.AppendLine("(Estimated time " & F(tp.EstimatedMinutes) & " min, " & tp.Contours.Count.ToString(Ci) & " passes)")
        sb.AppendLine(If(s.Units = OutputUnits.Millimeters, "G21", "G20") & " G90 G17 G94 G40 G49 G54")
        sb.AppendLine("G0 Z" & F(s.SafeZ * scale))
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

        For Each mv In tp.Moves
            Dim t = mv.Target
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
