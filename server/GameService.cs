using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;

namespace Codenames;

public sealed class GameService(IHubContext<GameHub> hub)
{
    private readonly Dictionary<string, Game> games = new();
    private readonly Dictionary<string, (Game Game, Player Player)> connections = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<string> CreateGame()
    {
        await gate.WaitAsync();
        try
        {
            string code;
            do
            {
                code = new string(Enumerable.Range(0, 6).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
            } while (games.ContainsKey(code));
            games.Add(code, new Game(code));
            return code;
        }
        finally { gate.Release(); }
    }

    public async Task Join(string connection, string code, string token, string name)
    {
        await gate.WaitAsync();
        try
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            name = (name ?? "").Trim();
            if (!games.TryGetValue(code, out var game)) throw new HubException("Game not found. Check the link or code.");
            if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new HubException("Invalid session.");
            if (name.Length is < 1 or > 24) throw new HubException("Enter a name between 1 and 24 characters.");
            if (connections.ContainsKey(connection)) throw new HubException("Already joined a game.");

            if (!game.Players.TryGetValue(token, out var player))
            {
                player = new Player(name);
                game.Players.Add(token, player);
            }
            else player.Name = name;
            player.Connections.Add(connection);
            connections.Add(connection, (game, player));
            await Broadcast(game);
        }
        finally { gate.Release(); }
    }

    public Task ChooseRole(string connection, string team, string role) => Change(connection, (game, player) =>
    {
        if (team == "spectator" && role == "spectator")
        {
            if (game.Started && player.Role != "spectator") throw new HubException("Players cannot change roles after the game starts.");
            player.Team = team;
            player.Role = role;
            return;
        }
        if ((team != "red" && team != "blue") || (role != "operative" && role != "spymaster"))
            throw new HubException("Choose a valid team and role.");
        if (game.Started && player.Role != "spectator") throw new HubException("Players cannot change roles after the game starts.");
        if (game.Started && role == "spymaster") throw new HubException("Spymasters must join before the game starts.");
        if (role == "spymaster" && game.Players.Values.Any(p => p != player && p.Team == team && p.Role == role))
            throw new HubException("That team already has a spymaster.");
        player.Team = team;
        player.Role = role;
    });

    public Task StartGame(string connection) => Change(connection, (game, player) =>
    {
        if (game.Started) throw new HubException("The game has already started.");
        if (player.Team == "spectator") throw new HubException("Join a team to start the game.");
        foreach (var team in new[] { "red", "blue" })
            foreach (var role in new[] { "spymaster", "operative" })
                if (!game.Players.Values.Any(p => p.Team == team && p.Role == role && p.Connections.Count > 0))
                    throw new HubException("Both teams need a connected spymaster and operative.");

        game.Turn = RandomNumberGenerator.GetInt32(2) == 0 ? "red" : "blue";
        var colors = Enumerable.Repeat(game.Turn, 9)
            .Concat(Enumerable.Repeat(OtherTeam(game.Turn), 8))
            .Concat(Enumerable.Repeat("neutral", 7))
            .Append("assassin").OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToArray();
        var words = WordService.GetWords();
        game.Cards = words.Select((word, i) => new Card(word, colors[i])).ToList();
        game.Started = true;
        game.Log.Add($"{Cap(game.Turn)} team goes first.");
    });

    public Task GiveClue(string connection, string word, int number) => Change(connection, (game, player) =>
    {
        RequireTurn(game, player, "spymaster");
        if (game.GuessesLeft != 0) throw new HubException("Your team is still guessing.");
        word = (word ?? "").Trim();
        if (word.Length is < 1 or > 30 || word.Any(char.IsWhiteSpace)) throw new HubException("The clue must be one word (up to 30 characters).");
        if (number is < 1 or > 25) throw new HubException("Choose a number from 1 to 25.");
        if (game.Cards.Any(c => !c.Revealed && string.Equals(c.Word, word, StringComparison.OrdinalIgnoreCase)))
            throw new HubException("The clue cannot be an unrevealed board word.");
        game.GuessesLeft = number + 1;
        game.Clue = $"{word} · {number}";
        game.Log.Add($"{Cap(game.Turn)} clue: {word} ({number}).");
    });

