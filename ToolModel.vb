' ============================================================================
'  ToolModel.vb
'  Triangle mesh of the V-bit, built by revolving its side profile: an optional
'  tip flat, the cone up to the full diameter, then a cylinder one diameter
'  long (from the side: a square sitting on a cone). Local coordinates: the tip
'  is at the origin, +Z runs up the tool, units are inches.
'  Used to draw the tool in the view and to stamp material removal into the
'  simulation heightmap (rendered from above, each texel keeps the deepest point).
' ============================================================================

Public Class ToolModel
    ''' <summary>Interleaved position (3) + normal (3) per vertex, three vertices per triangle.</summary>
    Public ReadOnly Vertices As Single()
    Public ReadOnly VertexCount As Integer
    Public ReadOnly Diameter As Double
    Public ReadOnly TipFlat As Double
    Public ReadOnly IncludedAngleDeg As Double
    ''' <summary>Height of the cone part: where the full diameter is reached.</summary>
    Public ReadOnly ConeHeight As Double
    ''' <summary>Total height of the model (cone + cylinder).</summary>
    Public ReadOnly Height As Double

    Public Sub New(diameterIn As Double, tipFlatIn As Double, includedAngleDeg As Double, Optional segments As Integer = 48)
        Diameter = Math.Max(0.001, diameterIn)
        TipFlat = Math.Max(0.0, Math.Min(tipFlatIn, Diameter * 0.999))
        IncludedAngleDeg = Math.Max(1.0, Math.Min(179.0, includedAngleDeg))
        Dim tanA As Double = Math.Tan(IncludedAngleDeg * Math.PI / 360.0)
        Dim rTip As Double = TipFlat / 2.0
        Dim rFull As Double = Diameter / 2.0
        ConeHeight = (rFull - rTip) / tanA
        Height = ConeHeight + Diameter          ' cylinder part is one diameter long

        ' Side profile (r, z) from the tip centre to the top centre.
        Dim profile As New List(Of Tuple(Of Double, Double))
        profile.Add(Tuple.Create(0.0, 0.0))
        If rTip > 0 Then profile.Add(Tuple.Create(rTip, 0.0))
        profile.Add(Tuple.Create(rFull, ConeHeight))
        profile.Add(Tuple.Create(rFull, Height))
        profile.Add(Tuple.Create(0.0, Height))

        Dim n As Integer = Math.Max(8, segments)
        Dim verts As New List(Of Single)(profile.Count * n * 36)
        For i = 0 To profile.Count - 2
            Dim r0 = profile(i).Item1, z0 = profile(i).Item2
            Dim r1 = profile(i + 1).Item1, z1 = profile(i + 1).Item2
            ' Outward normal of this profile edge in the (r, z) plane.
            Dim dr = r1 - r0, dz = z1 - z0
            Dim len = Math.Sqrt(dr * dr + dz * dz)
            If len < 0.000000001 Then Continue For
            Dim nr = dz / len, nz = -dr / len
            If i = profile.Count - 2 Then
                nr = 0 : nz = 1                     ' top cap faces up
            ElseIf r0 = 0 AndAlso z0 = 0 AndAlso rTip > 0 Then
                nr = 0 : nz = -1                    ' tip flat faces down
            End If
            For k = 0 To n - 1
                Dim a0 = 2 * Math.PI * k / n
                Dim a1 = 2 * Math.PI * (k + 1) / n
                Dim c0 = Math.Cos(a0), s0 = Math.Sin(a0), c1 = Math.Cos(a1), s1 = Math.Sin(a1)
                ' Quad (r0,a0)-(r1,a0)-(r1,a1)-(r0,a1) as two triangles, degenerate ones skipped.
                Dim p00 = {r0 * c0, r0 * s0, z0}, p10 = {r1 * c0, r1 * s0, z1}
                Dim p11 = {r1 * c1, r1 * s1, z1}, p01 = {r0 * c1, r0 * s1, z0}
                Dim n00 = {nr * c0, nr * s0, nz}, n01 = {nr * c1, nr * s1, nz}
                If r1 > 0 Then
                    AddTri(verts, p00, n00, p10, n00, p11, n01)
                End If
                If r0 > 0 Then
                    AddTri(verts, p00, n00, p11, n01, p01, n01)
                End If
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
