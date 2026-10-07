param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\bin\Release\SolidDNA.dll')
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw "FAILED: $Message"
    }
    Write-Host "PASS: $Message"
}

$resolvedAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$assemblyDirectory = Split-Path -Parent $resolvedAssembly
$handler = [ResolveEventHandler]{
    param($sender, $eventArgs)
    $simpleName = ([Reflection.AssemblyName]$eventArgs.Name).Name + '.dll'
    $candidate = Join-Path $assemblyDirectory $simpleName
    if (Test-Path -LiteralPath $candidate) {
        return [Reflection.Assembly]::LoadFrom($candidate)
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($handler)

try {
    $assembly = [Reflection.Assembly]::LoadFrom($resolvedAssembly)
    $defaultsType = $assembly.GetType('SolidDNA.PropertyPaneDefaults', $true)
    $createDefaults = $defaultsType.GetMethod('Create',
        [Reflection.BindingFlags]'Public,Static')
    $profile = $createDefaults.Invoke($null, @())

    Assert-True ($profile.SchemaVersion -eq 2) 'Built-in profile uses schema 2.'
    Assert-True ($profile.Layouts.Count -eq 3) 'Built-in profile contains Part, Assembly, and Drawing layouts.'
    Assert-True ($profile.Dimensions.Count -eq 4) 'Built-in profile contains the four practical part dimensions.'
    Assert-True ($profile.PropertyCatalog.Count -ge 55) 'Built-in standard property catalog is populated.'

    $resolverType = $assembly.GetType('SolidDNA.DrawingNumberResolver', $true)
    $tryResolve = $resolverType.GetMethod('TryResolve',
        [Reflection.BindingFlags]'Public,Static')

    $arguments = @($profile, 'Cabin Compact (1) A', 'M', 'Room Layout', $null, $null)
    $matched = [bool]$tryResolve.Invoke($null, $arguments)
    Assert-True $matched 'Known drawing-number mapping resolves.'
    Assert-True ($arguments[4] -eq 'D.520.0053.408.562') 'Known mapping returns the expected drawing number.'

    $arguments = @($profile, 'Not a cabin', 'X', 'Room Layout', $null, $null)
    $matched = [bool]$tryResolve.Invoke($null, $arguments)
    Assert-True (-not $matched) 'Unknown drawing-number mapping is rejected.'
    Assert-True ([string]$arguments[5] -match 'No drawing-number mapping') 'No-match reason is actionable.'

    $mapType = $assembly.GetType('SolidDNA.DrawingNumberMap', $true)
    foreach ($number in @('TEST-001', 'TEST-002')) {
        $map = [Activator]::CreateInstance($mapType)
        $map.CabinDescription = 'Ambiguous cabin'
        $map.CabinDefined = 'A'
        $map.LayoutType = 'Room Layout'
        $map.DrawingNumber = $number
        [void]$profile.DrawingNumberMappings.Add($map)
    }
    $arguments = @($profile, 'Ambiguous cabin', 'A', 'Room Layout', $null, $null)
    $matched = [bool]$tryResolve.Invoke($null, $arguments)
    Assert-True (-not $matched) 'Conflicting drawing-number mappings are rejected.'
    Assert-True ([string]$arguments[5] -match 'ambiguous') 'Ambiguity reason is reported.'

    $profileType = $assembly.GetType('SolidDNA.PropertyPaneProfile', $true)
    $serializer = [System.Xml.Serialization.XmlSerializer]::new($profileType)
    $stream = [IO.MemoryStream]::new()
    try {
        $serializer.Serialize($stream, $profile)
        $stream.Position = 0
        $roundTrip = $serializer.Deserialize($stream)
        Assert-True ($roundTrip.SchemaVersion -eq 2) 'Profile XML round-trip preserves the schema.'
        Assert-True ($roundTrip.Layouts.Count -eq 3) 'Profile XML round-trip preserves layouts.'
        Assert-True ($roundTrip.Dimensions.Count -eq 4) 'Profile XML round-trip preserves dimension mappings.'
    }
    finally {
        $stream.Dispose()
    }

    $settingsType = $assembly.GetType('SolidDNA.PropertyPaneSettingsService', $true)
    $settings = [Activator]::CreateInstance($settingsType, $true)
    $cloneMethod = $settingsType.GetMethod('Clone',
        [Reflection.BindingFlags]'Public,Instance')
    $clone = $cloneMethod.Invoke($settings, @($profile))
    $clone.Layouts[0].Groups[0].Label = 'Changed only in clone'
    Assert-True ($profile.Layouts[0].Groups[0].Label -ne $clone.Layouts[0].Groups[0].Label) 'Settings editing uses a deep clone.'

    $validateMethod = $settingsType.GetMethod('Validate',
        [Reflection.BindingFlags]'NonPublic,Static')
    $invalid = $cloneMethod.Invoke($settings, @($profile))
    $firstGroup = $invalid.Layouts[0].Groups[0]
    $firstGroup.Fields[1].Id = $firstGroup.Fields[0].Id
    $rejected = $false
    try {
        $validateMethod.Invoke($null, @($invalid))
    }
    catch {
        $exception = $_.Exception
        while ($null -ne $exception.InnerException) {
            $exception = $exception.InnerException
        }
        $rejected = $exception -is [IO.InvalidDataException]
    }
    Assert-True $rejected 'Profile validation rejects duplicate stable field IDs.'

    Write-Host 'All property-pane logic tests passed.' -ForegroundColor Green
}
finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($handler)
}
