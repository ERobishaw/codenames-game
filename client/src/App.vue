<script setup>
import { computed, onBeforeUnmount, ref } from 'vue'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'

const code = ref((new URLSearchParams(location.search).get('game') || '').toUpperCase())
const name = ref(localStorage.getItem('codenames-name') || '')
const game = ref(null)
const status = ref('disconnected')
const error = ref('')
const notice = ref('')
const clue = ref('')
const clueNumber = ref(1)
let connection
let sessionToken

const shareLink = computed(() => `${location.origin}${location.pathname}?game=${game.value?.code || code.value}`)
const myTurn = computed(() => game.value && game.value.you.team === game.value.turn && !game.value.winner)
const canGuess = computed(() => myTurn.value && game.value.you.role === 'operative' && game.value.guessesLeft > 0)
const canClue = computed(() => myTurn.value && game.value.you.role === 'spymaster' && game.value.guessesLeft === 0)
const ready = computed(() => ['red', 'blue'].every(team =>
  ['spymaster', 'operative'].every(role => game.value?.players.some(p => p.team === team && p.role === role && p.online))))

function message(e) {
  return (e?.message || 'Something went wrong.').replace(/^An unexpected error occurred invoking '[^']+' on the server\. HubException: /, '')
}

async function createGame() {
  error.value = ''
  try {
    const response = await fetch('/api/games', { method: 'POST' })
    if (!response.ok) throw new Error('Could not create a game.')
    code.value = (await response.json()).code
    await joinGame()
  } catch (e) { error.value = message(e) }
}

async function joinGame() {
  error.value = ''
  code.value = code.value.trim().toUpperCase()
  name.value = name.value.trim()
  if (!name.value || name.value.length > 24 || !/^[A-Z2-9]{6}$/.test(code.value)) {
    error.value = 'Enter your name and a valid six-character game code.'
    return
  }
  localStorage.setItem('codenames-name', name.value)
  const key = `codenames-session-${code.value}`
  sessionToken = localStorage.getItem(key)
  if (!sessionToken) {
    sessionToken = Array.from(crypto.getRandomValues(new Uint8Array(32)), byte => byte.toString(16).padStart(2, '0')).join('')
    localStorage.setItem(key, sessionToken)
  }
  connection = new HubConnectionBuilder()
    .withUrl('/hub/game')
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: ctx => Math.min(1000 * (ctx.previousRetryCount + 1), 10000) })
    .configureLogging(LogLevel.Warning)
    .build()
  connection.on('GameUpdated', state => { game.value = state })
  connection.onreconnecting(() => { status.value = 'reconnecting' })
  connection.onreconnected(async () => {
    try {
      await connection.invoke('Join', code.value, sessionToken, name.value)
      status.value = 'connected'
      error.value = ''
    } catch (e) { error.value = message(e) }
  })
  connection.onclose(() => { status.value = 'disconnected' })
  status.value = 'connecting'
  try {
    await connection.start()
    await connection.invoke('Join', code.value, sessionToken, name.value)
    status.value = 'connected'
    history.replaceState(null, '', `?game=${encodeURIComponent(code.value)}`)
  } catch (e) {
    error.value = message(e)
    await connection.stop()
    game.value = null
    status.value = 'disconnected'
  }
}

async function invoke(method, ...args) {
  error.value = ''
  try { await connection.invoke(method, ...args) } catch (e) { error.value = message(e) }
}

async function giveClue() {
  await invoke('GiveClue', clue.value, Number(clueNumber.value))
  if (!error.value) clue.value = ''
}

async function copyLink() {
  try {
    await navigator.clipboard.writeText(shareLink.value)
    notice.value = 'Link copied!'
  } catch {
    notice.value = 'Copy the link from the address bar.'
  }
}

onBeforeUnmount(() => connection?.stop())
</script>

