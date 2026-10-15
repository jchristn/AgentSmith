@echo off
REM Fails when any tracked text file contains an em-dash (U+2014). See WRITING_DOCUMENTS.md.
powershell -NoProfile -Command "$em=[char]0x2014; $bad=git ls-files | Where-Object { (Test-Path $_) -and -not (Get-Item $_).PSIsContainer } | Where-Object { (Get-Content -Raw -ErrorAction SilentlyContinue $_) -match $em }; if ($bad) { $bad; Write-Error 'Em-dash found'; exit 1 } else { 'No em-dashes found.' }"
