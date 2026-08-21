using AutoJMS.DataHub.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AutoJMS.DataHub.Api.Services;

public interface IDoorbellPublisher
{
    Task PublishAsync(IReadOnlyList<Infrastructure.ChangeDoorbell> doorbells, CancellationToken cancellationToken);
}

public sealed class SignalRDoorbellPublisher(IHubContext<SiteHub> hub) : IDoorbellPublisher
{
    public async Task PublishAsync(IReadOnlyList<Infrastructure.ChangeDoorbell> doorbells, CancellationToken cancellationToken)
    {
        foreach (var doorbell in doorbells)
        {
            await hub.Clients.Group(SiteHub.GroupName(doorbell.SiteId))
                .SendAsync("change", doorbell, cancellationToken);
        }
    }
}
