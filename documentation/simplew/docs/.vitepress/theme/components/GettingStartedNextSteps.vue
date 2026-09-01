<template>
    <nav class="next-steps" aria-label="Continue learning SimpleW">
        <a class="next-step" href="/guide/routing">
            <span class="next-step__number">01</span>
            <strong>Build an API</strong>
            <span>Learn routing, parameters and controller-based handlers.</span>
            <span class="next-step__link">Explore routing <span aria-hidden="true">→</span></span>
        </a>

        <a class="next-step" href="/guide/staticfiles">
            <span class="next-step__number">02</span>
            <strong>Serve a website</strong>
            <span>Publish HTML, CSS and JavaScript with the static files module.</span>
            <span class="next-step__link">Serve static files <span aria-hidden="true">→</span></span>
        </a>

        <a class="next-step" href="/addons/addons">
            <span class="next-step__number">03</span>
            <strong>Extend SimpleW</strong>
            <span>Add authentication, Swagger, Razor, background jobs and more.</span>
            <span class="next-step__link">Browse addons <span aria-hidden="true">→</span></span>
        </a>
    </nav>
</template>

<style scoped>
.next-steps {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 1rem;
    margin: 1.5rem 0 2rem;
}

.next-step {
    display: flex;
    flex-direction: column;
    min-height: 190px;
    padding: 1.15rem;
    border: 1px solid var(--vp-c-divider);
    border-radius: 12px;
    background: var(--vp-c-bg-soft);
    color: var(--vp-c-text-2);
    text-decoration: none;
    transition: border-color .2s ease, box-shadow .2s ease, transform .2s ease;
}

.next-step:hover {
    border-color: var(--vp-c-brand-1);
    box-shadow: 0 12px 30px rgba(44, 62, 80, .12);
    transform: translateY(-3px);
}

.next-step__number {
    margin-bottom: 1rem;
    color: var(--vp-c-brand-1);
    font-family: var(--vp-font-family-mono);
    font-size: .72rem;
    font-weight: 700;
    letter-spacing: .08em;
}

.next-step strong {
    margin-bottom: .45rem;
    color: var(--vp-c-text-1);
    font-size: 1rem;
}

.next-step > span:not(.next-step__number, .next-step__link) {
    font-size: .84rem;
    line-height: 1.55;
}

.next-step__link {
    margin-top: auto;
    padding-top: 1rem;
    color: var(--vp-c-brand-1);
    font-size: .78rem;
    font-weight: 600;
}

@media (max-width: 760px) {
    .next-steps {
        grid-template-columns: 1fr;
    }

    .next-step {
        min-height: 0;
    }
}

@media (prefers-reduced-motion: reduce) {
    .next-step {
        transition: none;
    }

    .next-step:hover {
        transform: none;
    }
}
</style>
