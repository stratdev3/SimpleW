<script setup>
import { computed, nextTick, ref } from 'vue'

const capabilityGroups = [
  {
    id: 'core',
    label: 'CORE',
    defaultExample: 'minimal-routing',
    items: [
      { id: 'minimal-routing', title: 'Minimal routing', description: 'One route, one JSON response.' },
      { id: 'controller-routing', title: 'Controller routing', description: 'Group routes in a controller.' },
      { id: 'static-files', title: 'Static files', description: 'Serve a cached frontend directory.' },
      { id: 'websockets', title: 'WebSockets', description: 'Reply to real-time operations.' },
      { id: 'observability', title: 'Observability', description: 'Enable traces and metrics.' }
    ]
  },
  {
    id: 'addons',
    label: 'ADDONS',
    defaultExample: 'basic-authentication',
    items: [
      { id: 'basic-authentication', title: 'Basic authentication', description: 'Protect one route with one user.' },
      { id: 'firewall', title: 'Firewall', description: 'Filter traffic before routing.' },
      { id: 'lets-encrypt', title: "Let's Encrypt", description: 'Issue and renew HTTPS certificates.' },
      { id: 'background-task', title: 'Background task', description: 'Accept work and run it later.' },
      { id: 'swagger', title: 'Swagger / OpenAPI', description: 'Expose interactive API documentation.' }
    ]
  }
]

