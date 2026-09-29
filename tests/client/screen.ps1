param($Context)

$adapterActor = $Context.Actor.id
function Invoke-AdapterAction([string]$action) {
    $reply = (Invoke-LabServer "/hsmadapterprobe $adapterActor $action") -join "`n"
    if ($reply -notmatch 'ADAPTER_ACTION_OK') { throw "Adapter action failed: $reply" }
}
Invoke-AdapterAction 'screen'
$null = Invoke-LabInput @{id='adapter-screen-layout';frames=180;capture=$true}
$null = Invoke-LabScreenshot -Name 'adapter-screen-layout'
Invoke-AdapterAction 'screenraw'
$null = Invoke-LabInput @{id='adapter-screen-raw-replacement';frames=90;capture=$true}
Invoke-AdapterAction 'screentimed'
$null = Invoke-LabInput @{id='adapter-screen-before-renewal';frames=180;capture=$true}
Invoke-AdapterAction 'screentimed'
$null = Invoke-LabInput @{id='adapter-screen-renewal';frames=360;capture=$true}
$null = Invoke-LabInput @{id='adapter-screen-expiry';frames=180;capture=$true}
Invoke-AdapterAction 'dispose'
$null = Invoke-LabInput @{id='adapter-screen-dispose';frames=90;capture=$true}
$null = Invoke-LabScreenshot -Name 'adapter-screen-dispose'
Invoke-AdapterAction 'clear'
