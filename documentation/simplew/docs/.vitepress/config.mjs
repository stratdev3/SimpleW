import { defineConfig } from 'vitepress'

// https://vitepress.dev/reference/site-config
export default defineConfig({
    appearance: 'dark',
    title: 'SimpleW',
    titleTemplate: 'SimpleW',
    description: "SimpleW is a lightweight web server for .NET. Simple by design. Standalone or embedded.",
    head: [
        ['link', { rel: 'icon', href: '/favicon.ico' }],
        // analytics
        ['script', { async: '', src: 'https://cloud.umami.is/script.js', 'data-website-id': '6cb0147f-6faa-4c64-ba68-6bd607980ea5' } ],
        // og
        ['meta', { property: 'og:site_name', content: 'SimpleW' }],
        ['meta', { property: 'og:title', content: 'SimpleW | Lightweight Web Server for .NET' }],
        ['meta', { property: 'og:description', content: 'A lightweight .NET web server with an embeddable core and powerful addons. Build standalone or embedded services without ASP.NET.' }],
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
                link: '/guide/getting-started',
                activeMatch: '/guide/'
            },
            {
                text: 'Addons',
                link: '/addons/addons',
                activeMatch: '/addons/'
            },
            {
                text: 'API Reference',
                link: '/reference/simplewserver',
                activeMatch: '/reference/'
            },
            //{ text: '📖✨ Features', link: '/features' },
            {
                text: 'v26.1.0',
                items: [
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
                        { text: 'Getting Started', link: '/guide/getting-started' },
                        { text: 'What is SimpleW?', link: '/guide/what-is-simplew' },
                    ]
                },
                {
                    text: 'Core',
                    items: [
                        { text: 'Server', link: '/guide/server' },
                        { text: 'Routing', link: '/guide/routing' },
                        { text: 'Handler', link: '/guide/handler' },
                        { text: 'Request', link: '/guide/request' },
                        { text: 'Response', link: '/guide/response' },
                    ]
                },
                {
                    text: 'Content & Realtime',
                    items: [
                        { text: 'Static Files', link: '/guide/staticfiles' },
                        { text: 'Websockets', link: '/guide/websockets' },
                        { text: 'Server Sent Events', link: '/guide/serversentevents' },
                    ]
                },
                {
                    text: 'Security',
                    items: [
                        { text: 'Principal', link: '/guide/principal' },
                        { text: 'CORS', link: '/guide/cors' },
                        { text: 'TLS Certificates', link: '/guide/tls-certificates' },
                    ]
                },
                {
                    text: 'Extensibility',
                    items: [
                        { text: 'Middleware', link: '/guide/middleware' },
                        { text: 'Module', link: '/guide/module' },
                        { text: 'Handler Metadata', link: '/guide/handler-attribute' },
                        { text: 'Result Handler', link: '/guide/resulthandler' },
                    ]
                },
                {
                    text: 'Operations',
                    items: [
                        { text: 'Logging', link: '/guide/logging' },
                        { text: 'Observability', link: '/guide/observability' },
                        { text: 'Performances', link: '/guide/performances' },
                    ]
                },
                {
                    text: 'Advanced',
                    items: [
                        { text: 'Controller Callback', link: '/guide/controller-callback' },
                        { text: 'Replacing Core Components', link: '/guide/replacing-core-components' },
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
                        { text: 'Background', link: '/addons/service-background' },
                        { text: 'Chaos', link: '/addons/service-chaos' },
                        { text: 'FileBrowser', link: '/addons/service-filebrowser' },
                        { text: 'Firewall', link: '/addons/service-firewall' },
                        { text: 'Jwt', link: '/addons/service-jwt' },
                        { text: 'Latency', link: '/addons/service-latency' },
                        { text: 'LetsEncrypt', link: '/addons/service-letsencrypt' },
                        { text: 'LiquidPages', link: '/addons/service-liquidpages' },
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
                    text: 'Network Engines',
                    items: [
                        { text: 'Ioxide', link: '/addons/engine-ioxide' },
                    ]
                },
                {
                    text: 'Json Engines',
                    items: [
                        { text: 'Newtonsoft', link: '/addons/jsonengine-newtonsoft' },
                    ]
                },
                {
                    text: 'Templates',
                    items: [
                        { text: 'SimpleW', link: '/addons/template-templates' },
                    ]
                },
            ],

            '/reference/': [
                {
                    text: 'Core',
                    items: [
                        { text: 'SimpleWServer', link: '/reference/simplewserver' },
                        { text: 'SimpleWServerOptions', link: '/reference/simplewserveroptions' },
                        { text: 'SimpleWEngineOptions', link: '/reference/simplewengineoptions' },
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
            { icon: 'discord', link: 'https://discord.gg/8rVv95jrMN' },
            //{ icon: 'nuget', link: 'https://www.nuget.org/packages/SimpleW' },
        ],

        footer: {
            message: 'Released under the MIT License.',
            copyright: 'Copyright © 2021-2026 <a href="#" onclick="location.href=\'mailto:\'+atob(\'Y29udGFjdEBzaW1wbGV3Lm5ldA==\');return false;">Christophe CHATEAU</a>'
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
