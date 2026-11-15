# Gives PowerShell a Commodore 64 style prompt: READY. on its own line, the cursor below it.
# Load it for this session:      . .\ready.ps1
# Or for every session:          Add-Content $PROFILE ". '$PWD\ready.ps1'"
# The working directory is shown in the window title instead of the prompt.

function global:prompt {
    $Host.UI.RawUI.WindowTitle = "PS $($executionContext.SessionState.Path.CurrentLocation)"
    # An empty line before READY., like the C64 after a command's output.
    "`nREADY.`n"
}
