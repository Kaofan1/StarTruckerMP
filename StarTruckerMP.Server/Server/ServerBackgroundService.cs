using Microsoft.Extensions.Hosting;
using StarTruckerMP.Server.Server.Services;

namespace StarTruckerMP.Server.Server;

public class ServerBackgroundService(ServerManager serverManager) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _pollingTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        serverManager.Start();

        // Run the polling loop in a separate Task so StartAsync returns immediately,
        // allowing Kestrel and the rest of the host to finish starting up. The loop owns its
        // token, because the token passed to StartAsync is never cancelled on shutdown: waiting
        // for a loop that cannot end made the service sit in STOP_PENDING for the whole host
        // shutdown timeout.
        _pollingTask = Task.Run(async () =>
        {
            try
            {
                while (!_stopping.IsCancellationRequested)
                {
                    serverManager.Polling();
                    await Task.Delay(15, _stopping.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        serverManager.Stop();

        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The host gave up waiting, which is still a stopped server.
            }
        }

        _stopping.Dispose();
    }
}