const examples = {
  'minimal-routing': {
    title: 'Minimal routing',
    family: 'core',
    package: 'INCLUDED',
    guide: './guide/getting-started',
    code: `<span class="token-keyword">using</span> System.Net;
<span class="token-keyword">using</span> SimpleW;

<span class="token-keyword">var</span> server = <span class="token-keyword">new</span> SimpleWServer(IPAddress.Any, <span class="token-number">8080</span>);

<span class="code-focus">server.MapGet(<span class="token-string">"/hello"</span>, () =&gt; {
    <span class="token-keyword">return new</span> { message = <span class="token-string">"Hello from SimpleW"</span> };
});</span>
<span class="token-keyword">await</span> server.RunAsync();`
  },
  'controller-routing': {
    title: 'Controller routing',
    family: 'core',
    package: 'INCLUDED',
    guide: './guide/routing',
    code: `<span class="token-keyword">using</span> System.Net;
<span class="token-keyword">using</span> SimpleW;

<span class="token-keyword">var</span> server = <span class="token-keyword">new</span> SimpleWServer(IPAddress.Any, <span class="token-number">8080</span>);

<span class="code-focus">server.MapController&lt;HelloController&gt;();</span>
<span class="token-keyword">await</span> server.RunAsync();

<span class="code-focus"><span class="token-keyword">class</span> HelloController : Controller {
    [Route(<span class="token-string">"GET"</span>, <span class="token-string">"/hello"</span>)]
    <span class="token-keyword">public</span> object Get() {
        <span class="token-keyword">return new</span> { message = <span class="token-string">"Hello World"</span> };
    }
}</span>`
  },
  'static-files': {
    title: 'Static files',
    family: 'core',
    package: 'INCLUDED',
    guide: './guide/staticfiles',
    code: `<span class="token-keyword">using</span> System;
<span class="token-keyword">using</span> System.Net;
<span class="token-keyword">using</span> SimpleW;
<span class="token-keyword">using</span> SimpleW.Modules;

<span class="token-keyword">var</span> server = <span class="token-keyword">new</span> SimpleWServer(IPAddress.Any, <span class="token-number">8080</span>);

<span class="code-focus">server.UseStaticFilesModule(options =&gt; {
    options.Path = <span class="token-string">"wwwroot"</span>;
    options.Prefix = <span class="token-string">"/assets"</span>;
    options.CacheTimeout = TimeSpan.FromDays(<span class="token-number">1</span>);
});</span>
<span class="token-keyword">await</span> server.RunAsync();`
  },
  websockets: {
    title: 'WebSockets',
    family: 'core',
    package: 'INCLUDED',
    guide: './guide/websockets',
    code: `<span class="token-keyword">using</span> System.Net;
<span class="token-keyword">using</span> SimpleW;
<span class="token-keyword">using</span> SimpleW.Modules;

<span class="token-keyword">var</span> server = <span class="token-keyword">new</span> SimpleWServer(IPAddress.Any, <span class="token-number">8080</span>);

<span class="code-focus">server.UseWebSocketModule(ws =&gt; {
    ws.Prefix = <span class="token-string">"/ws"</span>;
    ws.Map(<span class="token-string">"ping"</span>, <span class="token-keyword">async</span> (connection, context, message) =&gt; {
        <span class="token-keyword">await</span> connection.SendTextAsync(<span class="token-string">"{\\"op\\":\\"pong\\"}"</span>);
    });
});</span>
<span class="token-keyword">await</span> server.RunAsync();`
  },
  observability: {
    title: 'Observability',
    family: 'core',
    package: 'INCLUDED',
    guide: './guide/observability',
    code: `<span class="token-keyword">using</span> System.Net;
<span class="token-keyword">using</span> SimpleW;

<span class="token-keyword">var</span> server = <span class="token-keyword">new</span> SimpleWServer(IPAddress.Any, <span class="token-number">8080</span>);

server.MapGet(<span class="token-string">"/hello"</span>, () =&gt; <span class="token-string">"Hello"</span>);

<span class="code-focus">server.ConfigureTelemetry(options =&gt; {
    options.Enabled = <span class="token-keyword">true</span>;
    options.InstanceId = <span class="token-string">"api-01"</span>;
});</span>
<span class="token-keyword">await</span> server.RunAsync();`
  },
  'basic-authentication': {
    title: 'Basic authentication',
    family: 'addons',
    package: 'SimpleW.Service.BasicAuth',
    guide: './addons/service-basicauth',
    code: `<span class="token-keyword">using</span> System.Net;
<span class="token-keyword">using</span> SimpleW;
<span class="token-keyword">using</span> SimpleW.Helper.BasicAuth;
<span class="token-keyword">using</span> SimpleW.Service.BasicAuth;

<span class="token-keyword">var</span> server = <span class="token-keyword">new</span> SimpleWServer(IPAddress.Any, <span class="token-number">8080</span>);
server.MapGet(<span class="token-string">"/private"</span>, Private);

<span class="code-focus">server.UseBasicAuthModule(options =&gt; {
    options.Users = [
        <span class="token-keyword">new</span> BasicUser(<span class="token-string">"admin"</span>, <span class="token-string">"secret"</span>)
    ];
});</span>
<span class="token-keyword">await</span> server.RunAsync();

<span class="code-focus">[BasicAuth(<span class="token-string">"Private"</span>)]
<span class="token-keyword">static</span> object Private(HttpSession session) =&gt; <span class="token-keyword">new</span> {
    user = session.Principal.Name
};</span>`
  },
  firewall: {
    title: 'Firewall',
    family: 'addons',
    package: 'SimpleW.Service.Firewall',
    guide: './addons/service-firewall',
    code: `<span class="token-keyword">using</span> System;
<span class="token-keyword">using</span> System.Net;
<span class="token-keyword">using</span> SimpleW;
<span class="token-keyword">using</span> SimpleW.Service.Firewall;

<span class="token-keyword">var</span> server = <span class="token-keyword">new</span> SimpleWServer(IPAddress.Any, <span class="token-number">8080</span>);
server.MapGet(<span class="token-string">"/hello"</span>, () =&gt; <span class="token-string">"Hello"</span>);

<span class="code-focus">server.UseFirewallModule(options =&gt; {
    options.DenyRules.Add(IpRule.Cidr(<span class="token-string">"203.0.113.0/24"</span>));
    options.GlobalRateLimit = <span class="token-keyword">new</span> RateLimitOptions {
        Limit = <span class="token-number">100</span>,
        Window = TimeSpan.FromMinutes(<span class="token-number">1</span>)
    };
});</span>
<span class="token-keyword">await</span> server.RunAsync();`
  },
  'lets-encrypt': {
    title: "Let's Encrypt",
    family: 'addons',
    package: 'SimpleW.Service.Letsencrypt',
    guide: './addons/service-letsencrypt',
    code: `<span class="token-keyword">using</span> System.Net;
<span class="token-keyword">using</span> SimpleW;
<span class="token-keyword">using</span> SimpleW.Service.Letsencrypt;

<span class="token-keyword">var</span> server = <span class="token-keyword">new</span> SimpleWServer(IPAddress.Any, <span class="token-number">443</span>);
server.MapGet(<span class="token-string">"/hello"</span>, () =&gt; <span class="token-string">"Hello"</span>);

<span class="code-focus">server.UseLetsEncryptModule(options =&gt; {
    options.Email = <span class="token-string">"admin@example.com"</span>;
    options.Domains = [ <span class="token-string">"example.com"</span> ];
    options.StoragePath = <span class="token-string">"./letsencrypt"</span>;
});</span>
<span class="token-keyword">await</span> server.RunAsync();`
  },
  'background-task': {
    title: 'Background task',
    family: 'addons',
    package: 'SimpleW.Service.Background',
    guide: './addons/service-background',
    code: `<span class="token-keyword">using</span> System.Net;
<span class="token-keyword">using</span> System.Threading.Tasks;
<span class="token-keyword">using</span> SimpleW;
<span class="token-keyword">using</span> SimpleW.Service.Background;

<span class="token-keyword">var</span> server = <span class="token-keyword">new</span> SimpleWServer(IPAddress.Any, <span class="token-number">8080</span>);

<span class="code-focus">server.UseBackgroundModule();

server.MapPost(<span class="token-string">"/reports"</span>, (HttpSession session) =&gt; {
    <span class="token-keyword">var</span> job = session.GetBackgroundService().Enqueue(
        <span class="token-string">"report"</span>,
        <span class="token-keyword">async</span> ctx =&gt; <span class="token-keyword">await</span> Task.Delay(<span class="token-number">5000</span>, ctx.CancellationToken)
    );

    <span class="token-keyword">return</span> session.Response
                  .Status(<span class="token-number">202</span>)
                  .Json(<span class="token-keyword">new</span> { jobId = job.Id });
});</span>
<span class="token-keyword">await</span> server.RunAsync();`
  },
  swagger: {
    title: 'Swagger / OpenAPI',
    family: 'addons',
    package: 'SimpleW.Helper.Swagger',
    guide: './addons/helper-swagger',
    code: `<span class="token-keyword">using</span> System.Net;
<span class="token-keyword">using</span> SimpleW;
<span class="token-keyword">using</span> SimpleW.Helper.Swagger;

<span class="token-keyword">var</span> server = <span class="token-keyword">new</span> SimpleWServer(IPAddress.Any, <span class="token-number">8080</span>);

server.MapGet(<span class="token-string">"/api/hello"</span>, (string name) =&gt; {
    <span class="token-keyword">return new</span> { message = $<span class="token-string">"Hello {name}"</span> };
});

<span class="code-focus">server.MapGet(<span class="token-string">"/swagger.json"</span>, <span class="token-keyword">static</span> (HttpSession session) =&gt; {
    <span class="token-keyword">return</span> Swagger.Json(session);
});
server.MapGet(<span class="token-string">"/swagger"</span>, <span class="token-keyword">static</span> (HttpSession session) =&gt; {
    <span class="token-keyword">return</span> Swagger.UI(session);
});</span>
<span class="token-keyword">await</span> server.RunAsync();`
  }
}

