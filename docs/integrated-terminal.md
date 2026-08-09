# Phase 23 integrated terminal

Phase 23 embeds a PowerShell terminal in Foundry's resizable bottom tool area.
Open it through **View > Integrated Terminal** or press **Ctrl+Backtick**.

## Runtime boundary

Foundry starts Windows PowerShell as a separate, non-elevated process with
redirected standard input, output, and error streams. Foundry does not run a
shell command until the creator submits it, does not silently alter the Windows
environment, and does not request administrator rights. Commands retain normal
PowerShell capabilities and should be reviewed before they are entered.

The terminal starts in the active project root. Changing the active project
stops the old session so a command cannot accidentally continue in another
project. **Stop**, application shutdown, and failure cleanup terminate the
PowerShell process tree. Visible output is capped at 250,000 characters.

## Phase 23 manual acceptance

1. Launch the Phase 23 desktop and open any disposable Foundry sample project.
2. Choose **View > Integrated Terminal** and confirm the Terminal tab is
   selected; repeat with **Ctrl+Backtick**.
3. Confirm the displayed working directory is the active project's root.
4. Enter `Write-Output "Foundry terminal ready"` and confirm the text appears.
5. Enter `Get-Location` and confirm it reports the same project root.
6. Enter two harmless commands, then use Up and Down to navigate command
   history.
7. Choose **Clear** and confirm only the output view is cleared.
8. Choose **Stop** and confirm the status becomes **STOPPED**; enter another
   command and confirm the session starts again.
9. Choose **Restart** and confirm a fresh session starts in the project root.
10. Open or activate another disposable project and confirm the old session is
    stopped and the next command starts in the new project root.
11. Run a longer harmless command such as
    `Start-Sleep -Seconds 30; Write-Output "unexpected"`, choose **Stop**, and
    confirm Foundry remains responsive and `unexpected` is not printed.
12. Start the terminal, close Foundry, and confirm the desktop closes without a
    remaining `powershell.exe` process owned by that terminal session.
13. Repeat the visual checks in Dark, Light, and System themes and confirm the
    terminal output, command input, status, working directory, and buttons are
    readable.

Phase 23 exits after the complete automated regression gate, desktop smoke
tests, and all thirteen manual checks pass.
