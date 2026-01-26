import { defineConfig } from 'vitepress'

// https://vitepress.dev/reference/site-config
export default defineConfig({
    appearance: 'dark',
    base: '/v26/',
    title: 'SimpleW',
    titleTemplate: 'SimpleW',
    description: "SimpleW is a Web server library in .NET Core. Designed for Simplicity. Built for Speed. Packed with Power.",
    head: [
        ['link', { rel: 'icon', href: '/v26/favicon.ico' }],
        // analytics
        ['script', { async: '', src: 'https://cloud.umami.is/script.js', 'data-website-id': '6cb0147f-6faa-4c64-ba68-6bd607980ea5' } ],
        // og
        ['meta', { property: 'og:site_name', content: 'SimpleW' }],
        ['meta', { property: 'og:title', content: 'SimpleW | Web Server Library .NET Core' }],
        ['meta', { property: 'og:description', content: 'Built on top of native sockets. Minimal overhead, instant startup, ideal for microservices, embedded apps, and high-performance workloads' }],
        ['meta', { property: 'og:type', content: 'website' }],
        ['meta', { property: 'og:url', content: 'https://simplew.net' }],
        ['meta', { property: 'og:image', content: 'https://simplew.net/simplew-og.png' }],
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
                text: 'Addons',
                link: '/addons/addons',
                activeMatch: '/addons/'
            },
            {
                text: 'Reference',
                link: '/reference/simplewserver',
                activeMatch: '/reference/'
            },
            //{ text: '📖✨ Features', link: '/features' },
            {
                text: 'v26.0-alpha',
                items: [
                    { text: 'v16.1.0', link: 'https://simplew.net/' },
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
                    text: 'Framework',
                    items: [
                        { text: 'Handler', link: '/guide/handler' },
                        { text: 'Routing', link: '/guide/routing' },
                        { text: 'Response', link: '/guide/response' },
                        { text: 'Request', link: '/guide/request' },
                    ]
                },
                {
                    text: 'Extend',
                    items: [
                        { text: 'Middleware', link: '/guide/middleware' },
                        { text: 'Module', link: '/guide/module' },
                        { text: 'Callback', link: '/guide/callback' },
                        { text: 'Result Handler', link: '/guide/handlerresult' },
                        { text: 'Json Engine', link: '/guide/jsonengine' },
                    ]
                },
                {
                    text: 'Security',
                    items: [
                        { text: 'Json Web Token', link: '/guide/jsonwebtoken' },
                        { text: 'Cross-Origin Resource Sharing', link: '/guide/cors' },
                        { text: 'SSL Certificate', link: '/guide/ssl-certificate' },
                        { text: 'Basic Auth', link: '/guide/basicauth' },
                    ]
                },
                {
                    text: 'Communication',
                    items: [
                        { text: 'Server Sent Events', link: '/guide/serversentevents' },
                        { text: 'Websockets', link: '/guide/websockets' },
                    ]
                },
                {
                    text: 'Others',
                    items: [
                        { text: 'Static Files', link: '/guide/staticfiles' },
                        { text: 'Unix Sockets', link: '/guide/unix-sockets' },
                        { text: 'Observability', link: '/guide/observability' },
                    ]
                },
            ],

            '/addons/': [
                {
                    text: 'Services',
                    items: [
                        
                    ]
                },
                {
                    text: 'JsonEngine',
                    items: [
                        { text: 'Newtonsoft', link: '/addons/newtonsoft' },
                    ]
                },
                {
                    text: 'Helpers',
                    items: [
                        { text: 'Hosting', link: '/addons/hosting' },
                    ]
                },
            ],

            '/reference/': [
                {
                    text: 'Core',
                    items: [
                        { text: 'SimpleWServer', link: '/reference/simplewserver' },
                        { text: 'SimpleWServerOptions', link: '/reference/simplewserveroptions' },
                        { text: 'HttpSession', link: '/reference/httpsession' },
                        { text: 'HttpRequest', link: '/reference/httprequest' },
                        { text: 'HttpResponse', link: '/reference/httpresponse' },
                        { text: 'HttpHeaders', link: '/reference/httpheaders' },
                    ]
                },
                {
                    text: 'Routing',
                    items: [
                        { text: 'Router', link: '/reference/router' },
                        { text: 'RouteAttribute', link: '/reference/routeattribute' },
                        { text: 'Controller', link: '/reference/controller' },
                    ]
                },
                {
                    text: 'Modules',
                    items: [
                        { text: 'StaticFilesModule', link: '/reference/staticfilesmodule' },
                        { text: 'CorsModule', link: '/reference/corsmodule' },
                        { text: 'SseModule', link: '/reference/ssemodule' },
                        { text: 'WebsocketModule', link: '/reference/websocketmodule' },
                    ]
                },
                {
                    text: 'Helpers',
                    items: [
                        { text: 'SimpleWExtension', link: '/reference/simplewextension' },
                        { text: 'IJsonEngine', link: '/reference/ijsonengine' },
                        { text: 'HttpMiddleware', link: '/reference/httpmiddleware' },
                        { text: 'IHttpModule', link: '/reference/ihttpmodule' },
                        { text: 'IWebUser', link: '/reference/iwebuser' },
                        { text: 'TelemetryOptions', link: '/reference/telemetryoptions' },
                    ]
                },
            ]

        },

        socialLinks: [
            { icon: 'github', link: 'https://github.com/stratdev3/SimpleW' },
            { icon: 'discord', link: 'https://discord.gg/mDNRjyV8Ak' },
            //{ icon: 'nuget', link: 'https://www.nuget.org/packages/SimpleW' },
        ],

        footer: {
            message: 'Released under the MIT License.',
            copyright: 'Copyright © 2021-present Christophe CHATEAU'
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
        hostname: 'https://simplew.net'
    },
    ignoreDeadLinks: [
        // ignore all localhost links
        /^https?:\/\/localhost/,
    ],
})
