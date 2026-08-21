using AutoJMS.DataHub.Api.Auth;
using Microsoft.AspNetCore.SignalR;

namespace AutoJMS.DataHub.Api.Hubs;

public sealed class SiteHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var identity = httpContext?.GetDeviceIdentity();
        if (identity is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(identity.SiteId));
        await base.OnConnectedAsync();
    }

    public static string GroupName(Guid siteId) => $"site:{siteId:D}";
}
