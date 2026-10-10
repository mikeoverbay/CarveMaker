' ============================================================================
'  SettingsView.vb
'  What each property grid shows, without copying any data:
'   - Text tab:          the text settings plus the text's toolpath rows
'   - Machine Settings:  every other job setting
'   - Drawings tab:      the selected drawing plus its toolpath rows
'  A FilteredView hands a PropertyGrid a chosen list of the real property
'  descriptors and names the real object as their owner, so edits land on the
'  shared objects and the form's change handlers see the usual property names.
'  Toolpath rows (Machining) are shown flat in their own category; which rows
'  appear follows the chosen operation (Machining.VisibleProperties).
' ============================================================================

Imports System.ComponentModel

''' <summary>A filtered, flattened view of an object (and optionally its Machining) for a PropertyGrid.</summary>
Public Class FilteredView
    Implements ICustomTypeDescriptor

    Private ReadOnly _target As Object
    Private ReadOnly _include As Func(Of PropertyDescriptor, Boolean)
    Private ReadOnly _machining As Func(Of Machining)
    Private ReadOnly _machiningCategory As String

    ''' <param name="machining">The toolpath settings to show flat (Nothing for none).</param>
    ''' <param name="machiningCategory">Category the toolpath rows appear under.</param>
    Public Sub New(target As Object, include As Func(Of PropertyDescriptor, Boolean),
                   Optional machining As Func(Of Machining) = Nothing, Optional machiningCategory As String = "Toolpath")
        _target = target
        _include = include
        _machining = machining
        _machiningCategory = machiningCategory
    End Sub

    ''' <summary>The object being shown.</summary>
    Public ReadOnly Property Target As Object
        Get
            Return _target
        End Get
    End Property

    Public Function GetProperties(attributes As Attribute()) As PropertyDescriptorCollection Implements ICustomTypeDescriptor.GetProperties
        Dim list = TypeDescriptor.GetProperties(_target, attributes, True).Cast(Of PropertyDescriptor)().Where(_include).ToList()
        Dim m = _machining?.Invoke()
        If m IsNot Nothing Then
            For Each p In Machining.VisibleProperties(m, attributes)
                list.Add(TypeDescriptor.CreateProperty(GetType(Machining), p, New CategoryAttribute(_machiningCategory)))
            Next
        End If
        Return New PropertyDescriptorCollection(list.ToArray())
    End Function

    Public Function GetProperties() As PropertyDescriptorCollection Implements ICustomTypeDescriptor.GetProperties
        Return GetProperties(Nothing)
    End Function

    ''' <summary>Toolpath rows belong to the Machining object, everything else to the target.</summary>
    Public Function GetPropertyOwner(pd As PropertyDescriptor) As Object Implements ICustomTypeDescriptor.GetPropertyOwner
        If pd IsNot Nothing AndAlso pd.ComponentType Is GetType(Machining) AndAlso _machining IsNot Nothing Then Return _machining()
        Return _target
    End Function

    Public Function GetAttributes() As AttributeCollection Implements ICustomTypeDescriptor.GetAttributes
        Return TypeDescriptor.GetAttributes(_target, True)
    End Function

    Public Function GetClassName() As String Implements ICustomTypeDescriptor.GetClassName
        Return TypeDescriptor.GetClassName(_target, True)
    End Function

    Public Function GetComponentName() As String Implements ICustomTypeDescriptor.GetComponentName
        Return TypeDescriptor.GetComponentName(_target, True)
    End Function

    Public Function GetConverter() As TypeConverter Implements ICustomTypeDescriptor.GetConverter
        Return TypeDescriptor.GetConverter(_target, True)
    End Function

    Public Function GetDefaultEvent() As EventDescriptor Implements ICustomTypeDescriptor.GetDefaultEvent
        Return TypeDescriptor.GetDefaultEvent(_target, True)
    End Function

    Public Function GetDefaultProperty() As PropertyDescriptor Implements ICustomTypeDescriptor.GetDefaultProperty
        Return Nothing
    End Function

    Public Function GetEditor(editorBaseType As Type) As Object Implements ICustomTypeDescriptor.GetEditor
        Return TypeDescriptor.GetEditor(_target, editorBaseType, True)
    End Function

    Public Function GetEvents() As EventDescriptorCollection Implements ICustomTypeDescriptor.GetEvents
        Return TypeDescriptor.GetEvents(_target, True)
    End Function

    Public Function GetEvents(attributes As Attribute()) As EventDescriptorCollection Implements ICustomTypeDescriptor.GetEvents
        Return TypeDescriptor.GetEvents(_target, attributes, True)
    End Function
End Class

''' <summary>A view of the job settings (Text tab or Machine Settings window).</summary>
Public Class SettingsView
    Inherits FilteredView

    Private ReadOnly _settings As CarveSettings

    Public Sub New(settings As CarveSettings, include As Func(Of PropertyDescriptor, Boolean), Optional withTextToolpath As Boolean = False)
        MyBase.New(settings, include, If(withTextToolpath, Function() settings.TextMachining, Nothing), SettingsGroups.TextToolpathCategory)
        _settings = settings
    End Sub

    ''' <summary>The settings object being shown.</summary>
    Public ReadOnly Property Settings As CarveSettings
        Get
            Return _settings
        End Get
    End Property
End Class

''' <summary>A view of one drawing: its object and placement rows plus its toolpath rows.</summary>
Public Class DrawingView
    Inherits FilteredView

    Private ReadOnly _drawing As DesignObject

    Public Sub New(drawing As DesignObject)
        MyBase.New(drawing, Function(p) p.Name <> NameOf(DesignObject.Machining), Function() drawing.Machining, "Toolpath")
        _drawing = drawing
    End Sub

    ''' <summary>The drawing being shown.</summary>
    Public ReadOnly Property Drawing As DesignObject
        Get
            Return _drawing
        End Get
    End Property
End Class

''' <summary>Which settings belong to the Text tab; everything else is a machine setting.</summary>
Public Module SettingsGroups
    ''' <summary>Category of the text's toolpath rows on the Text tab.</summary>
    Public Const TextToolpathCategory As String = "3. Toolpath"

    ''' <summary>Job settings shown on the Text tab (the text's toolpath rows come on top of these).</summary>
    Public ReadOnly TextPropertyNames As New HashSet(Of String)(StringComparer.Ordinal) From {
        NameOf(CarveSettings.SizeLargeIn), NameOf(CarveSettings.FontLarge),
        NameOf(CarveSettings.SizeMediumIn), NameOf(CarveSettings.FontMedium),
        NameOf(CarveSettings.SizeSmallIn), NameOf(CarveSettings.FontSmall),
        NameOf(CarveSettings.SizeBy), NameOf(CarveSettings.LineSpacing),
        NameOf(CarveSettings.VerticalPlacement),
        NameOf(CarveSettings.TextOffsetX), NameOf(CarveSettings.TextOffsetY)}

    Public Function TextView(s As CarveSettings) As SettingsView
        Return New SettingsView(s, Function(p) TextPropertyNames.Contains(p.Name), withTextToolpath:=True)
    End Function

    Public Function MachineView(s As CarveSettings) As SettingsView
        Return New SettingsView(s, Function(p) Not TextPropertyNames.Contains(p.Name))
    End Function
End Module
