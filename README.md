# Codenames

A mobile-friendly, real-time word game for remote family and friends. Create a game, share its link, and join as a red or blue spymaster, operative, or spectator.

## Run locally

Prerequisites: .NET 11 SDK and Node.js 22.12 or later.

In one terminal:

```sh
dotnet run --project server --urls http://localhost:5000
```

In another:

```sh
cd client
npm ci
npm run dev
```

Open the Vite URL shown in the terminal. The development server proxies API and SignalR requests to port 5000. For a production build, run `npm run build` in `client` and `dotnet publish server -c Release`; the Docker image combines both into a single service.

## Play

Create a game and copy the invite link. Each team needs a connected spymaster and operative before anyone on a team can start. Spymasters see the hidden key and give a one-word clue and number. The current team's operatives must reveal at least one card, and can guess up to the clue number plus one. A wrong card ends the turn; revealing the assassin immediately awards the other team the win. Reveal all your team's words to win. Spectators can join either team as operatives during a game.

The browser stores a random session token per game to restore your role after a reconnect or page reload. Keep the token and invite link private; there are no accounts or game passwords. Players cannot change roles after starting, and an unavailable spymaster must reconnect using the same browser session.

## Deploy to Render

Create a Render Blueprint from this repository using `render.yaml`, or create a Docker web service using the included Dockerfile. Render provides `PORT`; the container serves the Vue app, API, and SignalR on that port. `/api/health` is the health check. Free instances may sleep when idle.

Games are stored **in memory** on one server instance. A restart, deployment, or instance replacement deletes active games; multiple instances will not share game state. Use a single instance for this MVP. The included word list is local; image upload/OCR, persistent history, custom word sets, and difficulty selection are future work.
