' ============================================================================
'  frmMachineSettings.vb
'  Machine Settings window (Machine > Machine Settings, Ctrl+K): blank and
'  stock, V-carve tool, carve, machine and simulation settings. A tool window
'  owned by the main form: it floats over it, stays open while you work, and
'  closing it only hides it. The main form binds the grid and handles its
'  changes, exactly like the Text tab's grid.
' ============================================================================

Public Class frmMachineSettings
    Private _placed As Boolean

    ''' <summary>The settings grid (the main form binds and listens to it).</summary>
    Public ReadOnly Property Grid As PropertyGrid
        Get
            Return pgMachine
        End Get
    End Property

    ''' <summary>Shows the window (first time: along the right edge of the owner) and brings it forward.</summary>
    Public Sub ShowNear(owner As Form)
        ' First time, or when the remembered spot is no longer on any screen (monitor unplugged).
        Dim onScreen = Screen.AllScreens.Any(Function(sc) sc.WorkingArea.IntersectsWith(Bounds))
        If owner IsNot Nothing AndAlso (Not _placed OrElse Not onScreen) Then
            Dim area = Screen.FromControl(owner).WorkingArea
            Dim y = Math.Max(area.Top, owner.Top + 90)
            Dim bottom = Math.Min(owner.Bottom, area.Bottom)
            Height = Math.Max(MinimumSize.Height, Math.Min(Height, bottom - y - 40))
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - Height))
            Dim x = Math.Max(area.Left, Math.Min(owner.Right - Width - 24, area.Right - Width))
            Location = New Point(x, y)
            _placed = True
        End If
        If Not Visible Then Show() Else Activate()
        If WindowState = FormWindowState.Minimized Then WindowState = FormWindowState.Normal
    End Sub

    ''' <summary>
    ''' Menu shortcuts (F5, Ctrl+S, Ctrl+G, ...) belong to the main window's menu, which only
    ''' sees keys while focus is inside the main window. Pass them on from here too.
    ''' </summary>
    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If MyBase.ProcessCmdKey(msg, keyData) Then Return True
        Dim item = FindShortcut(Owner?.MainMenuStrip?.Items, keyData)
        If item Is Nothing Then Return False
        item.PerformClick()
        Return True
    End Function

    Private Shared Function FindShortcut(items As ToolStripItemCollection, keyData As Keys) As ToolStripMenuItem
        If items Is Nothing Then Return Nothing
        For Each it In items.OfType(Of ToolStripMenuItem)()
            If it.Enabled AndAlso it.ShortcutKeys = keyData AndAlso keyData <> Keys.None Then Return it
            Dim inner = FindShortcut(it.DropDownItems, keyData)
            If inner IsNot Nothing Then Return inner
        Next
        Return Nothing
    End Function

    Private Sub frmMachineSettings_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        ' Closing by the user only hides the window; it closes for real with the main form.
        If e.CloseReason = CloseReason.UserClosing Then
            e.Cancel = True
            Hide()
        End If
    End Sub

End Class
