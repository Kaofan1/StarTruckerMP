<script lang="ts">
    import {type GameMessage, onGameMessage, sendToGame} from "$lib/gameEvents";
    import ObjectInspector from "$lib/inspector/ObjectInspector.svelte";
    import RunCode from "$lib/runcode/RunCode.svelte";
    import Hud from "$lib/hud/Hud.svelte";

    sendToGame("overlayLoaded", { timestamp: Date.now() });
</script>

<div class="container">
    <Hud />
    <ObjectInspector />
    <!--<RunCode />-->
</div>

<style>
    .container {
        position: absolute;
        top: 0;
        left: 0;
        width: 100%;
        height: 100%;
    }
</style>