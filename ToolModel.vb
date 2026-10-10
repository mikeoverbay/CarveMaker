' ============================================================================
'  ToolModel.vb
'  Triangle mesh of a cutting tool, built by revolving its side profile
'  (ToolDefinition.FullProfile): the flutes from the tip up to the flute
'  length, then a shank stub one diameter long. Works for every tool type
'  (flat, ball, bull nose, V-bit, tapered ball nose, drill). Arcs get smooth
'  normals, straight edges flat ones. Local coordinates: the tip is at the
'  origin, +Z runs up the tool, units are inches.
'  Used to draw the tool in the view and to stamp material removal into the
'  simulation heightmap (rendered from above, each texel keeps the deepest point).
' ============================================================================

Public Class ToolModel
    ''' <summary>Interleaved position (3) + normal (3) per vertex, three vertices per triangle.</summary>
    Public ReadOnly Vertices As Single()
    Public ReadOnly VertexCount As Integer
    Public ReadOnly Diameter As Double
    ''' <summary>Total height of the model (flutes + shank stub).</summary>
    Public ReadOnly Height As Double

    Public Sub New(tool As ToolDefinition, Optional segments As Integer = 48)
        Dim profile = tool.FullProfile(12)
        Diameter = Math.Max(0.0001, tool.Diameter)
        Height = profile(profile.Count - 1).H

        Dim n As Integer = Math.Max(8, segments)
        Dim verts As New List(Of Single)(profile.Count * n * 36)
        For i = 0 To profile.Count - 2
            Dim p0 = profile(i), p1 = profile(i + 1)
            Dim r0 = p0.R, z0 = p0.H, r1 = p1.R, z1 = p1.H
            Dim dr = r1 - r0, dz = z1 - z0
            Dim len = Math.Sqrt(dr * dr + dz * dz)
            If len < 0.000000001 Then Continue For
            ' Outward normal of the edge in the (r, z) plane; arcs use their exact normals.
            Dim er = dz / len, ez = -dr / len
            Dim nr0 = er, nz0 = ez, nr1 = er, nz1 = ez
            If p0.Smooth AndAlso p1.Smooth Then
                nr0 = p0.NR : nz0 = p0.NH : nr1 = p1.NR : nz1 = p1.NH
            End If
            For k = 0 To n - 1
                Dim a0 = 2 * Math.PI * k / n
                Dim a1 = 2 * Math.PI * (k + 1) / n
                Dim c0 = Math.Cos(a0), s0 = Math.Sin(a0), c1 = Math.Cos(a1), s1 = Math.Sin(a1)
                ' Quad (r0,a0)-(r1,a0)-(r1,a1)-(r0,a1) as two triangles, degenerate ones skipped.
                Dim p00 = {r0 * c0, r0 * s0, z0}, p10 = {r1 * c0, r1 * s0, z1}
                Dim p11 = {r1 * c1, r1 * s1, z1}, p01 = {r0 * c1, r0 * s1, z0}
                Dim n00 = {nr0 * c0, nr0 * s0, nz0}, n01 = {nr0 * c1, nr0 * s1, nz0}
                Dim n10 = {nr1 * c0, nr1 * s0, nz1}, n11 = {nr1 * c1, nr1 * s1, nz1}
                If r1 > 0 Then AddTri(verts, p00, n00, p10, n10, p11, n11)
                If r0 > 0 Then AddTri(verts, p00, n00, p11, n11, p01, n01)
            Next
        Next
        Vertices = verts.ToArray()
        VertexCount = Vertices.Length \ 6
    End Sub

    Private Shared Sub AddTri(verts As List(Of Single), p0 As Double(), n0 As Double(), p1 As Double(), n1 As Double(), p2 As Double(), n2 As Double())
        AddVertex(verts, p0, n0)
        AddVertex(verts, p1, n1)
        AddVertex(verts, p2, n2)
    End Sub

    Private Shared Sub AddVertex(verts As List(Of Single), p As Double(), n As Double())
        verts.Add(CSng(p(0))) : verts.Add(CSng(p(1))) : verts.Add(CSng(p(2)))
        verts.Add(CSng(n(0))) : verts.Add(CSng(n(1))) : verts.Add(CSng(n(2)))
    End Sub
End Class
