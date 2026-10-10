' ============================================================================
'  SettingsView.vb
'  One CarveSettings object, two property grids: the Text tab shows only the
'  text settings, the Machine Settings window everything else. A SettingsView
'  hands a PropertyGrid a filtered list of the real property descriptors and
'  names the settings object as their owner, so edits land on the one shared
'  object and both grids raise the same PropertyValueChanged notifications.
' ============================================================================

Imports System.ComponentModel

''' <summary>A filtered view of a CarveSettings object for a PropertyGrid.</summary>
Public Class SettingsView
    Implements ICustomTypeDescriptor

    Private ReadOnly _settings As CarveSettings
    Private ReadOnly _include As Func(Of PropertyDescriptor, Boolean)

    Public Sub New(settings As CarveSettings, include As Func(Of PropertyDescriptor, Boolean))
        _settings = settings
        _include = include
    End Sub

    ''' <summary>The settings object being shown.</summary>
    Public ReadOnly Property Settings As CarveSettings
        Get
            Return _settings
        End Get
    End Property

    Public Function GetProperties(attributes As Attribute()) As PropertyDescriptorCollection Implements ICustomTypeDescriptor.GetProperties
        Dim all = TypeDescriptor.GetProperties(_settings, attributes, True)
        Return New PropertyDescriptorCollection(all.Cast(Of PropertyDescriptor)().Where(_include).ToArray())
    End Function

    Public Function GetProperties() As PropertyDescriptorCollection Implements ICustomTypeDescriptor.GetProperties
        Return GetProperties(Nothing)
    End Function

    Public Function GetPropertyOwner(pd As PropertyDescriptor) As Object Implements ICustomTypeDescriptor.GetPropertyOwner
        Return _settings
    End Function

    Public Function GetAttributes() As AttributeCollection Implements ICustomTypeDescriptor.GetAttributes
        Return TypeDescriptor.GetAttributes(_settings, True)
    End Function

    Public Function GetClassName() As String Implements ICustomTypeDescriptor.GetClassName
        Return TypeDescriptor.GetClassName(_settings, True)
    End Function

    Public Function GetComponentName() As String Implements ICustomTypeDescriptor.GetComponentName
        Return TypeDescriptor.GetComponentName(_settings, True)
    End Function

    Public Function GetConverter() As TypeConverter Implements ICustomTypeDescriptor.GetConverter
        Return TypeDescriptor.GetConverter(_settings, True)
    End Function

    Public Function GetDefaultEvent() As EventDescriptor Implements ICustomTypeDescriptor.GetDefaultEvent
        Return TypeDescriptor.GetDefaultEvent(_settings, True)
    End Function

    Public Function GetDefaultProperty() As PropertyDescriptor Implements ICustomTypeDescriptor.GetDefaultProperty
        Return Nothing
    End Function

    Public Function GetEditor(editorBaseType As Type) As Object Implements ICustomTypeDescriptor.GetEditor
        Return TypeDescriptor.GetEditor(_settings, editorBaseType, True)
    End Function

    Public Function GetEvents() As EventDescriptorCollection Implements ICustomTypeDescriptor.GetEvents
        Return TypeDescriptor.GetEvents(_settings, True)
    End Function

    Public Function GetEvents(attributes As Attribute()) As EventDescriptorCollection Implements ICustomTypeDescriptor.GetEvents
        Return TypeDescriptor.GetEvents(_settings, attributes, True)
    End Function
End Class

''' <summary>Which settings belong to the Text tab; everything else is a machine setting.</summary>
Public Module SettingsGroups
    ''' <summary>Settings shown on the Text tab.</summary>
    Public ReadOnly TextPropertyNames As New HashSet(Of String)(StringComparer.Ordinal) From {
        NameOf(CarveSettings.SizeLargeIn), NameOf(CarveSettings.FontLarge),
        NameOf(CarveSettings.SizeMediumIn), NameOf(CarveSettings.FontMedium),
        NameOf(CarveSettings.SizeSmallIn), NameOf(CarveSettings.FontSmall),
        NameOf(CarveSettings.SizeBy), NameOf(CarveSettings.LineSpacing),
        NameOf(CarveSettings.VerticalPlacement),
        NameOf(CarveSettings.TextOffsetX), NameOf(CarveSettings.TextOffsetY),
        NameOf(CarveSettings.TextMachining)}

    Public Function TextView(s As CarveSettings) As SettingsView
        Return New SettingsView(s, Function(p) TextPropertyNames.Contains(p.Name))
    End Function

    Public Function MachineView(s As CarveSettings) As SettingsView
        Return New SettingsView(s, Function(p) Not TextPropertyNames.Contains(p.Name))
    End Function
End Module
