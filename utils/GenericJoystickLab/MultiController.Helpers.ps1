function Get-MultiControllerReadingErrors($Probe, [object[]]$Expected, $Baseline, [switch]$EnumerationOnly) {
    if ($Probe.error) { 'Native probe reported an API error.' }
    foreach ($api in @('directInput','winMM')) {
        $readings = @($Probe.$api)
        if ($readings.Count -ne $Expected.Count) { "$api found $($readings.Count) devices; expected $($Expected.Count)."; continue }
        $ids = @($readings | ForEach-Object { [string]$_.instance })
        if (@($ids | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0 -or @($ids | Select-Object -Unique).Count -ne $readings.Count) {
            "$api did not report distinct instance identifiers."
        }
        foreach ($expectedDevice in $Expected) {
            $matchingReadings = @($readings | Where-Object {
                $single = [pscustomobject]@{ error=$false; directInput=@($_); winMM=@($_) }
                @(Get-LabReadingErrors $single $expectedDevice.state).Count -eq 0
            })
            if ($EnumerationOnly) {
                if ($matchingReadings.Count -ne $readings.Count) { "$api did not enumerate every joystick at its neutral state." }
                continue
            }
            if ($matchingReadings.Count -ne 1) { "$api cannot uniquely match controller $($expectedDevice.name) by its input pattern."; continue }
            # DirectInput instance identity must survive re-creation. WinMM slot
            # numbers are recorded but are allowed to change when devices reorder.
            if ($api -eq 'directInput') {
                $key = $expectedDevice.name
                if ($Baseline.ContainsKey($key) -and $Baseline[$key] -cne [string]$matchingReadings[0].instance) { "DirectInput identity changed for controller $key." }
            }
        }
    }
}