const activeFamily = ref('core')
const selectedId = ref('minimal-routing')
const selectedExample = computed(() => examples[selectedId.value])
const codePanelStyle = computed(() => {
  const code = selectedExample.value.code
  const lineCount = code.split('\n').length
  const focusCount = code.match(/class="code-focus"/g)?.length ?? 0

  return {
    '--code-height': `${Math.min(500, Math.ceil(lineCount * 20.64 + focusCount * 8 + 46))}px`,
    '--code-height-mobile': `${Math.min(500, Math.ceil(lineCount * 18.92 + focusCount * 8 + 38))}px`
  }
})

function selectCapability(item, family) {
  selectedId.value = item.id
  activeFamily.value = family
}

function selectFamily(family) {
  const group = capabilityGroups.find(item => item.id === family)
  activeFamily.value = family
  selectedId.value = group.defaultExample
}

async function moveFamily(offset) {
  const currentIndex = capabilityGroups.findIndex(group => group.id === activeFamily.value)
  const nextIndex = (currentIndex + offset + capabilityGroups.length) % capabilityGroups.length
  const nextFamily = capabilityGroups[nextIndex].id

  selectFamily(nextFamily)
  await nextTick()
  document.getElementById(`family-tab-${nextFamily}`)?.focus()
}
</script>

