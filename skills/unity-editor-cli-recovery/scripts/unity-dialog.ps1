# Lists the Unity Editor's visible windows (a modal dialog blocks `unity command`) and, with -Click, presses a
# dialog button by its label. Usage:
#   powershell -File unity-dialog.ps1                                    # every Editor: windows and dialog buttons
#   powershell -File unity-dialog.ps1 -Project E:/Projects/MyGame        # only that project's Editor
#   powershell -File unity-dialog.ps1 -Project MyGame -Click Reload      # press "Reload" on that Editor's dialog
# -Project takes the project folder's path or name. -Click refuses to run when several Editors are open and no
# -Project is given, so a button is never pressed in the wrong Editor.
param([string]$Project, [string]$Click)

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class UnityDialog {
    public delegate bool Proc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(Proc p, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, Proc p, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
"@

function Text([IntPtr]$h) { $s = New-Object Text.StringBuilder 512; [void][UnityDialog]::GetWindowText($h, $s, 512); $s.ToString() }
function Class([IntPtr]$h) { $s = New-Object Text.StringBuilder 64; [void][UnityDialog]::GetClassName($h, $s, 64); $s.ToString() }

$unityPids = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object { [uint32]$_.Id })
if ($unityPids.Count -eq 0) { "No Unity process running."; exit 1 }

# Visible top-level windows, grouped by Unity process.
$byPid = @{}
[void][UnityDialog]::EnumWindows({ param($h, $l)
    $p = [uint32]0; [void][UnityDialog]::GetWindowThreadProcessId($h, [ref]$p)
    if ($unityPids -contains $p -and [UnityDialog]::IsWindowVisible($h)) {
        if (-not $byPid.ContainsKey($p)) { $byPid[$p] = New-Object System.Collections.ArrayList }
        [void]$byPid[$p].Add($h)
    }
    $true }, [IntPtr]::Zero)

# The Editor's main window title starts with the project path: "E:\Projects\MyGame - Scene - Platform - Unity 6.x".
function ProjectOf($windows) {
    foreach ($w in $windows) { $t = Text $w; if ($t -match '^(.+?) - .* - Unity ') { return $Matches[1] } }
    return $null
}
function Matches-Project($path) {
    if (-not $Project) { return $true }
    if (-not $path) { return $false }
    $want = $Project.Replace('/', '\').TrimEnd('\')
    return ($path.TrimEnd('\') -ieq $want) -or ((Split-Path $path -Leaf) -ieq (Split-Path $want -Leaf))
}

$editors = @($byPid.Keys | Where-Object { Matches-Project (ProjectOf $byPid[$_]) })
if ($editors.Count -eq 0) {
    if ($Project) { "No open Unity Editor for project '$Project'." } else { "No visible Unity windows." }
    exit 1
}
if ($Click -and -not $Project -and $editors.Count -gt 1) {
    "Several Unity Editors are open; pass -Project <path or name> with -Click:"
    foreach ($p in $editors) { "    pid $p  $(ProjectOf $byPid[$p])" }
    exit 1
}

$clicked = $false
foreach ($p in $editors) {
    "pid $p  project: $(ProjectOf $byPid[$p])"
    foreach ($w in $byPid[$p]) {
        "  [$w] $(Text $w)"
        $buttons = New-Object System.Collections.ArrayList
        [void][UnityDialog]::EnumChildWindows($w, { param($h, $l) if ((Class $h) -eq "Button") { [void]$buttons.Add($h) }; $true }, [IntPtr]::Zero)
        foreach ($b in $buttons) {
            $label = (Text $b) -replace "&", ""
            "      button: $label"
            if ($Click -and -not $clicked -and $label -eq $Click) {
                [void][UnityDialog]::SendMessage($b, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) # BM_CLICK
                "      -> clicked"
                $clicked = $true
            }
        }
    }
}
if ($Click -and -not $clicked) { "No button labelled '$Click' found."; exit 1 }
