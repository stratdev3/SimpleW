import { defineConfig } from 'vitepress'

// https://vitepress.dev/reference/site-config
export default defineConfig({
    appearance: 'dark',
    title: 'SimpleW',
    titleTemplate: 'SimpleW',
    description: "SimpleW is a modern web server for .NET. Designed for Simplicity. Built for Speed. Packed with Power.",
    head: [
        ['link', { rel: 'icon', href: 'favicon.ico' }],
        // analytics
        ['script', { async: '', src: 'https://cloud.umami.is/script.js', 'data-website-id': '6cb0147f-6faa-4c64-ba68-6bd607980ea5' } ],
        // og
        ['meta', { property: 'og:site_name', content: 'SimpleW' }],
        ['meta', { property: 'og:title', content: 'SimpleW | Modern Web Server for .NET' }],
        ['meta', { property: 'og:description', content: 'A modern .NET web server built for speed and control. Handle APIs, static files, and dynamic content with a production-ready core and zero dependencies.' }],
        ['meta', { property: 'og:type', content: 'website' }],
        ['meta', { property: 'og:url', content: 'https://simplew.net' }],
        ['meta', { property: 'og:image', content: 'https://simplew.net/simplew-og.png' }],
    ],
    themeConfig: {
        // https://vitepress.dev/reference/default-theme-config
        logo: '/logo.svg',

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
                text: 'v26.0-rc',
                items: [
                    { text: 'v16.1.0', link: 'https://simplew.net/v16/' },
                    { text: 'Changelog', link: '/version/changelog' },
                    { text: 'FAQ', link: '/version/faq' },
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
                    text: 'Core',
                    items: [
                        { text: 'Server', link: '/guide/server' },
                        { text: 'Handler', link: '/guide/handler' },
                        { text: 'Routing', link: '/guide/routing' },
                        { text: 'Response', link: '/guide/response' },
                        { text: 'Request', link: '/guide/request' },
                    ]
                },
                {
                    text: 'Extensibility',
                    items: [
                        { text: 'Middleware', link: '/guide/middleware' },
                        { text: 'Attribute', link: '/guide/handler-attribute' },
                        { text: 'Module', link: '/guide/module' },
                        { text: 'Callback', link: '/guide/callback' },
                        { text: 'Result Handler', link: '/guide/resulthandler' },
                        { text: 'Json Engine', link: '/guide/jsonengine' },
                    ]
                },
                {
                    text: 'Security',
                    items: [
                        { text: 'Principal', link: '/guide/principal' },
                        { text: 'Cross-Origin Resource Sharing', link: '/guide/cors' },
                        { text: 'SSL Certificate', link: '/guide/ssl-certificate' },
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
                    text: 'Operations',
                    items: [
                        { text: 'Static Files', link: '/guide/staticfiles' },
                        { text: 'Observability', link: '/guide/observability' },
                        { text: 'Logging', link: '/guide/logging' },
                        { text: 'Unix Sockets', link: '/guide/unix-sockets' },
                    ]
                },
                {
                    text: 'How to',
                    link: '/guide/how-to'
                },
            ],

            '/addons/': [
                {
                    text: 'Services',
                    items: [
                        { text: 'BasicAuth', link: '/addons/service-basicauth' },
                        { text: 'Chaos', link: '/addons/service-chaos' },
                        { text: 'Firewall', link: '/addons/service-firewall' },
                        { text: 'Jwt', link: '/addons/service-jwt' },
                        { text: 'Latency', link: '/addons/service-latency' },
                        { text: 'LetsEncrypt', link: '/addons/service-letsencrypt' },
                        { text: 'OpenID', link: '/addons/service-openid' },
                    ]
                },
                {
                    text: 'Helpers',
                    items: [
                        { text: 'BasicAuth', link: '/addons/helper-basicauth' },
                        { text: 'Dependency Injection', link: '/addons/helper-dependency-injection' },
                        { text: 'Hosting', link: '/addons/helper-hosting' },
                        { text: 'Jwt', link: '/addons/helper-jwt' },
                        { text: 'Log4net', link: '/addons/helper-log4net' },
                        { text: 'OpenID', link: '/addons/helper-openid' },
                        { text: 'Razor', link: '/addons/helper-razor' },
                        { text: 'Serilog', link: '/addons/helper-serilog' },
                        { text: 'Swagger', link: '/addons/helper-swagger' },
                    ]
                },
                {
                    text: 'Templates',
                    items: [
                        { text: 'SimpleW', link: '/addons/template-templates' },
                    ]
                },
                {
                    text: 'JsonEngines',
                    items: [
                        { text: 'Newtonsoft', link: '/addons/jsonengine-newtonsoft' },
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
                        { text: 'HttpPrincipal', link: '/reference/httpprincipal' },
                        { text: 'HttpIdentity', link: '/reference/httpidentity' },
                        { text: 'HttpBag', link: '/reference/httpbag' },
                        { text: 'HttpMiddleware', link: '/reference/httpmiddleware' },
                        { text: 'IHttpModule', link: '/reference/ihttpmodule' },
                        { text: 'ISimpleWEngine', link: '/reference/isimplewengine' },
                        { text: 'IJsonEngine', link: '/reference/ijsonengine' },
                        { text: 'IdentityProperty', link: '/reference/identityproperty' },
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
                        { text: 'SseModule', link: '/reference/serversenteventsmodule' },
                        { text: 'WebsocketModule', link: '/reference/websocketmodule' },
                    ]
                },
                {
                    text: 'Helpers',
                    items: [
                        { text: 'SimpleWExtension', link: '/reference/simplewextension' },
                        { text: 'Logger', link: '/reference/logger' },
                        { text: 'TelemetryOptions', link: '/reference/telemetryoptions' },
                    ]
                },
            ],

            '/version/': [
                {
                    items: [
                        { text: 'Changelog', link: './changelog' },
                        { text: 'FAQ', link: './faq' },
                    ]
                },
            ],

        },

        socialLinks: [
            { icon: 'github', link: 'https://github.com/stratdev3/SimpleW' },
            { icon: 'discord', link: 'https://discord.gg/mDNRjyV8Ak' },
            //{ icon: 'nuget', link: 'https://www.nuget.org/packages/SimpleW' },
        ],

        footer: {
            message: 'Released under the MIT License.',
            copyright: 'Copyright © 2021-present <a href="#" onclick="location.href=\'mailto:\'+atob(\'Y29udGFjdEBzaW1wbGV3Lm5ldA==\');return false;">Christophe CHATEAU</a>'
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
