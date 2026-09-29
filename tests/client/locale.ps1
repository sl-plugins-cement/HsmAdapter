param($Context)

$adapterActor = $Context.Actor.id
$reply = (Invoke-LabServer "/hsmadapterprobe $adapterActor fractional") -join "`n"
if ($reply -notmatch 'ADAPTER_ACTION_OK') { throw "Fractional layout failed: $reply" }
$null = Invoke-LabInput @{id='adapter-comma-culture';frames=180;capture=$true}
$null = Invoke-LabScreenshot -Name 'adapter-comma-culture'
$reply = (Invoke-LabServer "/hsmadapterprobe $adapterActor dispose") -join "`n"
if ($reply -notmatch 'ADAPTER_ACTION_OK') { throw "Fractional layout disposal failed: $reply" }
$null = Invoke-LabInput @{id='adapter-comma-culture-cleanup';frames=60;capture=$true}
$null = Invoke-LabScreenshot -Name 'adapter-comma-culture-cleanup'