<template>
  <div class="home-overview">
    <section class="home-section explorer" aria-labelledby="explorer-title">
      <header class="section-heading">
        <p class="section-kicker">Examples</p>
        <h2 id="explorer-title">One feature. One clear example.</h2>
        <p>Choose a capability and see the complete SimpleW program behind it.</p>
      </header>

      <div class="explorer-grid">
        <div class="capability-selector">
          <div class="family-tabs" role="tablist" aria-label="Example family">
            <button
              v-for="group in capabilityGroups"
              :id="`family-tab-${group.id}`"
              :key="group.id"
              type="button"
              role="tab"
              :class="`family-tab-${group.id}`"
              :aria-controls="`capability-group-${group.id}`"
              :aria-selected="activeFamily === group.id"
              :tabindex="activeFamily === group.id ? 0 : -1"
              @click="selectFamily(group.id)"
              @keydown.left.prevent="moveFamily(-1)"
              @keydown.right.prevent="moveFamily(1)"
            >
              {{ group.label }}
            </button>
          </div>

          <section
            v-for="group in capabilityGroups"
            :id="`capability-group-${group.id}`"
            :key="group.id"
            class="capability-group"
            :class="[`capability-${group.id}`, { 'is-hidden': activeFamily !== group.id }]"
            role="tabpanel"
            :aria-labelledby="`family-tab-${group.id}`"
          >
            <div class="capability-list">
              <button
                v-for="item in group.items"
                :key="item.id"
                type="button"
                class="capability-action"
                :class="{ 'is-selected': selectedId === item.id }"
                :aria-pressed="selectedId === item.id"
                aria-controls="home-code-preview"
                @click="selectCapability(item, group.id)"
              >
                <span class="capability-copy">
                  <strong>{{ item.title }}</strong>
                  <small>{{ item.description }}</small>
                </span>
                <span class="capability-meta">CODE</span>
              </button>
            </div>
          </section>
        </div>

        <aside
          class="code-workbench"
          :class="`code-${selectedExample.family}`"
          :style="codePanelStyle"
          aria-label="Selected SimpleW code example"
        >
          <header class="workbench-heading">
            <span class="workbench-file">PROGRAM.CS</span>
            <div class="workbench-context">
              <div>
                <strong>{{ selectedExample.title }}</strong>
                <span>{{ selectedExample.package }}</span>
              </div>
              <a :href="selectedExample.guide">View guide -&gt;</a>
            </div>
          </header>

          <div class="code-panel">
            <Transition name="code-swap" mode="out-in">
              <pre :key="selectedId" id="home-code-preview"><code v-html="selectedExample.code"></code></pre>
            </Transition>
          </div>

          <p class="visually-hidden" aria-live="polite">{{ selectedExample.title }} code example selected.</p>
        </aside>
      </div>
    </section>
  </div>
</template>