    public Task RevealCard(string connection, int index) => Change(connection, (game, player) =>
    {
        RequireTurn(game, player, "operative");
        if (game.GuessesLeft == 0) throw new HubException("Wait for your spymaster's clue.");
        if (index < 0 || index >= game.Cards.Count || game.Cards[index].Revealed) throw new HubException("Choose an unrevealed card.");
        var card = game.Cards[index];
        card.Revealed = true;
        game.GuessesLeft--;
        game.Log.Add($"{Cap(game.Turn)} revealed {card.Word} ({card.Color}).");
        if (card.Color == "assassin")
            Finish(game, OtherTeam(game.Turn), "The assassin was revealed.");
        else if ((card.Color == "red" || card.Color == "blue") && game.Cards.All(c => c.Color != card.Color || c.Revealed))
            Finish(game, card.Color, "All team words were revealed.");
        else if (card.Color != game.Turn || game.GuessesLeft == 0)
            NextTurn(game);
    });

    public Task EndTurn(string connection) => Change(connection, (game, player) =>
    {
        RequireTurn(game, player, "operative");
        if (game.GuessesLeft == 0) throw new HubException("There is no active clue.");
        game.Log.Add($"{Cap(game.Turn)} ended their turn.");
        NextTurn(game);
    });

    public async Task Disconnect(string connection)
    {
        await gate.WaitAsync();
        try
        {
            if (connections.Remove(connection, out var entry))
            {
                entry.Player.Connections.Remove(connection);
                await Broadcast(entry.Game);
            }
        }
        finally { gate.Release(); }
    }

    private async Task Change(string connection, Action<Game, Player> action)
    {
        await gate.WaitAsync();
        try
        {
            if (!connections.TryGetValue(connection, out var entry)) throw new HubException("Join a game first.");
            action(entry.Game, entry.Player);
            await Broadcast(entry.Game);
        }
        finally { gate.Release(); }
    }

    private async Task Broadcast(Game game)
    {
        foreach (var player in game.Players.Values)
        {
            var state = new
            {
                game.Code, game.Started, game.Turn, game.Winner, game.Clue, game.GuessesLeft,
                You = new { player.Name, player.Team, player.Role },
                Players = game.Players.Values.Select(p => new { p.Name, p.Team, p.Role, Online = p.Connections.Count > 0 }).ToArray(),
                Cards = game.Cards.Select(c => new { c.Word, c.Revealed, Color = c.Revealed || player.Role == "spymaster" || game.Winner != null ? c.Color : null }).ToArray(),
                RedRemaining = game.Cards.Count(c => c.Color == "red" && !c.Revealed),
                BlueRemaining = game.Cards.Count(c => c.Color == "blue" && !c.Revealed),
                Log = game.Log.ToArray()
            };
            foreach (var connection in player.Connections)
                await hub.Clients.Client(connection).SendAsync("GameUpdated", state);
        }
    }

    private static void RequireTurn(Game game, Player player, string role)
    {
        if (!game.Started || game.Winner != null) throw new HubException("The game is not in progress.");
        if (player.Team != game.Turn || player.Role != role) throw new HubException("This action is not available for your role or team.");
    }
    private static void NextTurn(Game game)
    {
        game.Turn = OtherTeam(game.Turn);
        game.GuessesLeft = 0;
        game.Clue = null;
        game.Log.Add($"{Cap(game.Turn)} team's turn.");
    }
    private static void Finish(Game game, string winner, string reason)
    {
        game.Winner = winner;
        game.GuessesLeft = 0;
        game.Log.Add($"{reason} {Cap(winner)} team wins!");
    }
    private static string OtherTeam(string team) => team == "red" ? "blue" : "red";
    private static string Cap(string value) => char.ToUpperInvariant(value[0]) + value[1..];
}

internal sealed class Game(string code)
{
    public string Code { get; } = code;
    public Dictionary<string, Player> Players { get; } = new();
    public List<Card> Cards { get; set; } = new();
    public List<string> Log { get; } = new();
    public bool Started { get; set; }
    public string Turn { get; set; } = "";
    public string? Winner { get; set; }
    public string? Clue { get; set; }
    public int GuessesLeft { get; set; }
}

internal sealed class Player(string name)
{
    public string Name { get; set; } = name;
    public string Team { get; set; } = "spectator";
    public string Role { get; set; } = "spectator";
    public HashSet<string> Connections { get; } = new();
}

internal sealed class Card(string word, string color)
{
    public string Word { get; } = word;
    public string Color { get; } = color;
    public bool Revealed { get; set; }
}
