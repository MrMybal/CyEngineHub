param([string]$Root = (Join-Path $PSScriptRoot '../../src/CyEngineHub.Desktop'))
$ErrorActionPreference = 'Stop'
# Mechanical, reproducible export of advanced forms. These descriptions are drawn
# entirely by Dear ImGui, not instantiated as Avalonia controls.
$kept = @('IsVisible','IsEnabled','DataContext','Text','Content','Header','Command','Click','IsChecked','IsReadOnly','AcceptsReturn','IsExpanded','ItemsSource','SelectedItem','SelectedIndex','DisplayMemberBinding','PlaceholderText','Value','Maximum','IsIndeterminate','ColumnDefinitions','RowDefinitions','Grid.Column','Grid.Row','Grid.ColumnSpan','Orientation','Width','Height','MinHeight','MaxHeight','MaxWidth','FontSize','Foreground','TextWrapping','TextTrimming','Classes','ToolTip.Tip','Panel.ZIndex','PointerReleased','AttachedToVisualTree','Source','Flow')
function Convert-Node([System.Xml.XmlElement]$Source, [System.Xml.XmlDocument]$Target) {
    if ($Source.LocalName -match '\.(Styles|Resources|Effect|ItemsPanel|RenderTransform)$' -or $Source.GetAttribute('IsVisible') -eq 'False') { return $null }
    $node = $Target.CreateElement($Source.LocalName)
    foreach ($attribute in $Source.Attributes) {
        if ($attribute.NamespaceURI -eq '' -and $kept -contains $attribute.Name) { $node.SetAttribute($attribute.Name, $attribute.Value) }
    }
    foreach ($child in $Source.ChildNodes) {
        if ($child -isnot [System.Xml.XmlElement]) { continue }
        if ($child.LocalName -match '\.ItemsPanel$' -and $child.InnerXml -match 'WrapPanel') { $node.SetAttribute('Flow','Wrap') }
        $converted = Convert-Node $child $Target
        if ($null -ne $converted) { [void]$node.AppendChild($converted) }
    }
    return ,$node
}
$output = Join-Path $Root 'ImGui/Layouts'
$kept += @('Minimum','Increment','FormatString','Name')
New-Item -ItemType Directory -Path $output -Force | Out-Null
$names = @{Main='MainWindow'; Extensions='ExtensionHubWindow'; Collections='QuixelCollectionsPanel'; Connector='ConnectorLibraryPanel'; FabSignIn='FabSignInPanel'}
foreach ($entry in $names.GetEnumerator()) {
    $source = [System.Xml.XmlDocument]::new()
    $source.Load((Join-Path $Root ('Views/' + $entry.Value + '.axaml')))
    $target = [System.Xml.XmlDocument]::new()
    $interface = $target.CreateElement('Interface')
    $interface.SetAttribute('Name', $entry.Key)
    [void]$target.AppendChild($interface)
    foreach ($child in $source.DocumentElement.ChildNodes) {
        if ($child -isnot [System.Xml.XmlElement]) { continue }
        $converted = Convert-Node $child $target
        if ($null -ne $converted) { [void]$interface.AppendChild($converted) }
    }
    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Indent = $true
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $writer = [System.Xml.XmlWriter]::Create((Join-Path $output ($entry.Key + '.xml')), $settings)
    try { $target.Save($writer) } finally { $writer.Dispose() }
}
