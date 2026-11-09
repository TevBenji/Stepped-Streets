# Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
# This loads managed types only; it creates no systems and never starts the game.
param(
    [string]$ManagedPath = 'D:/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Managed',
    [string]$AssemblyPath = "$PSScriptRoot/../../SteppedStreets/bin/Release/SteppedStreets.dll"
)
$ErrorActionPreference = 'Stop'
$assemblyFile = (Resolve-Path -LiteralPath $AssemblyPath).Path
$outputDirectory = Split-Path -Parent $assemblyFile
$resolver = [System.ResolveEventHandler]{
    param($sender, $arguments)
    $name = ([Reflection.AssemblyName]::new($arguments.Name)).Name + '.dll'
    foreach ($directory in @($outputDirectory, $ManagedPath)) {
        $candidate = Join-Path $directory $name
        if (Test-Path -LiteralPath $candidate) { return [Reflection.Assembly]::LoadFrom($candidate) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $assembly = [Reflection.Assembly]::LoadFrom($assemblyFile)
    $panel = $assembly.GetType('SteppedStreets.Panel.PanelSystem', $true)
    $flags = [Reflection.BindingFlags]'Static,NonPublic'
    $stringWriter = $panel.GetMethod('String', $flags)
    [string[]]$samples = @('', 'Hill "Street"\North', ("line`r`n`t" + [char]1), 'Straße 台北 🏘️', ([string][char]0x2028))
    foreach ($sample in $samples) {
        $builder = [Text.StringBuilder]::new()
        $null = $stringWriter.Invoke($null, @($builder, $sample))
        $roundTrip = ConvertFrom-Json -InputObject $builder.ToString()
        if ($roundTrip -cne $sample) { throw 'Panel JSON text did not round-trip.' }
    }
    $previousCulture = [Globalization.CultureInfo]::CurrentCulture
    try {
        [Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('fr-FR')
        $numberWriter = $panel.GetMethod('Number', $flags)
        foreach ($pair in @(@([single]1.5, '1.5'), @([single]::NaN, 'null'), @([single]::PositiveInfinity, 'null'))) {
            if ($numberWriter.Invoke($null, @($pair[0])) -cne $pair[1]) { throw 'Panel JSON number was invalid.' }
        }
    }
    finally { [Globalization.CultureInfo]::CurrentCulture = $previousCulture }
    'Panel JSON self-check: 5 string cases and 3 numeric cases passed.'

    # Moving or folding the panel must not make a ready plan stale; a real setting still must.
    $settingsType = $assembly.GetType('SteppedStreets.Settings.SteppedStreetsSettings', $true)
    $signature = $assembly.GetType('SteppedStreets.Tool.NeighbourhoodToolSystem', $true).GetMethod('SettingsSignature', $flags)
    $settings = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($settingsType)
    $before = $signature.Invoke($null, @($settings))
    $settings.PanelLeft = 0.5; $settings.PanelTop = 0.5; $settings.PanelCollapsed = $true
    if ($signature.Invoke($null, @($settings)) -cne $before) { throw 'The panel fold or place changed the plan signature.' }
    $settings.BlockDepth = 120
    if ($signature.Invoke($null, @($settings)) -ceq $before) { throw 'A real setting did not change the plan signature.' }
    'Plan signature self-check: the panel leaves a plan ready and a setting does not.'
}
finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