<style scoped>
.home-overview {
  --home-cyan: #22d3ee;
  --home-violet: #a855f7;
  width: 100%;
  margin: 0 auto;
  padding-top: 52px;
}

.home-section {
  padding: 52px 0;
  border-top: 1px solid var(--vp-c-divider);
}

.section-heading {
  max-width: 760px;
  margin-bottom: 26px;
}

.section-kicker {
  margin: 0 0 8px;
  color: var(--vp-c-text-3);
  font-family: ui-monospace, monospace;
  font-size: 11px;
  font-weight: 700;
  line-height: 1.4;
  letter-spacing: 0;
  text-transform: uppercase;
}

.section-heading h2 {
  margin: 0;
  padding: 0;
  border: 0;
  color: var(--vp-c-text-1);
  font-size: 32px;
  line-height: 1.2;
  letter-spacing: 0;
}

.section-heading > p:last-child {
  margin: 14px 0 0;
  color: var(--vp-c-text-2);
  font-size: 16px;
  line-height: 1.65;
}

.explorer-grid {
  display: grid;
  grid-template-columns: minmax(280px, 0.64fr) minmax(0, 1.36fr);
  gap: 28px;
  align-items: start;
}

.capability-selector {
  overflow: hidden;
  border-top: 1px solid var(--vp-c-divider);
  border-bottom: 1px solid var(--vp-c-divider);
}

.family-tabs {
  display: flex;
  min-height: 45px;
  padding: 9px 10px;
  align-items: center;
  gap: 8px;
  border-bottom: 1px solid var(--vp-c-divider);
}

.family-tabs button {
  --tab-accent: var(--home-cyan);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 74px;
  height: 25px;
  margin: 0;
  padding: 0 10px;
  border: 1px solid var(--tab-accent);
  border-radius: 3px;
  background: transparent;
  color: var(--tab-accent);
  font-family: ui-monospace, monospace;
  font-size: 10px;
  font-weight: 700;
  line-height: 1;
  letter-spacing: 0;
  cursor: pointer;
  opacity: 0.62;
}

.family-tabs .family-tab-addons { --tab-accent: var(--home-violet); }

.family-tabs button:hover,
.family-tabs button[aria-selected="true"] {
  background: color-mix(in srgb, var(--tab-accent) 10%, transparent);
  opacity: 1;
}

.family-tabs button:focus-visible {
  outline: 2px solid var(--tab-accent);
  outline-offset: 2px;
}

.capability-group { --group-accent: var(--home-cyan); }
.capability-addons { --group-accent: var(--home-violet); }
.capability-group.is-hidden { display: none; }

