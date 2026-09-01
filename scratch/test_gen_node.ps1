$kspManaged = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\KSP_x64_Data\Managed"
[System.Reflection.Assembly]::LoadFrom((Join-Path $kspManaged "UnityEngine.dll")) | Out-Null
[System.Reflection.Assembly]::LoadFrom((Join-Path $kspManaged "UnityEngine.CoreModule.dll")) | Out-Null
[System.Reflection.Assembly]::LoadFrom((Join-Path $kspManaged "Assembly-CSharp.dll")) | Out-Null
$dllPath = "c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\src\KSPGMAT\bin\Release\net48\KSPGMAT.dll"
[System.Reflection.Assembly]::LoadFrom($dllPath) | Out-Null

$step1 = New-Object KSPGMAT.MissionStep
$step1.type = 'Propagate'
$step1.propagator = 'LEOProp'
$step1.stop_type = 'Sat.Earth.Node'

$step2 = New-Object KSPGMAT.MissionStep
$step2.type = 'Propagate'
$step2.propagator = 'LEOProp'
$step2.stop_type = 'Sat.Earth.Apoapsis'

$step3 = New-Object KSPGMAT.MissionStep
$step3.type = 'Propagate'
$step3.propagator = 'LEOProp'
$step3.stop_type = 'Sat.Periapsis'

$list = New-Object 'System.Collections.Generic.List[KSPGMAT.MissionStep]'
$list.Add($step1)
$list.Add($step2)
$list.Add($step3)

$script = [KSPGMAT.Service.GMATScriptGenerator]::GenerateScript($null, $list)
Write-Host ">>> GENERATED SCRIPT SNIPPET:"
$lines = $script -split "`r?`n"
foreach ($l in $lines) {
    if ($l -like "*Propagate*") {
        Write-Host $l
    }
}
