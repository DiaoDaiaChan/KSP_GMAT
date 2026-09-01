$managedDir = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\KSP_x64_Data\Managed'
Add-Type -Path (Join-Path $managedDir 'UnityEngine.dll')
Add-Type -Path (Join-Path $managedDir 'UnityEngine.CoreModule.dll')
Add-Type -Path (Join-Path $managedDir 'Assembly-CSharp.dll')
Add-Type -Path 'c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\src\KSPGMAT\bin\Release\net48\KSPGMAT.dll'

$parseRes = [KSPGMAT.Service.GMATScriptParser]::ParseScriptFile('C:\Users\43701\Documents\test.script', [Vessel]$null)
$state = [KSPGMAT.Model.GMATStateManager]::Current
$state.Spacecraft.Name = 'Sat'
$state.Spacecraft.EpochUtc = $parseRes.craft_epoch
$state.Spacecraft.DryMassKg = $parseRes.craft_dry_mass
$state.Spacecraft.TotalMassKg = 7822.0
$state.Spacecraft.PosKm = New-Object Vector3d($parseRes.craft_x, $parseRes.craft_y, $parseRes.craft_z)
$state.Spacecraft.VelKms = New-Object Vector3d($parseRes.craft_vx, $parseRes.craft_vy, $parseRes.craft_vz)
$state.Tanks = $parseRes.tanks
$state.Thrusters = $parseRes.thrusters

# Synchronously test the runner parsing logic
$gmatRootDir = 'C:\Users\43701\gmat-win-R2026a'
$outputDir = Join-Path $gmatRootDir 'output'
$scriptFile = Join-Path $outputDir 'mission_run.script'
$reportFile = Join-Path $outputDir 'TrajReport.txt'
[System.IO.File]::WriteAllText($scriptFile, $genScript, [System.Text.UTF8Encoding]::new($false))

$p = Start-Process -FilePath (Join-Path $gmatRootDir 'bin\GmatConsole.exe') -ArgumentList @('--run', "`"$scriptFile`"", '--exit') -WorkingDirectory (Join-Path $gmatRootDir 'bin') -NoNewWindow -Wait -PassThru -RedirectStandardOutput (Join-Path $outputDir 'stdout.log') -RedirectStandardError (Join-Path $outputDir 'stderr.log')

$stdout = [System.IO.File]::ReadAllText((Join-Path $outputDir 'stdout.log'))
$stderr = [System.IO.File]::ReadAllText((Join-Path $outputDir 'stderr.log'))

$res = [KSPGMAT.Service.GMATSolutionMapper]::ParseAndMapResult($stdout, $stderr, $p.ExitCode, $reportFile, $outputDir, $parseRes.sequence, $genScript)

Write-Host ">>> PARSE SUCCESS:" $res.success
foreach ($b in $res.solved_burns) {
    Write-Host ("Burn: " + $b.name + " | Dur: " + [string]::Format('{0:F2}', $b.duration) + "s | Thrust: " + $b.thrust_kn + "kN | Isp: " + $b.isp + "s | Dv: " + [string]::Format('{0:F2}', $b.magnitude) + "m/s (V=" + [string]::Format('{0:F2}', $b.dv_v) + ", N=" + [string]::Format('{0:F2}', $b.dv_n) + ", B=" + [string]::Format('{0:F2}', $b.dv_b) + ")")
}
Write-Host ">>> SEQUENCE CALLBACK SYNC:"
foreach ($s in $res.updated_sequence) {
    if ($s.type -eq 'Vary') {
        Write-Host ("  Vary " + $s.vary_variable + " -> Solved Value: " + [string]::Format('{0:F2}', $s.vary_initial))
    } elseif ($s.type -eq 'FiniteBurn') {
        Write-Host ("  FiniteBurn " + $s.burn_name + " -> Solved Duration: " + [string]::Format('{0:F2}', $s.duration) + "s")
    } elseif ($s.type -eq 'ThrustVectorControl') {
        Write-Host ("  TVC " + $s.bound_thruster + " -> Solved Dirs: (" + [string]::Format('{0:F2}', $s.thrust_dir1) + ", " + [string]::Format('{0:F2}', $s.thrust_dir2) + ", " + [string]::Format('{0:F2}', $s.thrust_dir3) + ")")
    }
}