.capability-action {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 12px;
  align-items: center;
  width: 100%;
  min-height: 61px;
  margin: 0;
  padding: 8px 10px;
  border: 0;
  border-bottom: 1px solid var(--vp-c-divider);
  border-radius: 0;
  background: transparent;
  color: var(--vp-c-text-1);
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.capability-action:last-child { border-bottom: 0; }

.capability-action:hover,
.capability-action.is-selected {
  background: color-mix(in srgb, var(--group-accent) 7%, transparent);
}

.capability-action.is-selected { box-shadow: inset 2px 0 var(--group-accent); }

.capability-action:focus-visible {
  outline: 2px solid var(--group-accent);
  outline-offset: -2px;
}

.capability-copy {
  display: block;
  min-width: 0;
}

.capability-copy strong,
.capability-copy small { display: block; }

.capability-copy strong {
  color: var(--vp-c-text-1);
  font-size: 13px;
  font-weight: 700;
  line-height: 1.35;
}

.capability-copy small {
  margin-top: 3px;
  color: var(--vp-c-text-3);
  font-size: 11px;
  line-height: 1.35;
}

.capability-meta {
  color: var(--vp-c-text-3);
  font-family: ui-monospace, monospace;
  font-size: 9px;
  font-weight: 700;
  line-height: 1.3;
}

.capability-action.is-selected .capability-meta,
.capability-action:hover .capability-meta { color: var(--group-accent); }

.code-workbench {
  --code-accent: var(--home-cyan);
  position: relative;
  min-width: 0;
  overflow: hidden;
  border: 1px solid var(--vp-c-divider);
  border-radius: 6px;
  background: var(--vp-code-block-bg);
}

.code-workbench.code-addons { --code-accent: var(--home-violet); }

.workbench-heading {
  height: 82px;
  border-bottom: 1px solid var(--vp-c-divider);
}

.workbench-file {
  display: block;
  height: 30px;
  padding: 0 18px;
  border-bottom: 1px solid var(--vp-c-divider);
  color: var(--vp-c-text-3);
  font-family: ui-monospace, monospace;
  font-size: 9px;
  font-weight: 700;
  line-height: 30px;
}

.workbench-context {
  display: flex;
  height: 51px;
  padding: 8px 18px;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

.workbench-context > div {
  flex: 1 1 auto;
  min-width: 0;
}
.workbench-context strong,
.workbench-context span {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.workbench-context strong {
  color: var(--vp-c-text-1);
  font-size: 13px;
  line-height: 1.35;
}

.workbench-context span {
  margin-top: 2px;
  color: var(--code-accent);
  font-family: ui-monospace, monospace;
  font-size: 9px;
  font-weight: 700;
  line-height: 1.35;
}

.workbench-context a {
  flex: 0 0 auto;
  color: var(--vp-c-text-2);
  font-family: ui-monospace, monospace;
  font-size: 10px;
  font-weight: 700;
  text-decoration: none;
  white-space: nowrap;
}

.workbench-context a:hover { color: var(--code-accent); }

.workbench-context a:focus-visible {
  outline: 2px solid var(--code-accent);
  outline-offset: 3px;
}

.code-panel {
  height: var(--code-height);
  min-width: 0;
  overflow: hidden;
  transition: height 0.22s ease;
}

.code-panel pre {
  box-sizing: border-box;
  height: 100%;
  margin: 0;
  padding: 22px;
  overflow: auto;
  background: transparent;
}

.code-panel code {
  display: block;
  min-width: max-content;
  color: var(--vp-code-block-color);
  font-family: var(--vp-font-family-mono);
  font-size: 12px;
  line-height: 1.72;
  white-space: pre;
}

.code-panel :deep(.token-keyword) { color: #c084fc; }
.code-panel :deep(.token-number) { color: #f59e0b; }
.code-panel :deep(.token-string) { color: #34d399; }

.code-panel :deep(.code-focus) {
  display: block;
  margin: 0 -22px;
  padding: 4px 20px;
  border-left: 2px solid var(--code-accent);
  background: color-mix(in srgb, var(--code-accent) 7%, transparent);
}

.code-swap-enter-active,
.code-swap-leave-active { transition: opacity 0.16s ease, transform 0.16s ease; }

.code-swap-enter-from {
  opacity: 0;
  transform: translateY(4px);
}

.code-swap-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}

.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

@media (max-width: 959px) {
  .home-overview { padding-top: 44px; }
  .home-section { padding: 44px 0; }
  .section-heading { margin-bottom: 22px; }
  .section-heading h2 { font-size: 27px; }

  .explorer-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 18px;
  }

  .capability-action {
    min-height: 66px;
    padding-right: 12px;
    padding-left: 12px;
  }
}

@media (max-width: 480px) {
  .section-heading > p:last-child { font-size: 15px; }

  .code-panel { height: var(--code-height-mobile); }

  .workbench-context {
    padding-right: 12px;
    padding-left: 12px;
  }

  .workbench-context a { font-size: 9px; }

  .code-panel pre {
    padding: 18px 16px;
  }

  .code-panel code {
    font-size: 11px;
    line-height: 1.72;
  }

  .code-panel :deep(.code-focus) {
    margin-right: -16px;
    margin-left: -16px;
    padding-right: 14px;
    padding-left: 14px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .code-panel,
  .code-swap-enter-active,
  .code-swap-leave-active { transition: none; }
}
</style>
