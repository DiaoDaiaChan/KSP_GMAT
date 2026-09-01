
$kspManaged = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\KSP_x64_Data\Managed'
Get-ChildItem $kspManaged -Filter '*.dll' | ForEach-Object { try { [Reflection.Assembly]::LoadFile($_.FullName) | Out-Null } catch {} }
[Reflection.Assembly]::LoadFile('C:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\src\KSPGMAT\bin\Release\net48\KSPGMAT.dll') | Out-Null

Write-Host "=== 1. Testing Achieve context mode (Strictly Continuous, NO Apoapsis) ===" -ForegroundColor Cyan
$achieveItems = [KSPGMAT.GMATParameterRegistry]::QueryIntelliSense('Sat.Earth.', $null, 'achieve')
foreach ($it in $achieveItems) {
    Write-Host "  - [$($it.TypeTag)] $($it.InsertText): $($it.Doc)"
}

Write-Host "`n=== 2. Testing Propagate Stop context mode (Contains Extremum events) ===" -ForegroundColor Cyan
$stopItems = [KSPGMAT.GMATParameterRegistry]::QueryIntelliSense('Sat.Earth.', $null, 'stop')
foreach ($it in $stopItems) {
    Write-Host "  - [$($it.TypeTag)] $($it.InsertText): $($it.Doc)"
}

Write-Host "`n=== 3. Testing Vary context mode (Thrust steering vectors & variables) ===" -ForegroundColor Cyan
$varyItems = [KSPGMAT.GMATParameterRegistry]::QueryIntelliSense('Sat.', $null, 'vary')
foreach ($it in $varyItems) {
    Write-Host "  - [$($it.TypeTag)] $($it.InsertText): $($it.Doc)"
}
