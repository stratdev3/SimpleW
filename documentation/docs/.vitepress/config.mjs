import { defineConfig } from 'vitepress'

// https://vitepress.dev/reference/site-config
export default defineConfig({
    appearance: 'dark',
    base: '/SimpleW/',
    title: 'SimpleW',
    titleTemplate: 'SimpleW',
    description: "SimpleW is a Web server library in .NET Core. Designed for Simplicity. Built for Speed. Packed with Power.",
    head: [
        ['link', { rel: 'icon', href: '/SimpleW/favicon.ico' }],
        // analytics
        ['script', { async: '', src: 'https://www.googletagmanager.com/gtag/js?id=G-5X34BRXK43' } ],
        ['script', {}, `window.dataLayer = window.dataLayer || []; function gtag(){dataLayer.push(arguments);} gtag('js', new Date()); gtag('config', 'G-5X34BRXK43');`],
        // og
        ['meta', { property: 'og:site_name', content: 'SimpleW' }],
        ['meta', { property: 'og:title', content: 'SimpleW | Web Server Library .NET Core' }],
        ['meta', { property: 'og:description', content: 'Built on top of native sockets. Minimal overhead, instant startup, ideal for microservices, embedded apps, and high-performance workloads' }],
        ['meta', { property: 'og:type', content: 'website' }],
        ['meta', { property: 'og:url', content: 'https://stratdev3.github.io/SimpleW' }],
        ['meta', { property: 'og:image', content: 'https://stratdev3.github.io/SimpleW/simplew-og.png' }],
    ],
    themeConfig: {
        // https://vitepress.dev/reference/default-theme-config
        logo: '/logo-min.png',

        nav: [
            {
                text: 'Guide',
                link: '/guide/what-is-simplew',
                activeMatch: '/guide/'
            },
            {
                text: 'Reference',
                link: '/reference/simplewserver',
                activeMatch: '/reference/'
            },
            //{ text: '📖✨ Features', link: '/features' },
            {
                text: 'v15.1.0',
                items: [
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
                        { text: 'Json Web Token', link: '/guide/api-json-web-token' },
                        { text: 'Cross-Origin Resource Sharing', link: '/guide/api-cors' },
                        { text: 'SSL Certificate', link: '/guide/ssl-certificate' },
                        { text: 'Unix Sockets', link: '/guide/unix-sockets' },
                    ]
                },
                {
                    text: 'Communication',
                    items: [
                        { text: 'Server Sent Events', link: '/guide/server-sent-events' },
                        { text: 'Websockets', link: '/guide/websockets' },
                    ]
                },
                {
                    text: 'Others',
                    items: [
                        { text: 'Static Files', link: '/guide/static-files' },
                        { text: 'Observability', link: '/guide/observability' },
                    ]
                },
            ],

            '/reference/': [
                {
                    text: 'Reference',
                    items: [
                        { text: 'Server', link: '/reference/simplewserver' },
                        { text: 'RouteAttribute', link: '/reference/routeattribute' },
                        {
                          text: 'Controller',
                          base: '/reference/controller-',
                          items: [
                              { text: 'Overview', link: 'overview' },
                              { text: 'HttpRequest', link: 'httprequest' },
                              { text: 'Session', link: 'session' },
                              { text: 'HttpResponse', link: 'httpresponse' },
                              { text: 'OnBeforeMethod', link: 'onbeforemethod' },
                          ]
                        },
                        { text: 'NetCoreServerExtension', link: '/reference/netcoreserverextension' },
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
