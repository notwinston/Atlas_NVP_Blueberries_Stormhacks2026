# ATLAS Station firewall rules (Master Plan Appendix C.7).
#
# Run once, as administrator:
#     powershell -ExecutionPolicy Bypass -File station\setup_firewall.ps1
#
# Opens the three inbound ports the phones need on the Station:
#     UDP 9050   Hub: the LiteNetLib link (phone, Spectators, Editors)
#     TCP 8080   Hub: Console, pairing page, WebSocket
#     TCP 8000   Forge: RoomModel uploads and GLB downloads
# Running it again replaces the rules instead of adding copies.

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Run this script from an elevated PowerShell (Run as administrator)."
    exit 1
}

$rules = @(
    @{ Name = "ATLAS Hub UDP";  Protocol = "UDP"; Port = 9050 },
    @{ Name = "ATLAS Hub HTTP"; Protocol = "TCP"; Port = 8080 },
    @{ Name = "ATLAS Forge";    Protocol = "TCP"; Port = 8000 }
)

foreach ($rule in $rules) {
    # Deleting a rule that does not exist is not an error worth stopping for.
    netsh advfirewall firewall delete rule name="$($rule.Name)" | Out-Null

    netsh advfirewall firewall add rule name="$($rule.Name)" dir=in action=allow protocol=$($rule.Protocol) localport=$($rule.Port)
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Could not add the rule '$($rule.Name)'."
        exit 1
    }
}

Write-Host ""
Write-Host "Done. Check that the Station's network profile is Private (Settings > Network & internet),"
Write-Host "then open http://<station address>:8080/pair from a phone on the same router."
