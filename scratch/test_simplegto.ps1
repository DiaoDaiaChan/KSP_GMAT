$managedDir = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\KSP_x64_Data\Managed'
Add-Type -Path (Join-Path $managedDir 'UnityEngine.dll')
Add-Type -Path (Join-Path $managedDir 'UnityEngine.CoreModule.dll')
Add-Type -Path (Join-Path $managedDir 'Assembly-CSharp.dll')
Add-Type -Path 'c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\src\KSPGMAT\bin\Release\net48\KSPGMAT.dll'

$importRes = [KSPGMAT.Service.GMATScriptParser]::ParseScriptFile('C:\Users\43701\Documents\simplegto.script', [Vessel]$null)
$state = [KSPGMAT.Model.GMATStateManager]::Current
$state.Spacecraft.Name = 'Sat'
$state.Spacecraft.EpochUtc = $importRes.craft_epoch
$state.Spacecraft.DryMassKg = $importRes.craft_dry_mass
$state.Spacecraft.TotalMassKg = 7822.0
$state.Spacecraft.PosKm = New-Object Vector3d($importRes.craft_x, $importRes.craft_y, $importRes.craft_z)
$state.Spacecraft.VelKms = New-Object Vector3d($importRes.craft_vx, $importRes.craft_vy, $importRes.craft_vz)
$state.Tanks = $importRes.tanks
$state.Thrusters = $importRes.thrusters

$importRes = [KSPGMAT.Service.GMATScriptParser]::ParseScriptFile("C:\Users\43701\Documents\simplegto.script", $null)
Write-Host ">>> PARSED STEPS COUNT:" $importRes.sequence.Count

# Generate script
$script = [KSPGMAT.Service.GMATScriptGenerator]::GenerateScript($null, $importRes.sequence)
$scriptPath = "c:\Users\43701\gmat-win-R2026a\output\test_simplegto_gen.script"
$script | Set-Content $scriptPath

Write-Host ">>> RUNNING IN GMAT..."
& "c:\Users\43701\gmat-win-R2026a\bin\GmatConsole.exe" -r $scriptPath

Write-Host ">>> READING TrajReport.txt..."
$trajLines = Get-Content "c:\Users\43701\gmat-win-R2026a\output\TrajReport.txt"
foreach ($l in $trajLines) {
    Write-Host "TRAJ ROW:" $l
}

$mapped = [KSPGMAT.Service.GMATSolutionMapper]::ParseAndMapResult("", "", 0, "c:\Users\43701\gmat-win-R2026a\output\TrajReport.txt", "c:\Users\43701\gmat-win-R2026a\output", $importRes.sequence, $script)
Write-Host ">>> SOLVED BURNS COUNT:" $mapped.solved_burns.Count
foreach ($b in $mapped.solved_burns) {
    Write-Host ("Burn: {0} | EpochOffset: {1}s ({2}min) | Dur: {3}s" -f $b.name, $b.epoch_offset, ($b.epoch_offset / 60.0), $b.duration)
}
