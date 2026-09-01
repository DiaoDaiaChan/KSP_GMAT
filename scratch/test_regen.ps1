$managedDir = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\KSP_x64_Data\Managed'
Add-Type -Path (Join-Path $managedDir 'UnityEngine.dll')
Add-Type -Path (Join-Path $managedDir 'UnityEngine.CoreModule.dll')
Add-Type -Path (Join-Path $managedDir 'Assembly-CSharp.dll')
Add-Type -Path 'c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\src\KSPGMAT\bin\Release\net48\KSPGMAT.dll'

$userTestScript = 'C:\Users\43701\Documents\test.script'
$parseRes = [KSPGMAT.Service.GMATScriptParser]::ParseScriptFile($userTestScript, $null)

# Setup GMATStateManager as if imported
$state = [KSPGMAT.Model.GMATStateManager]::Current
$state.Spacecraft.Name = 'Sat3'
$state.Spacecraft.EpochUtc = $parseRes.craft_epoch
$state.Spacecraft.DryMassKg = $parseRes.craft_dry_mass
$state.Spacecraft.PosKm = New-Object Vector3d($parseRes.craft_x, $parseRes.craft_y, $parseRes.craft_z)
$state.Spacecraft.VelKms = New-Object Vector3d($parseRes.craft_vx, $parseRes.craft_vy, $parseRes.craft_vz)
$state.Tanks = $parseRes.tanks
$state.Thrusters = $parseRes.thrusters

$reGenScript = [KSPGMAT.Service.GMATScriptGenerator]::GenerateScript($null, $parseRes.sequence, 'Earth', $null)
$sanitized = [KSPGMAT.Model.GMATStateManager]::SanitizeGmatScript($reGenScript)

$testGenFile = 'c:\Users\43701\gmat-win-R2026a\output\test_regen.script'
[System.IO.File]::WriteAllText($testGenFile, $sanitized, [System.Text.UTF8Encoding]::new($false))

Write-Host "Generated Script Content:"
Write-Host "----------------------------------------------------"
Write-Host $sanitized
Write-Host "----------------------------------------------------"

Write-Host 'Running GmatConsole on test_regen.script...'
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath 'c:\Users\43701\gmat-win-R2026a\bin\GmatConsole.exe' -ArgumentList @('--run', "`"$testGenFile`"", '--exit') -WorkingDirectory 'c:\Users\43701\gmat-win-R2026a\bin' -NoNewWindow -Wait -PassThru
$sw.Stop()
Write-Host 'Exit Code:' $p.ExitCode 'Elapsed:' $sw.ElapsedMilliseconds 'ms'
