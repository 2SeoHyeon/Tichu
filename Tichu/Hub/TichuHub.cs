namespace Tichu.Hub
{
    using Microsoft.AspNetCore.SignalR;

    public class TichuHub : Hub
    {
        public async Task JoinRoom(string roomId, string user)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{roomId}");
            await Clients.Group($"room_{roomId}").SendAsync("PlayerJoined", user);
        }

        public async Task LeaveRoom(string roomId, string user)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room_{roomId}");
            await Clients.Group($"room_{roomId}").SendAsync("PlayerLeft", user);
        }

        public async Task SendPlay(string roomId, string player, string card)
        {
            await Clients.Group($"room_{roomId}").SendAsync("ReceivePlay", player, card);
        }

        public async Task NotifyTurnEnd(string roomId, string user)
        {
            await Clients.Group($"room_{roomId}").SendAsync("TurnEnded", user);
        }
    }
}
