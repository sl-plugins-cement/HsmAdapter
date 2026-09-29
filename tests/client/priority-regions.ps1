param($Context)
$ErrorActionPreference = 'Stop'
$noticeActor = $Context.Actor.id

function Invoke-NoticeAction([string]$action) {
    $reply = (Invoke-LabServer "/hsmadapterprobe $noticeActor $action") -join "`n"
    if ($reply -notmatch 'ADAPTER_ACTION_OK') { throw "Notice action failed: $reply" }
}
function Assert-NoticeState([string]$expected, [int]$actorId = $noticeActor) {
    $reply = (Invoke-LabServer "/hsmadapterprobe $actorId priority-check") -join "`n"
    if (-not $reply.Contains("NOTICE_STATE $expected")) { throw "Expected '$expected', received: $reply" }
}
function Capture-Notice([string]$name, [int]$frames = 90) {
    Invoke-LabInput @{id=$name;frames=$frames;capture=$true} | Out-Null
    Invoke-LabScreenshot -Name $name | Out-Null
}

# Exercise the public adapter contract through two independently owned scopes.
Invoke-NoticeAction 'priority-start'
Assert-NoticeState 'high=Visible low=Visible tie=Queued timed='
Capture-Notice 'priority-order'

if ($Context.Actors.Count -gt 1) {
    $peerActor = $Context.Actors[1].id
    Assert-NoticeState 'high= low= tie= timed=' $peerActor
    Invoke-LabScreenshot -Name 'priority-other-player' -ClientId $Context.ClientIds[1] | Out-Null
}

Invoke-NoticeAction 'priority-refresh'
Assert-NoticeState 'high=Visible low=Visible tie=Queued timed='
Capture-Notice 'priority-stable-refresh'

Invoke-NoticeAction 'priority-cover'
Assert-NoticeState 'high=Queued low=Queued tie=Queued timed=Queued'
Capture-Notice 'priority-reservation' 210
Assert-NoticeState 'high=Queued low=Queued tie=Queued timed='

Invoke-NoticeAction 'priority-release'
Assert-NoticeState 'high=Visible low=Visible tie=Queued timed='
Capture-Notice 'priority-reservation-released'

Invoke-NoticeAction 'priority-high'
Assert-NoticeState 'high= low=Visible tie=Visible timed='
Capture-Notice 'priority-queue-promoted'

Invoke-NoticeAction 'priority-clear'
Assert-NoticeState 'high= low= tie=Visible timed='
Capture-Notice 'priority-scope-cleared'

Invoke-NoticeAction 'priority-dispose'
Assert-NoticeState 'high= low= tie= timed='
Capture-Notice 'priority-disposed'
