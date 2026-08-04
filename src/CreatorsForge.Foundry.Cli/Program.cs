namespace CreatorsForge.Foundry.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;

        try
        {
            return await FoundryCli.RunAsync(
                args,
                Console.Out,
                Console.Error,
                cancellationToken: cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Foundry operation cancelled.");
            return FoundryCli.CancelledExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
