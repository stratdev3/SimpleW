import { defineConfig } from 'vitepress'

// https://vitepress.dev/reference/site-config
export default defineConfig({
    appearance: 'dark',
    base: '/SimpleW/rewrite/',
    title: 'SimpleW',
    titleTemplate: 'SimpleW',
    description: "SimpleW is a Web server library in .NET Core. Designed for Simplicity. Built for Speed. Packed with Power.",
    head: [
        ['link', { rel: 'icon', href: '/SimpleW/rewrite/favicon.ico' }],
        // analytics
        ['script', { async: '', src: 'https://cloud.umami.is/script.js', 'data-website-id': '6cb0147f-6faa-4c64-ba68-6bd607980ea5' } ],
        // og
        ['meta', { property: 'og:site_name', content: 'SimpleW' }],
        ['meta', { property: 'og:title', content: 'SimpleW | Web Server Library .NET Core' }],
        ['meta', { property: 'og:description', content: 'Built on top of native sockets. Minimal overhead, instant startup, ideal for microservices, embedded apps, and high-performance workloads' }],
        ['meta', { property: 'og:type', content: 'website' }],
        ['meta', { property: 'og:url', content: 'https://stratdev3.github.io/SimpleW/rewrite' }],
        ['meta', { property: 'og:image', content: 'https://stratdev3.github.io/SimpleW/rewrite/simplew-og.png' }],
    ],
    themeConfig: {
        // https://vitepress.dev/reference/default-theme-config
        logo: '/logo-min.webp',

        nav: [
            {
                text: 'Guide',
                link: '/guide/what-is-simplew',
                activeMatch: '/guide/'
            },
            {
                text: 'Reference',
                link: '/reference/simplew',
                activeMatch: '/reference/'
            },
            //{ text: '📖✨ Features', link: '/features' },
            {
                text: 'v26.0-alpha',
                items: [
                    { text: 'v16.1.0', link: 'https://stratdev3.github.io/SimpleW/' },
                    { text: 'Changelog', link: 'https://github.com/stratdev3/SimpleW/blob/master/release.md' },
                ]
            },
        ],

        sidebar: {

            '/guide/': [
                {
                    text: 'Introduction',
                    items: [
                        { text: 'What is SimpleW?', link: '/guide/what-is-simplew' },
                        { text: 'Getting Started', link: '/guide/getting-started' },
                        { text: 'Performances', link: '/guide/performances' },
                        //{ text: 'Lifecycle', link: '/guide/lifecycle' },
                    ]
                },
                {
                    text: 'Serve API',
                    items: [
                        { text: 'Basic', link: '/guide/api-basic' },
                        { text: 'Routing', link: '/guide/api-routing' },
                        { text: 'Response', link: '/guide/api-response' },
                        { text: 'Request', link: '/guide/api-request' },
                        { text: 'Callback', link: '/guide/api-callback' },
                    ]
                },
                {
                    text: 'Security',
                    items: [
                        { text: 'SSL Certificate', link: '/guide/ssl-certificate' },
                        { text: 'Unix Sockets', link: '/guide/unix-sockets' },
                    ]
                },
                {
                    text: 'Others',
                    items: [
                        { text: 'Static Files', link: '/guide/static-files' },
                    ]
                },
            ],

            '/reference/': [
                {
                    text: 'Core',
                    items: [
                        { text: 'Server', link: '/reference/simplew' },
                    ]
                },
                {
                    text: 'Dynamic Content',
                    items: [
                        { text: 'Controller', link: '/reference/controller' },
                        { text: 'HttpRequest', link: '/reference/httprequest' },
                        { text: 'HttpResponse', link: '/reference/httpresponse' },
                        { text: 'HttpSession', link: '/reference/httpsession' },
                        { text: 'RouteAttribute', link: '/reference/routeattribute' },
                    ]
                },
            ]

        },

        socialLinks: [
            { icon: 'github', link: 'https://github.com/stratdev3/SimpleW' },
            //{ icon: 'nuget', link: 'https://www.nuget.org/packages/SimpleW' },
        ],

        footer: {
            message: 'Released under the MIT License.',
            copyright: 'Copyright © 2021-present StratDev'
        },

        search: {
            provider: 'local'
        },
        editLink: {
            pattern: 'https://github.com/stratdev3/SimpleW/edit/master/documentation/simplew/docs/:path'
        },
        externalLinkIcon: true
    },
    sitemap: {
        hostname: 'https://stratdev3.github.io'
    },
    ignoreDeadLinks: [
        // ignore all localhost links
        /^https?:\/\/localhost/,
    ],
})
