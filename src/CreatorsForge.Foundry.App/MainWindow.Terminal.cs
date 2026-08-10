using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public partial class MainWindow
{
    private const int MaximumTerminalCharacters = 250_000;
    private readonly IntegratedTerminalSession terminalSession = new();
    private readonly TerminalCommandHistory terminalHistory = new();

    private void InitializeTerminal()
    {
        terminalSession.OutputReceived += TerminalSession_OutputReceived;
        terminalSession.StateChanged += TerminalSession_StateChanged;
        TerminalOutput.Text =
            "Creators Forge Foundry integrated PowerShell terminal.\n" +
            "Commands run only when you enter them and are never elevated by Foundry.\n";
        UpdateTerminalPresentation();
    }

    private async void StartTerminal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureTerminalStartedAsync(lifetimeCancellation.Token);
            TerminalInput.Focus();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppendTerminalLine($"[Foundry] Terminal could not start: {exception.Message}");
        }
    }

    private async void StopTerminal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await terminalSession.StopAsync(lifetimeCancellation.Token);
            AppendTerminalLine("[Foundry] Terminal stopped.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppendTerminalLine($"[Foundry] Terminal could not stop cleanly: {exception.Message}");
        }
    }

    private async void RestartTerminal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await terminalSession.StopAsync(lifetimeCancellation.Token);
            await EnsureTerminalStartedAsync(lifetimeCancellation.Token);
            AppendTerminalLine("[Foundry] Terminal restarted.");
            TerminalInput.Focus();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppendTerminalLine($"[Foundry] Terminal could not restart: {exception.Message}");
        }
    }

    private void ClearTerminal_Click(object sender, RoutedEventArgs e)
    {
        TerminalOutput.Clear();
        AppendTerminalLine("[Foundry] Terminal output cleared.");
    }

    private void ToggleTerminal_Click(object sender, RoutedEventArgs e)
    {
        BottomTabs.SelectedItem = TerminalTab;
        TerminalInput.Focus();
    }

    private async void TerminalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var command = TerminalInput.Text.Trim();
            if (command.Length == 0)
            {
                return;
            }

            TerminalInput.Clear();
            terminalHistory.Record(command);

            try
            {
                await EnsureTerminalStartedAsync(lifetimeCancellation.Token);
                AppendTerminalLine($"PS {terminalSession.WorkingDirectory}> {command}");
                await terminalSession.SendCommandAsync(command, lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppendTerminalLine($"[Foundry] Command failed: {exception.Message}");
            }
        }
        else if (e.Key == Key.Up)
        {
            var command = terminalHistory.Previous();
            if (command is not null)
            {
                e.Handled = true;
                SetTerminalHistoryEntry(command);
            }
        }
        else if (e.Key == Key.Down)
        {
            var command = terminalHistory.Next();
            if (command is not null)
            {
                e.Handled = true;
                SetTerminalHistoryEntry(command);
            }
        }
    }

    private async Task EnsureTerminalStartedAsync(CancellationToken cancellationToken)
    {
        var root = GetTerminalWorkingDirectory();
        var alreadyRunningHere = terminalSession.IsRunning &&
            string.Equals(
                terminalSession.WorkingDirectory,
                root,
                StringComparison.OrdinalIgnoreCase);
        await terminalSession.StartAsync(root, cancellationToken);
        if (!alreadyRunningHere)
        {
            AppendTerminalLine($"[Foundry] PowerShell started in {root}");
        }
        UpdateTerminalPresentation();
    }

    private string GetTerminalWorkingDirectory()
    {
        if (viewModel.Workspace is not null)
        {
            return viewModel.Workspace.ProjectRoot;
        }

        if (Directory.Exists(viewModel.Settings.DefaultProjectDirectory))
        {
            return viewModel.Settings.DefaultProjectDirectory;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents)
            ? documents
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private void TerminalWorkspaceChanged()
    {
        if (!terminalSession.IsRunning)
        {
            Dispatcher.BeginInvoke(UpdateTerminalPresentation);
            return;
        }

        Dispatcher.BeginInvoke(
            async () =>
            {
                try
                {
                    await terminalSession.StopAsync(lifetimeCancellation.Token);
                    AppendTerminalLine(
                        "[Foundry] Terminal stopped because the active project changed. " +
                        "Run a command to start in the new project root.");
                }
                catch (OperationCanceledException)
                {
                }
            },
            DispatcherPriority.Background);
    }

    private async Task StopTerminalForShutdownAsync()
    {
        if (!terminalSession.IsRunning)
        {
            return;
        }

        try
        {
            await terminalSession.StopAsync(CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException)
        {
            AppendTerminalLine($"[Foundry] Terminal shutdown warning: {exception.Message}");
        }
    }

    private void TerminalSession_OutputReceived(object? sender, TerminalOutputEventArgs e) =>
        Dispatcher.BeginInvoke(
            () => AppendTerminalLine(e.IsError ? $"ERROR: {e.Text}" : e.Text),
            DispatcherPriority.Background);

    private void TerminalSession_StateChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(UpdateTerminalPresentation, DispatcherPriority.Background);

    private void UpdateTerminalPresentation()
    {
        var isRunning = terminalSession.IsRunning;
        TerminalStatus.Text = isRunning ? "RUNNING" : "STOPPED";
        TerminalWorkingDirectory.Text = terminalSession.WorkingDirectory ??
            GetTerminalWorkingDirectory();
        StartTerminalButton.IsEnabled = !isRunning;
        StopTerminalButton.IsEnabled = isRunning;
        RestartTerminalButton.IsEnabled = isRunning;
    }

    private void AppendTerminalLine(string text)
    {
        TerminalOutput.AppendText(text + Environment.NewLine);
        if (TerminalOutput.Text.Length > MaximumTerminalCharacters)
        {
            var removeCount = TerminalOutput.Text.Length - MaximumTerminalCharacters;
            var lineBoundary = TerminalOutput.Text.IndexOf('\n', removeCount);
            TerminalOutput.Select(
                0,
                lineBoundary >= 0 ? lineBoundary + 1 : removeCount);
            TerminalOutput.SelectedText = string.Empty;
        }
        TerminalOutput.ScrollToEnd();
    }

    private void SetTerminalHistoryEntry(string command)
    {
        TerminalInput.Text = command;
        TerminalInput.CaretIndex = TerminalInput.Text.Length;
    }
}
