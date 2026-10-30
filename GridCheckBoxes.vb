' ============================================================================
'  GridCheckBoxes.vb
'  Yes/no rows of the property grids as plain check boxes instead of a
'  True / False list:
'   - CheckBoxEditor:    paints the box (dark, a lime tick when on)
'   - CheckBoxConverter: no words and no drop-down list next to it (nothing is
'                        typed in: Attach swallows the keys of these rows)
'   - GridCheckBoxes:    the attributes that make a row a check box
'                        (FilteredView adds them to every yes/no row), and
'                        Attach, which makes a click on the row's value or
'                        Space flip it through the grid's usual
'                        PropertyValueChanged, so the windows see an
'                        ordinary edit.
'  The grid hands every click on a row's value to its edit box (empty on
'  these rows), so that box's clicks and keys are watched.
' ============================================================================

Imports System.ComponentModel
Imports System.Drawing.Design
Imports System.Drawing.Drawing2D
Imports System.Reflection

''' <summary>Paints a yes/no value as a check box.</summary>
Public Class CheckBoxEditor
    Inherits UITypeEditor

    Public Overrides Function GetEditStyle(context As ITypeDescriptorContext) As UITypeEditorEditStyle
        Return UITypeEditorEditStyle.None
    End Function

    Public Overrides Function GetPaintValueSupported(context As ITypeDescriptorContext) As Boolean
        Return True
    End Function

    Public Overrides Sub PaintValue(e As PaintValueEventArgs)
        GridCheckBoxes.PaintBox(e.Graphics, e.Bounds, TypeOf e.Value Is Boolean AndAlso CBool(e.Value))
    End Sub
End Class

''' <summary>
''' A check box row shows no words and no list. It still takes text (the grid draws a row it cannot
''' type into greyed out, like a read-only one: free text needs values that are not exclusive), but
''' GridCheckBoxes swallows the keys, so nothing is typed: a click or Space flips it. Text other than
''' yes / no keeps the value.
''' </summary>
Public Class CheckBoxConverter
    Inherits BooleanConverter

    Public Overrides Function GetStandardValuesSupported(context As ITypeDescriptorContext) As Boolean
        Return False
    End Function

    Public Overrides Function GetStandardValuesExclusive(context As ITypeDescriptorContext) As Boolean
        Return False
    End Function

    Public Overrides Function ConvertTo(context As ITypeDescriptorContext, culture As Globalization.CultureInfo, value As Object, destinationType As Type) As Object
        If destinationType Is GetType(String) AndAlso TypeOf value Is Boolean Then Return ""
        Return MyBase.ConvertTo(context, culture, value, destinationType)
    End Function

    Public Overrides Function ConvertFrom(context As ITypeDescriptorContext, culture As Globalization.CultureInfo, value As Object) As Object
        Dim s = TryCast(value, String)
        If s IsNot Nothing Then
            Select Case s.Trim().ToLowerInvariant()
                Case "true", "yes", "on", "1" : Return True
                Case "false", "no", "off", "0" : Return False
            End Select
            ' The empty box (or anything else): the value as it is.
            If context?.PropertyDescriptor IsNot Nothing AndAlso context.Instance IsNot Nothing Then Return context.PropertyDescriptor.GetValue(context.Instance)
        End If
        Return MyBase.ConvertFrom(context, culture, value)
    End Function
End Class

''' <summary>Yes/no rows as check boxes in the property grids.</summary>
Public Module GridCheckBoxes

    ''' <summary>What makes a row a check box.</summary>
    Public ReadOnly Attributes As Attribute() = {New EditorAttribute(GetType(CheckBoxEditor), GetType(UITypeEditor)),
                                                 New TypeConverterAttribute(GetType(CheckBoxConverter))}

    ' The grid's own way of telling its listeners about an edit (protected).
    Private ReadOnly RaiseValueChanged As MethodInfo = GetType(PropertyGrid).GetMethod("OnPropertyValueChanged",
        BindingFlags.Instance Or BindingFlags.NonPublic, Nothing, {GetType(PropertyValueChangedEventArgs)}, Nothing)

    Private ReadOnly _attached As New Runtime.CompilerServices.ConditionalWeakTable(Of PropertyGrid, Object)

    ' Holding Space repeats its character: only the press itself flips. Each character's own message says
    ' whether it is a repeat (bit 30 of lParam: the key was already down), read just before the character is
    ' handled (so a backlog of key messages cannot mix them up); one filter for the whole program.
    Private _spaceRepeats As Boolean
    Private _spaceWatch As SpaceWatch

    Private NotInheritable Class SpaceWatch
        Implements IMessageFilter
        Public Function PreFilterMessage(ByRef m As Message) As Boolean Implements IMessageFilter.PreFilterMessage
            Const WM_CHAR As Integer = &H102
            If m.Msg = WM_CHAR AndAlso m.WParam.ToInt64() = 32 Then _spaceRepeats = (m.LParam.ToInt64() And &H40000000L) <> 0
            Return False
        End Function
    End Class

    ''' <summary>A yes/no row as a check box; any other row as it is.</summary>
    Public Function AsCheckBox(p As PropertyDescriptor, ParamArray more As Attribute()) As PropertyDescriptor
        If p.PropertyType IsNot GetType(Boolean) Then Return If(more.Length = 0, p, TypeDescriptor.CreateProperty(p.ComponentType, p, more))
        Return TypeDescriptor.CreateProperty(p.ComponentType, p, Attributes.Concat(more).ToArray())
    End Function

    ''' <summary>Makes the grid's check box rows flip on a click on their value, or on Space (once per grid).</summary>
    Public Sub Attach(grid As PropertyGrid)
        Dim seen As Object = Nothing
        If grid Is Nothing OrElse _attached.TryGetValue(grid, seen) Then Return
        _attached.Add(grid, Nothing)
        If _spaceWatch Is Nothing Then
            _spaceWatch = New SpaceWatch()
            Application.AddMessageFilter(_spaceWatch)
        End If
        Dim view = grid.Controls.Cast(Of Control)().FirstOrDefault(Function(c) c.GetType().Name = "PropertyGridView")
        If view Is Nothing Then Return
        ' The grid's view raises no KeyDown of its own; keys typed while it has the focus reach the edit
        ' box as characters (KeyPress), so the edit box sees every Space.
        For Each c As Control In view.Controls
            WatchEditBox(grid, c)
        Next
        ' The edit box is made when it is first needed.
        AddHandler view.ControlAdded, Sub(s, e) WatchEditBox(grid, e.Control)
    End Sub

    Private Sub WatchEditBox(grid As PropertyGrid, c As Control)
        If Not TypeOf c Is TextBox Then Return
        AddHandler c.MouseDown, Sub(s, e)
                                    ' Any click on the selected row's value (the box included) lands here.
                                    If e.Button = MouseButtons.Left Then FlipLater(grid, grid.SelectedGridItem)
                                End Sub
        ' Nothing is typed into a check box row: Space flips it (once per press, not again while it is
        ' held), other characters, pasting and deleting do nothing.
        AddHandler c.KeyPress, Sub(s, e)
                                   If Not IsCheckBox(grid.SelectedGridItem) Then Return
                                   e.Handled = True
                                   If e.KeyChar = " "c AndAlso Control.ModifierKeys = Keys.None AndAlso Not _spaceRepeats Then FlipLater(grid, grid.SelectedGridItem)
                               End Sub
        ' Text that arrives some other way (the right-click menu's Paste, an input method) is taken out
        ' again, so the grid has nothing to commit (it would report an edit that changes nothing).
        Dim box = DirectCast(c, TextBox)
        AddHandler box.TextChanged, Sub(s, e)
                                        If box.Modified AndAlso box.Text.Length > 0 AndAlso IsCheckBox(grid.SelectedGridItem) Then box.Text = ""
                                    End Sub
        AddHandler c.KeyDown, Sub(s, e)
                                  If Not IsCheckBox(grid.SelectedGridItem) Then Return
                                  Dim paste = (e.Control AndAlso e.KeyCode = Keys.V) OrElse (e.Shift AndAlso e.KeyCode = Keys.Insert)
                                  If paste OrElse e.KeyCode = Keys.Delete OrElse e.KeyCode = Keys.Back Then
                                      e.Handled = True
                                      e.SuppressKeyPress = True
                                  End If
                              End Sub
    End Sub

    ''' <summary>Flips the row once the grid has finished with the click or key (it may rebuild its rows).</summary>
    Private Sub FlipLater(grid As PropertyGrid, item As GridItem)
        If Not IsCheckBox(item) OrElse Not grid.IsHandleCreated Then Return
        grid.BeginInvoke(Sub()
                             ' Only while that row is still the one selected (not after the rows were rebuilt).
                             If grid.SelectedGridItem Is item Then Flip(grid, item)
                         End Sub)
    End Sub

    ''' <summary>True for a row shown as a check box that can be changed.</summary>
    Public Function IsCheckBox(item As GridItem) As Boolean
        If item Is Nothing OrElse item.GridItemType <> GridItemType.Property Then Return False
        Dim pd = item.PropertyDescriptor
        Return pd IsNot Nothing AndAlso Not pd.IsReadOnly AndAlso pd.PropertyType Is GetType(Boolean) AndAlso
               TypeOf pd.GetEditor(GetType(UITypeEditor)) Is CheckBoxEditor
    End Function

    ''' <summary>
    ''' Flips a check box row as if it had been edited in the grid: the value changes, the grid's
    ''' PropertyValueChanged is raised (with the old value), and the rows are refreshed.
    ''' </summary>
    Public Sub Flip(grid As PropertyGrid, item As GridItem)
        If grid.IsDisposed OrElse Not IsCheckBox(item) OrElse grid.SelectedObjects.Length > 1 Then Return
        Dim owner = OwnerOf(grid, item)
        If owner Is Nothing Then Return
        Dim pd = item.PropertyDescriptor
        Dim before = pd.GetValue(owner)
        Try
            pd.SetValue(owner, Not CBool(before))
        Catch ex As Exception
            ' As the grid does with a value it cannot take: say why, keep the old one.
            MessageBox.Show(grid.FindForm(), If(ex.InnerException, ex).Message, item.Label, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        Dim place = PlaceOf(item)
        RaiseValueChanged?.Invoke(grid, {New PropertyValueChangedEventArgs(item, before)})
        If grid.IsDisposed Then Return
        grid.Refresh()
        ' After a refresh the grid selects the first row of the same name, whatever its category: a 3D
        ' model's Toolpath 2 row would hand the next Space to the same row of Toolpath 1. Select it again.
        SelectPlace(grid, place)
    End Sub

    ''' <summary>Where a row is: the labels from the top of the grid down to it (its category included).</summary>
    Private Function PlaceOf(item As GridItem) As List(Of String)
        Dim place As New List(Of String)
        Dim g = item
        While g IsNot Nothing AndAlso g.GridItemType <> GridItemType.Root
            place.Insert(0, g.Label)
            g = g.Parent
        End While
        Return place
    End Function

    ''' <summary>Selects the row at that place again, when it is still there and not already selected.</summary>
    Private Sub SelectPlace(grid As PropertyGrid, place As List(Of String))
        Dim g = grid.SelectedGridItem
        If g Is Nothing OrElse place.Count = 0 Then Return
        While g.Parent IsNot Nothing
            g = g.Parent
        End While
        For Each label In place
            g = g.GridItems.Cast(Of GridItem)().FirstOrDefault(Function(x) x.Label = label)
            If g Is Nothing Then Return
        Next
        If grid.SelectedGridItem IsNot g Then g.Select()
    End Sub

    ''' <summary>The object a row edits: the row above's value for a row inside another, else the shown object's owner of it.</summary>
    Private Function OwnerOf(grid As PropertyGrid, item As GridItem) As Object
        Dim parent = item.Parent
        While parent IsNot Nothing AndAlso parent.GridItemType = GridItemType.Category
            parent = parent.Parent
        End While
        If parent IsNot Nothing AndAlso parent.GridItemType = GridItemType.Property Then Return parent.Value
        Dim shown = grid.SelectedObject
        Dim custom = TryCast(shown, ICustomTypeDescriptor)
        Return If(custom IsNot Nothing, custom.GetPropertyOwner(item.PropertyDescriptor), shown)
    End Function

    ''' <summary>A check box about the height of the rectangle: dark, a grey rim, and when on a lime rim and tick.</summary>
    Friend Sub PaintBox(g As Graphics, bounds As Rectangle, isOn As Boolean)
        Using back As New SolidBrush(Skin.Field)
            g.FillRectangle(back, bounds)
        End Using
        Dim side = Math.Max(7, Math.Min(bounds.Width, bounds.Height) - 3)
        Dim box As New RectangleF(bounds.X + (bounds.Width - side) / 2.0F, bounds.Y + (bounds.Height - side) / 2.0F, side - 1, side - 1)
        Dim smoothing = g.SmoothingMode
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using path = Skin.RoundRect(box, 2)
            Using fill As New SolidBrush(Color.FromArgb(8, 8, 9))
                g.FillPath(fill, path)
            End Using
            Using rim As New Pen(If(isOn, Skin.Led, Skin.InkDim))
                g.DrawPath(rim, path)
            End Using
        End Using
        If isOn Then
            Using tick As New Pen(Skin.LedLit, Math.Max(1.6F, side / 6.0F)) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                g.DrawLines(tick, {New PointF(box.X + box.Width * 0.22F, box.Y + box.Height * 0.52F),
                                   New PointF(box.X + box.Width * 0.43F, box.Y + box.Height * 0.74F),
                                   New PointF(box.X + box.Width * 0.8F, box.Y + box.Height * 0.27F)})
            End Using
        End If
        g.SmoothingMode = smoothing
    End Sub
End Module
