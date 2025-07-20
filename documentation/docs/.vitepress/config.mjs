import { defineConfig } from 'vitepress'

// https://vitepress.dev/reference/site-config
export default defineConfig({
    base: '/SimpleW/',
    title: 'SimpleW',
    titleTemplate: 'SimpleW',
    description: "Simple Web server library in .NET Core",
    head: [
        ['link', { rel: 'icon', href: '/SimpleW/favicon.ico' }],
        ['script', { async: '', src: 'https://www.googletagmanager.com/gtag/js?id=G-5X34BRXK43' } ],
        ['script', {}, `window.dataLayer = window.dataLayer || []; function gtag(){dataLayer.push(arguments);} gtag('js', new Date()); gtag('config', 'G-5X34BRXK43');`]
    ],
    themeConfig: {
        // https://vitepress.dev/reference/default-theme-config
        logo: '/logo.png',

        nav: [
            { text: 'Guide', link: '/what-is-simplew' },
            {
                text: 'v14.0.0',
                items: [
                    { text: 'Changelog', link: 'https://github.com/stratdev3/SimpleW/blob/master/release.md' },
                ]
            },
        ],

        sidebar: [
            {
                text: 'Introduction',
                items: [
                    { text: 'What is SimpleW?', link: '/what-is-simplew' },
                    { text: 'Getting Started', link: '/getting-started' },
                ]
            },
            // {
            //     text: 'Routing', link: '/routing',
            // },
            {
                text: 'Serve API',
                items: [
                    { text: 'Basic Example', link: '/api-basic-example' },
                    { text: 'Return Type', link: '/api-return-type' },
                    { text: 'Routes', link: '/api-routes' },
                    { text: 'Post Body', link: '/api-post-body' },
                    { text: 'CORS', link: '/api-cors' },
                    { text: 'Serialization', link: '/api-serialization' },
                    { text: 'Hooks', link: '/api-hook' },
                ]
            },
            {
                text: 'Built-in Components',
                items: [
                    { text: 'Static Files', link: '/static-files' },
                    { text: 'JWT Authentication', link: '/api-jwt-authentication' },
                    { text: 'Websockets', link: '/websockets' },
                    { text: 'Https', link: '/https' },
                    { text: 'OpenTelemetry', link: '/opentelemetry' },
                    { text: 'Advanced', link: '/advanced' },
                ]
            },
        ],

        socialLinks: [
            { icon: 'github', link: 'https://github.com/stratdev3/SimpleW' },
            //{ icon: 'nuget', link: 'https://www.nuget.org/packages/SimpleW' },
        ],

        footer: {
            message: 'Released under the MIT License.',
            copyright: 'Copyright © 2024-present StratDev'
        },

        search: {
            provider: 'local'
        },
        editLink: {
            pattern: 'https://github.com/stratdev3/SimpleW/edit/master/documentation/docs/:path'
        },

    },
    sitemap: {
        hostname: 'https://stratdev3.github.io'
    },
    ignoreDeadLinks: [
        // ignore all localhost links
        /^https?:\/\/localhost/,
    ],
})
