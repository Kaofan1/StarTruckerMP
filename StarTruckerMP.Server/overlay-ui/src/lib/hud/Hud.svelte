<script lang="ts">
    import { onGameMessage, sendToGame } from "$lib/gameEvents";

    type SessionPayload = {
        connected?: boolean;
        server?: string;
        netId?: number;
    };

    let interactive = $state(false);
    let connected = $state(false);
    let server = $state("");
    let netId = $state<number | null>(null);

    let host = $state("");
    $effect(() => {
        host = location.host;
    });

    const polish = (navigator.language || "").toLowerCase().startsWith("pl");
    const labels = polish
        ? {
            title: "Star Trucker Multiplayer",
            server: "Serwer",
            session: "Sesja",
            online: "połączono",
            offline: "brak połączenia",
            hints: "F2 / Esc zamykają panel, F3 bada obiekt pod kursorem."
        }
        : {
            title: "Star Trucker Multiplayer",
            server: "Server",
            session: "Session",
            online: "connected",
            offline: "disconnected",
            hints: "F2 / Esc close this panel, F3 inspects the object under the cursor."
        };

    onGameMessage<{ interactive?: boolean }>("overlayModeChanged", (message) => {
        interactive = message.payload?.interactive === true;
    });

    onGameMessage<SessionPayload>("session", (message) => {
        connected = message.payload?.connected === true;
        if (message.payload?.server) server = message.payload.server;
        netId = message.payload?.netId ?? null;
    });

    // The overlay process starts before this page loads, so the game is asked for the
    // current mode and session instead of waiting for the next change.
    sendToGame("overlayLoaded", { timestamp: Date.now() });
</script>

{#if interactive}
    <div class="hud">
        <div class="hud-title">{labels.title}</div>
        <div class="hud-row">
            <span>{labels.server}</span>
            <b>{server || host}</b>
        </div>
        <div class="hud-row">
            <span>{labels.session}</span>
            <b class:online={connected} class:offline={!connected}>
                {connected ? labels.online : labels.offline}
            </b>
        </div>
        {#if connected && netId !== null && netId >= 0}
            <div class="hud-row">
                <span>NetId</span>
                <b>{netId}</b>
            </div>
        {/if}
        <div class="hud-hint">{labels.hints}</div>
    </div>
{/if}

<style>
    .hud {
        position: fixed;
        top: 2vh;
        left: 2vw;
        min-width: 20rem;
        padding: 0.9rem 1.1rem;
        background: rgb(0 0 0 / 72%);
        border: 1px solid rgb(255 255 255 / 18%);
        border-left: 3px solid #f59e0b;
        border-radius: 4px;
        font-size: 0.95rem;
        line-height: 1.5;
        letter-spacing: 0.02em;
        pointer-events: none;
    }

    .hud-title {
        margin-bottom: 0.4rem;
        color: #f59e0b;
        font-weight: 700;
        text-transform: uppercase;
        letter-spacing: 0.12em;
    }

    .hud-row {
        display: flex;
        justify-content: space-between;
        gap: 1.5rem;
        color: rgb(255 255 255 / 65%);
    }

    .hud-row b {
        color: #fff;
        font-weight: 600;
    }

    .hud-row b.online {
        color: #4ade80;
    }

    .hud-row b.offline {
        color: #f87171;
    }

    .hud-hint {
        margin-top: 0.6rem;
        padding-top: 0.5rem;
        border-top: 1px solid rgb(255 255 255 / 12%);
        color: rgb(255 255 255 / 55%);
        font-size: 0.85rem;
    }
</style>
