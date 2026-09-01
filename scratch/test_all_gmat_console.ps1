$gmatConsole = 'c:\Users\43701\gmat-win-R2026a\bin\GmatConsole.exe'
$templates = Get-ChildItem 'c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\templates' -Filter '*.script'

Write-Host "========================================"
Write-Host " NASA GmatConsole.exe Native Regression"
Write-Host "========================================"

foreach ($t in $templates) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $gmatConsole -ArgumentList @('--run', "`"$($t.FullName)`"", '--exit') -WorkingDirectory 'c:\Users\43701\gmat-win-R2026a\bin' -NoNewWindow -Wait -PassThru
    $sw.Stop()
    $status = if ($p.ExitCode -eq 0) { "PASS" } else { "FAIL ($($p.ExitCode))" }
    Write-Host "$($t.Name): $status in $($sw.ElapsedMilliseconds) ms"
}

Write-Host "----------------------------------------"
Write-Host "Testing test.script (User Custom Target Mission):"
$testScript = 'C:\Users\43701\Documents\test.script'
if (Test-Path $testScript) {
    $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
    $p2 = Start-Process -FilePath $gmatConsole -ArgumentList @('--run', "`"$testScript`"", '--exit') -WorkingDirectory 'c:\Users\43701\gmat-win-R2026a\bin' -NoNewWindow -Wait -PassThru
    $sw2.Stop()
    $status2 = if ($p2.ExitCode -eq 0) { "PASS" } else { "FAIL ($($p2.ExitCode))" }
    Write-Host "test.script: $status2 in $($sw2.ElapsedMilliseconds) ms"
}
