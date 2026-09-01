<template>
    <figure class="terminal" aria-labelledby="terminal-caption">
        <figcaption id="terminal-caption" class="terminal__caption">
            <span class="terminal__brand" aria-hidden="true">
                <span class="terminal__brand-icon">&gt;_</span>
                <span class="terminal__brand-name">SimpleW console</span>
            </span>
            <span class="terminal__title">hello-simplew — dotnet run</span>
            <span class="terminal__running">
                <span class="terminal__running-dot" aria-hidden="true"></span>
                running
            </span>
        </figcaption>

        <div class="terminal__screen">
            <div class="terminal__line terminal__line--command">
                <span class="terminal__prompt" aria-hidden="true">❯</span>
                <span>dotnet run</span>
            </div>

            <div class="terminal__line terminal__line--log terminal__line--first-log">
                <span class="terminal__time">12:04:31.205</span>
                <span class="terminal__level">[😴|INF]</span>
                <span class="terminal__source">SimpleW.SimpleWServer</span>
                <span>server starting...</span>
            </div>

            <div class="terminal__line terminal__line--log terminal__line--second-log">
                <span class="terminal__time">12:04:31.222</span>
                <span class="terminal__level">[😴|INF]</span>
                <span class="terminal__source">SimpleW.SimpleWServer</span>
                <span>server started at <strong>http://127.0.0.1:2015</strong></span>
            </div>

            <div class="terminal__ready">
                <span class="terminal__ready-icon" aria-hidden="true">✓</span>
                <span>
                    <strong>Ready to accept requests</strong>
                    <small>Press Ctrl+C to stop the server</small>
                </span>
                <span class="terminal__cursor" aria-hidden="true"></span>
            </div>
        </div>
    </figure>
</template>

<style scoped>
.terminal {
    --terminal-background: #0b1020;
    --terminal-surface: #11182b;
    --terminal-border: rgba(148, 163, 184, .2);
    --terminal-muted: #8390a8;
    --terminal-text: #e7edf7;
    --terminal-green: #5ee19f;
    --terminal-purple: #b9a4ff;
    margin: 1.5rem 0 2rem;
    overflow: hidden;
    border: 1px solid var(--terminal-border);
    border-radius: 14px;
    background: var(--terminal-background);
    box-shadow: 0 18px 50px rgba(6, 10, 24, .24);
}

.terminal__caption {
    display: grid;
    grid-template-columns: 1fr auto 1fr;
    align-items: center;
    min-height: 44px;
    padding: 0 .9rem;
    border-bottom: 1px solid var(--terminal-border);
    background: var(--terminal-surface);
    color: var(--terminal-muted);
    font-family: var(--vp-font-family-mono);
    font-size: .72rem;
}

.terminal__brand {
    display: inline-flex;
    gap: .55rem;
    align-items: center;
    justify-self: start;
}

.terminal__brand-icon {
    display: inline-grid;
    place-items: center;
    width: 29px;
    height: 24px;
    border: 1px solid rgba(94, 225, 159, .35);
    border-radius: 5px;
    background: rgba(94, 225, 159, .08);
    color: var(--terminal-green);
    font-size: .68rem;
    font-weight: 700;
    letter-spacing: -.08em;
}

.terminal__brand-name {
    color: #aab5c8;
    font-size: .68rem;
    letter-spacing: .02em;
}

.terminal__title {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.terminal__running {
    display: inline-flex;
    gap: .4rem;
    align-items: center;
    justify-self: end;
    color: var(--terminal-green);
    font-size: .68rem;
    letter-spacing: .04em;
    text-transform: uppercase;
}

.terminal__running-dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: currentColor;
    box-shadow: 0 0 0 4px rgba(94, 225, 159, .12);
}

.terminal__screen {
    overflow-x: auto;
    padding: 1.2rem 1.3rem 1.3rem;
    color: var(--terminal-text);
    font-family: var(--vp-font-family-mono);
    font-size: .82rem;
    line-height: 1.7;
}

.terminal__line {
    display: flex;
    gap: .65rem;
    width: max-content;
    min-width: 100%;
    white-space: nowrap;
    opacity: 0;
    animation: terminal-reveal .35s ease-out forwards;
}

.terminal__line--command {
    margin-bottom: .8rem;
    animation-delay: .1s;
}

.terminal__line--first-log {
    animation-delay: .55s;
}

.terminal__line--second-log {
    animation-delay: 1s;
}

.terminal__prompt,
.terminal__level,
.terminal__ready-icon {
    color: var(--terminal-green);
}

.terminal__time {
    color: var(--terminal-muted);
}

.terminal__source {
    color: var(--terminal-purple);
}

.terminal__line strong {
    color: #8ecbff;
    font-weight: 600;
}

.terminal__ready {
    display: flex;
    gap: .65rem;
    align-items: center;
    width: max-content;
    min-width: 100%;
    margin-top: 1rem;
    padding-top: .9rem;
    border-top: 1px solid var(--terminal-border);
    opacity: 0;
    animation: terminal-reveal .35s ease-out 1.45s forwards;
}

.terminal__ready > span:nth-child(2) {
    display: flex;
    flex-direction: column;
    line-height: 1.35;
}

.terminal__ready strong {
    color: var(--terminal-green);
    font-size: .78rem;
    font-weight: 600;
}

.terminal__ready small {
    color: var(--terminal-muted);
    font-size: .68rem;
}

.terminal__cursor {
    width: 7px;
    height: 15px;
    margin-left: .2rem;
    background: var(--terminal-green);
    animation: terminal-blink 1.1s steps(1) infinite;
}

@keyframes terminal-reveal {
    from {
        opacity: 0;
        transform: translateY(4px);
    }
    to {
        opacity: 1;
        transform: translateY(0);
    }
}

@keyframes terminal-blink {
    50% {
        opacity: 0;
    }
}

@media (max-width: 640px) {
    .terminal__caption {
        grid-template-columns: auto 1fr;
    }

    .terminal__title {
        padding-left: .8rem;
        text-align: right;
    }

    .terminal__brand-name {
        display: none;
    }

    .terminal__running {
        display: none;
    }

    .terminal__screen {
        padding: 1rem;
        font-size: .75rem;
    }
}

@media (prefers-reduced-motion: reduce) {
    .terminal__line,
    .terminal__ready {
        opacity: 1;
        animation: none;
    }

    .terminal__cursor {
        animation: none;
    }
}
</style>
