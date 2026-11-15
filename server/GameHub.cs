using Microsoft.AspNetCore.SignalR;

namespace Codenames;

public class GameHub(GameService games) : Hub
{
    public Task Join(string code, string token, string name) => games.Join(Context.ConnectionId, code, token, name);
    public Task ChooseRole(string team, string role) => games.ChooseRole(Context.ConnectionId, team, role);
    public Task StartGame() => games.StartGame(Context.ConnectionId);
    public Task GiveClue(string word, int number) => games.GiveClue(Context.ConnectionId, word, number);
    public Task RevealCard(int index) => games.RevealCard(Context.ConnectionId, index);
    public Task EndTurn() => games.EndTurn(Context.ConnectionId);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await games.Disconnect(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
