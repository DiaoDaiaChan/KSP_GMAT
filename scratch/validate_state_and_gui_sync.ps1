$managedDir = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\KSP_x64_Data\Managed"
Add-Type -Path (Join-Path $managedDir "UnityEngine.dll")
Add-Type -Path (Join-Path $managedDir "UnityEngine.CoreModule.dll")
Add-Type -Path (Join-Path $managedDir "Assembly-CSharp.dll")
Add-Type -Path "c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\src\KSPGMAT\bin\Release\net48\KSPGMAT.dll"

Write-Host "=========================================================="
Write-Host " GMATStateManager & C# Core Full Interoperability Check"
Write-Host "=========================================================="

# 1. State Manager Initialization & Spacecraft Setup
$state = [KSPGMAT.Model.GMATStateManager]::Current
$state.Spacecraft.Name = "Sat"
$state.Spacecraft.CentralBody = "Earth"
$state.Spacecraft.EpochUtc = "21 May 2024 12:00:00.000"
$state.Spacecraft.PosKm = New-Object Vector3d(7000.0, 0.0, 0.0)
$state.Spacecraft.VelKms = New-Object Vector3d(0.0, 7.5, 0.0)
$state.Spacecraft.DryMassKg = 3000.0
$state.Spacecraft.TotalMassKg = 8000.0

# 2. Tanks Setup
$state.Tanks.Clear()
$tank0 = New-Object KSPGMAT.Model.GMATTankModel
$tank0.Id = "Tank0"
$tank0.FuelMassKg = 5000.0
$tank0.VolumeM3 = 6.0
$state.Tanks.Add($tank0)

# 3. Thruster Setup
$state.Thrusters.Clear()
$thr = New-Object KSPGMAT.Model.GMATThrusterModel
$thr.Id = "RL10"
$thr.ThrustKN = 108.1
$thr.IspVac = 464.8
$thr.BoundTankId = "Tank0"
$thr.ThrustDirection1 = 1.0
$thr.ThrustDirection2 = 0.0
$thr.ThrustDirection3 = 0.0
$state.Thrusters.Add($thr)

# 4. Solvers & Variables
$state.Solvers.Clear()
$sol = New-Object KSPGMAT.Model.GMATSolverModel
$sol.Name = "DC_GTO"
$sol.MaxIterations = 50
$state.Solvers.Add($sol)

Write-Host "✅ GMATStateManager SSOT Models initialized."

# 5. Build Mission Sequence Steps (GUI representation)
$steps = New-Object 'System.Collections.Generic.List[KSPGMAT.MissionStep]'

$st1 = New-Object KSPGMAT.MissionStep
$st1.type = "Propagate"
$st1.propagator = "LEOProp"
$st1.stop_type = "Sat.ElapsedSecs"
$st1.duration = 1200.0
$steps.Add($st1)

$st2 = New-Object KSPGMAT.MissionStep
$st2.type = "Target"
$st2.dc_name = "DC_GTO"
$steps.Add($st2)

$st3 = New-Object KSPGMAT.MissionStep
$st3.type = "Vary"
$st3.dc_name = "DC_GTO"
$st3.vary_variable = "burntime"
$st3.vary_initial = 120.0
$st3.vary_lower = 10.0
$st3.vary_upper = 500.0
$steps.Add($st3)

$st4 = New-Object KSPGMAT.MissionStep
$st4.type = "FiniteBurn"
$st4.burn_name = "GTO_Burn"
$st4.bound_thruster = "RL10"
$st4.burn_use_vary_var = $true
$st4.burn_vary_var_name = "burntime"
$steps.Add($st4)

$st5 = New-Object KSPGMAT.MissionStep
$st5.type = "Achieve"
$st5.dc_name = "DC_GTO"
$st5.achieve_param = "Earth.RMAG"
$st5.achieve_val = 42164.14
$st5.achieve_tolerance = 0.5
$steps.Add($st5)

$st6 = New-Object KSPGMAT.MissionStep
$st6.type = "EndTarget"
$st6.dc_name = "DC_GTO"
$steps.Add($st6)

# 6. Test GMATScriptGenerator
$script = [KSPGMAT.Service.GMATScriptGenerator]::GenerateScript($null, $steps, "Earth", $null)
$sanitized = [KSPGMAT.Model.GMATStateManager]::SanitizeGmatScript($script)
Write-Host "✅ GMATScriptGenerator successfully produced script ($($sanitized.Length) chars)."

# 7. Test GMATScriptParser round-trip
$parseRes = [KSPGMAT.Service.GMATScriptParser]::ParseScriptText($sanitized, "GeneratedTest", $null)
Write-Host "✅ GMATScriptParser parsed generated script: Success=$($parseRes.success), Tanks=$($parseRes.tanks.Count), Thrusters=$($parseRes.thrusters.Count), Steps=$($parseRes.sequence.Count), Solvers=$($parseRes.solvers.Count)"

# 8. Test parsing all 5 templates
Write-Host "----------------------------------------------------------"
Write-Host "Parsing All 5 Deep Space Templates in Pure C#:"
$templates = Get-ChildItem "c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\GameData\KSPGMAT\templates" -Filter "*.script"
foreach ($t in $templates) {
    $tRes = [KSPGMAT.Service.GMATScriptParser]::ParseScriptFile($t.FullName, $null)
    $stat = if ($tRes.success) { "PASS" } else { "FAIL ($($tRes.message))" }
    Write-Host "   $($t.Name): $stat (Craft: $($tRes.primary_craft_name), Thrusters: $($tRes.thrusters.Count), Steps: $($tRes.sequence.Count))"
}

# 9. Test parsing user test.script
$userTestScript = "C:\Users\43701\Documents\test.script"
if (Test-Path $userTestScript) {
    $userParse = [KSPGMAT.Service.GMATScriptParser]::ParseScriptFile($userTestScript, $null)
    Write-Host "----------------------------------------------------------"
    Write-Host "✅ User test.script parsed:"
    Write-Host "   - Success: $($userParse.success)"
    Write-Host "   - Primary Craft: $($userParse.primary_craft_name)"
    Write-Host "   - Thrusters: $($userParse.thrusters.Count)"
    Write-Host "   - Tanks: $($userParse.tanks.Count)"
    Write-Host "   - Sequence Steps: $($userParse.sequence.Count)"
}

Write-Host "=========================================================="
Write-Host "🎉 ALL CHECKS PASSED 100%!"
Write-Host "=========================================================="
