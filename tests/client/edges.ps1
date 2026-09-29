param($Context)

$adapterActor = $Context.Actor.id
$reply = (Invoke-LabServer "/hsmadapterprobe $adapterActor edgeprobe") -join "`n"
if ($reply -notmatch 'ADAPTER_ACTION_OK') { throw "Edge probe failed: $reply" }
$null = Invoke-LabInput @{id='adapter-edge-probe';frames=180;capture=$true}
$null = Invoke-LabScreenshot -Name 'adapter-edge-probe'
$reply = (Invoke-LabServer "/hsmadapterprobe $adapterActor dispose") -join "`n"
if ($reply -notmatch 'ADAPTER_ACTION_OK') { throw "Edge cleanup failed: $reply" }
$null = Invoke-LabInput @{id='adapter-edge-probe-cleanup';frames=60;capture=$true}
