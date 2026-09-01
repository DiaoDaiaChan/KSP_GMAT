import subprocess

ps_content = r"""
$kspManaged = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\KSP_x64_Data\Managed'
Get-ChildItem $kspManaged -Filter '*.dll' | ForEach-Object { try { [Reflection.Assembly]::LoadFile($_.FullName) | Out-Null } catch {} }
[Reflection.Assembly]::LoadFile('C:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\src\KSPGMAT\bin\Release\net48\KSPGMAT.dll') | Out-Null

Write-Host "=== Testing GMAT Screenshot Cold-Parameter Coverage ===" -ForegroundColor Cyan
$queries = @("Incoming", "ModEquinoctial", "Equinoctial", "Delaunay", "Brouwer", "LST", "MHA", "MLONG", "MM", "Bdot")
foreach ($q in $queries) {
    $items = [KSPGMAT.GMATParameterRegistry]::QueryIntelliSense("Sat.Earth.$q", $null, "achieve")
    Write-Host "Query '$q' matched $($items.Count) parameters:"
    foreach ($it in $items) {
        Write-Host "  • $($it.InsertText) [$($it.TypeTag)]: $($it.Doc)"
    }
}
"""

with open("scratch/test_cold.ps1", "w", encoding="utf-8") as f:
    f.write(ps_content)

res = subprocess.run(["powershell", "-ExecutionPolicy", "Bypass", "-File", "scratch/test_cold.ps1"], capture_output=True, text=True, encoding="gbk", errors="replace")
print("STDOUT:\n", res.stdout)
if res.stderr:
    print("STDERR:\n", res.stderr)