<template>
  <div class="app">
    <header>
      <div>
        <p class="eyebrow">A game for friends & family</p>
        <h1>Codenames</h1>
      </div>
      <span v-if="game" class="connection" :class="status">{{ status }}</span>
    </header>

    <p v-if="error" class="alert" role="alert">{{ error }}</p>

    <main v-if="!game" class="welcome panel">
      <h2>Get everyone together.</h2>
      <p>Create a game and share the link, or enter a code to join. No account needed.</p>
      <form @submit.prevent="joinGame">
        <label>Your name <input v-model="name" maxlength="24" autocomplete="nickname" required placeholder="Your name"></label>
        <label>Game code <input v-model="code" maxlength="6" autocomplete="off" placeholder="e.g. ABC234" class="code-input"></label>
        <div class="actions">
          <button type="submit" :disabled="status !== 'disconnected'">Join game</button>
          <button type="button" class="secondary" :disabled="status !== 'disconnected' || !name.trim()" @click="createGame">Create game</button>
        </div>
      </form>
    </main>

    <main v-else>
      <section class="share panel">
        <div><span class="muted">Game code</span> <strong class="game-code">{{ game.code }}</strong></div>
        <button class="secondary" @click="copyLink">Copy invite link</button>
        <span v-if="notice" class="muted" role="status">{{ notice }}</span>
      </section>

      <div v-if="status !== 'connected'" class="alert" role="status">Reconnecting… Actions will be available when connected.</div>

      <section v-if="!game.started" class="panel lobby">
        <h2>Choose your team</h2>
        <p>Each team needs a spymaster and at least one operative. Everyone else can watch.</p>
        <p>You are <strong>{{ game.you.name }}</strong> · {{ game.you.team }} {{ game.you.role === 'spectator' ? '' : game.you.role }}</p>
        <div class="teams">
          <div v-for="team in ['red', 'blue']" :key="team" class="team" :class="team">
            <h3>{{ team }} team</h3>
            <ul>
              <li v-for="(player, i) in game.players.filter(p => p.team === team)" :key="i">
                {{ player.name }} · {{ player.role }} <span v-if="!player.online">(offline)</span>
              </li>
            </ul>
            <div class="actions">
              <button :disabled="status !== 'connected'" @click="invoke('ChooseRole', team, 'operative')">Join as operative</button>
              <button class="secondary" :disabled="status !== 'connected'" @click="invoke('ChooseRole', team, 'spymaster')">Be spymaster</button>
            </div>
          </div>
        </div>
        <p class="muted">Spectators: {{ game.players.filter(p => p.role === 'spectator').map(p => p.name + (p.online ? '' : ' (offline)')).join(', ') || 'none' }}</p>
        <div class="actions">
          <button class="secondary" :disabled="status !== 'connected'" @click="invoke('ChooseRole', 'spectator', 'spectator')">Watch instead</button>
          <button :disabled="!ready || game.you.team === 'spectator' || status !== 'connected'" @click="invoke('StartGame')">Start game</button>
        </div>
        <small v-if="!ready">Both teams need a connected spymaster and operative to start.</small>
      </section>

      <template v-else>
        <section class="panel game-status">
          <div>
            <h2 v-if="game.winner" class="capitalize">{{ game.winner }} team wins!</h2>
            <h2 v-else class="capitalize">{{ game.turn }} team's turn</h2>
            <p>You: {{ game.you.name }} · {{ game.you.team }} {{ game.you.role === 'spectator' ? '' : game.you.role }}</p>
            <p v-if="game.clue">Clue: <strong>{{ game.clue }}</strong> · {{ game.guessesLeft }} guesses left</p>
            <p v-else-if="!game.winner">Waiting for the spymaster's clue.</p>
          </div>
          <div class="scores"><span class="score red">Red {{ game.redRemaining }}</span><span class="score blue">Blue {{ game.blueRemaining }}</span></div>
        </section>
        <section v-if="game.you.role === 'spectator' && !game.winner" class="panel">
          <span>Want to play? </span>
          <button class="secondary" :disabled="status !== 'connected'" @click="invoke('ChooseRole', 'red', 'operative')">Join red</button>
          <button class="secondary" :disabled="status !== 'connected'" @click="invoke('ChooseRole', 'blue', 'operative')">Join blue</button>
        </section>
        <section v-if="canClue" class="panel">
          <form class="clue-form" @submit.prevent="giveClue">
            <label>One-word clue <input v-model="clue" maxlength="30" required></label>
            <label>Number <input v-model.number="clueNumber" type="number" min="1" max="25" required></label>
            <button :disabled="status !== 'connected'">Give clue</button>
          </form>
        </section>
        <section aria-label="Game board" class="board">
          <button v-for="(card, i) in game.cards" :key="i" class="card"
            :class="[card.color || 'hidden', { revealed: card.revealed }]"
            :disabled="!canGuess || card.revealed || status !== 'connected'"
            :aria-label="`${card.word}${card.color ? ', ' + card.color : ''}${card.revealed ? ', revealed' : ''}`"
            @click="invoke('RevealCard', i)">
            {{ card.word }} <span v-if="card.revealed" aria-hidden="true">✓</span>
          </button>
        </section>
        <div v-if="canGuess" class="actions"><button class="secondary" :disabled="status !== 'connected' || !game.guessesMade" @click="invoke('EndTurn')">End turn</button></div>
        <section class="panel log">
          <h2>Game log</h2>
          <ol><li v-for="(entry, i) in [...game.log].reverse()" :key="i">{{ entry }}</li></ol>
        </section>
      </template>
    </main>
  </div>
</template>
