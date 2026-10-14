<script setup>
import { computed, onBeforeUnmount, ref } from 'vue'
import { withBase } from 'vitepress'

const features = [
  {
    id: 'files', title: 'Serve your frontend',
    description: 'Serve HTML, CSS and JavaScript from www.',
    namespace: 'SimpleW.Modules',
    code: `server.UseStaticFilesModule(options => {
    options.Path = "www";
    options.Prefix = "/";
});`
  },
  {
    id: 'firewall', title: 'Control your traffic',
    description: 'Limit each IP to 100 requests per minute.',
    namespace: 'SimpleW.Service.Firewall',
    package: 'SimpleW.Service.Firewall',
    docs: '/addons/service-firewall',
    code: `server.UseFirewallModule(options => {
    options.GlobalRateLimit = new RateLimitOptions {
        Limit = 100,
        Window = TimeSpan.FromMinutes(1)
    };
});`
  },
  {
    id: 'letsencrypt', title: 'Enable HTTPS',
    description: "Automatically renew certificates with Let's Encrypt.",
    namespace: 'SimpleW.Service.LetsEncrypt',
    package: 'SimpleW.Service.LetsEncrypt',
    docs: '/addons/service-letsencrypt',
    code: `server.UseLetsEncryptModule(options => {
    options.Domains = ["example.com"];
    options.Email = "admin@example.com";
});`
  }
]
const selected = ref([])
const latestAdded = ref(null)
const copyState = ref('idle')
const announcement = ref('')
let highlightTimer
let copyTimer

// Register the firewall before static files, independently of the option order.
const activeFeatures = computed(() => features
  .filter(feature => selected.value.includes(feature.id))
  .sort((a, b) => Number(b.id === 'firewall') - Number(a.id === 'firewall')))
const requiredAddons = computed(() => activeFeatures.value.filter(feature => feature.package))
const blocks = computed(() => [
  { id: 'imports', code: [
    ...(selected.value.includes('firewall') ? ['System'] : []),
    'System.Net', 'SimpleW',
    ...activeFeatures.value.map(feature => feature.namespace).filter(Boolean)
  ].map(namespace => `using ${namespace};`).join('\n') },
  { id: 'server', code: `var server = new SimpleWServer(IPAddress.Any, ${selected.value.includes('letsencrypt') ? 80 : 8080});` },
  { id: 'hello', code: 'server.MapGet("/api/hello", () => new { message = "Hello world!" });' },
  ...activeFeatures.value,
  { id: 'run', code: 'await server.RunAsync();' }
])
const program = computed(() => blocks.value.map(block => block.code).join('\n\n'))
const displayLineCount = computed(() => program.value.split('\n').length)
const lineCount = computed(() => program.value.split('\n').filter(line => line.trim()).length)
const additions = computed(() => selected.value.length)

// Highlight only the escaped C# snippets defined in this component.
function highlight(source) {
  const escaped = source.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  return escaped.replace(/(\$?"[^"\n]*")|\b(using|var|new|await|string|true)\b|\b(\d+)\b/g,
    (match, string, keyword) => `<span class="syntax-${string ? 'string' : keyword ? 'keyword' : 'number'}">${match}</span>`)
}
function toggleFeature(feature) {
  const removing = selected.value.includes(feature.id)
  selected.value = removing
    ? selected.value.filter(id => id !== feature.id)
    : [...selected.value, feature.id]
  latestAdded.value = removing ? null : feature.id
  copyState.value = 'idle'
  clearTimeout(highlightTimer)
  clearTimeout(copyTimer)
  announcement.value = `${feature.title} ${removing ? 'removed' : 'added'}. Program updated.`
  highlightTimer = setTimeout(() => { latestAdded.value = null }, 1800)
}
function reset() {
  selected.value = []
  latestAdded.value = null
  copyState.value = 'idle'
  clearTimeout(highlightTimer)
  clearTimeout(copyTimer)
  announcement.value = 'Back to Hello World.'
}
async function copyProgram() {
  const snapshot = program.value
  try {
    await navigator.clipboard.writeText(snapshot)
    // A selection made while the clipboard resolves must not appear copied.
    if (snapshot !== program.value) return
    copyState.value = 'copied'
    announcement.value = 'Complete program copied.'
    clearTimeout(copyTimer)
    copyTimer = setTimeout(() => { copyState.value = 'idle' }, 2200)
  } catch {
    copyState.value = 'error'
    announcement.value = 'Copy unavailable. Select the code and copy it manually.'
  }
}
onBeforeUnmount(() => {
  clearTimeout(highlightTimer)
  clearTimeout(copyTimer)
})
</script>

<template>
  <section class="server-composer" aria-labelledby="composer-title">
    <div class="composer-controls">
      <header class="composer-intro">
        <h2 id="composer-title">Start small.<br /><span>Add what you need.</span></h2>
        <p>A working server in a few lines. Make it yours.</p>
      </header>

      <div class="included-feature"><span class="included-check" aria-hidden="true">✓</span><span>Hello World API</span><span class="included-label">Included</span></div>
      <div class="feature-options" role="group" aria-label="Add capabilities to your server">
        <button v-for="feature in features" :key="feature.id" type="button" role="switch"
          class="feature-option" :class="{ 'is-selected': selected.includes(feature.id) }"
          :aria-checked="selected.includes(feature.id)" :aria-labelledby="`feature-title-${feature.id}`"
          :aria-describedby="`feature-description-${feature.id}`" aria-controls="composed-program"
          @click="toggleFeature(feature)">
          <span class="feature-copy"><strong :id="`feature-title-${feature.id}`">{{ feature.title }}</strong><span :id="`feature-description-${feature.id}`">{{ feature.description }}</span></span>
          <span class="feature-switch" aria-hidden="true"><span></span></span>
        </button>
      </div>

      <div class="selection-summary">
        <span>{{ additions ? `${additions} ${additions === 1 ? 'option' : 'options'} added` : 'Just the essentials' }}</span>
        <button type="button" :disabled="!additions" @click="reset">Reset</button>
      </div>
      <a class="getting-started" :href="withBase('/guide/getting-started')">Run your first SimpleW app →</a>
    </div>

    <div class="composer-preview">
      <header class="program-heading">
        <span>Your program <span class="program-language">C#</span></span>
        <button type="button" class="copy-program" @click="copyProgram">
          <svg v-if="copyState !== 'copied'" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><rect x="8" y="8" width="12" height="13" rx="2"/><path d="M16 8V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h3"/></svg>
          <span v-else aria-hidden="true">✓</span>
          {{ copyState === 'copied' ? 'Copied!' : 'Copy program' }}
        </button>
      </header>
      <pre id="composed-program" class="program-preview" :style="{ '--program-lines': displayLineCount }" tabindex="0" aria-label="Complete generated C# program"><code><span v-for="(block, index) in blocks" :key="block.id" class="program-block" :class="{ 'just-added': latestAdded === block.id, 'optional-block': selected.includes(block.id) }" v-html="highlight(block.code) + (index < blocks.length - 1 ? '\n\n' : '')"></span></code></pre>
      <footer class="program-footer"><span>{{ lineCount }} lines of C#</span><span>{{ requiredAddons.length ? `SimpleW + ${requiredAddons.length} ${requiredAddons.length === 1 ? 'addon' : 'addons'}` : 'All in SimpleW core' }}</span></footer>
      <div v-if="selected.includes('files') || requiredAddons.length || copyState === 'error'" class="program-notes">
        <p v-for="addon in requiredAddons" :key="addon.id">Requires <a :href="withBase(addon.docs)">{{ addon.package }} ↗</a>.</p>
        <p v-if="selected.includes('letsencrypt')">Replace the domain and email with yours. Point DNS to your server and expose ports 80 (validation) and 443 (HTTPS).</p>
        <p v-if="selected.includes('files')">Place your frontend files in <code>www</code> before running.</p>
        <p v-if="copyState === 'error'">Copy unavailable. Select the code to copy it manually.</p>
      </div>
    </div>
    <p class="sr-only" role="status">{{ announcement }}</p>
  </section>
</template>

<style scoped>
.server-composer {
  --composer-accent: color-mix(in srgb, var(--vp-c-brand-1) 55%, #a855f7);
  --composer-soft: color-mix(in srgb, var(--composer-accent) 6%, var(--vp-c-bg));
  --composer-border: color-mix(in srgb, var(--composer-accent) 14%, var(--vp-c-divider));
  display: grid;
  grid-template-columns: minmax(0, .8fr) minmax(0, 1.2fr);
  align-items: start;
  gap: clamp(32px, 5vw, 72px);
  margin-top: 80px;
  padding-top: 48px;
  border-top: 1px solid var(--vp-c-divider);
  overflow-anchor: none;
}
.composer-controls { min-width: 0; }
.composer-intro h2 { margin: 0; padding: 0; border: 0; color: var(--vp-c-text-1); font-size: clamp(36px, 3.5vw, 48px); font-weight: 650; line-height: 1.08; letter-spacing: -.04em; }
.composer-intro h2 > span { color: var(--composer-accent); }
.composer-intro p { max-width: 360px; margin: 20px 0 30px; color: var(--vp-c-text-2); font-size: 16px; line-height: 1.65; }
.included-feature { display: flex; align-items: center; gap: 10px; padding: 15px 0; color: var(--vp-c-text-2); font-size: 13px; }
.included-check { color: var(--composer-accent); font-size: 16px; }
.included-label { margin-left: auto; color: var(--vp-c-text-3); font-size: 11px; }
.feature-options { border-top: 1px solid var(--composer-border); }
.feature-option { display: flex; align-items: center; justify-content: space-between; gap: 20px; width: 100%; min-height: 88px; padding: 18px 12px; border-bottom: 1px solid var(--composer-border); text-align: left; cursor: pointer; transition: background .18s ease; }
.feature-option:hover, .feature-option.is-selected { background: var(--composer-soft); }
.feature-copy { min-width: 0; }
.feature-copy strong { display: block; color: var(--vp-c-text-1); font-size: 14px; font-weight: 600; line-height: 1.5; }
.feature-copy > span { display: block; max-width: 300px; margin-top: 4px; color: var(--vp-c-text-2); font-size: 12px; line-height: 1.55; }
.feature-switch { flex: 0 0 34px; width: 34px; height: 20px; padding: 3px; border-radius: 12px; background: var(--vp-c-divider); transition: background .18s; }
.feature-switch > span { display: block; width: 14px; height: 14px; border-radius: 50%; background: var(--vp-c-text-3); transition: transform .18s, background .18s; }
.is-selected .feature-switch { background: var(--composer-accent); }
.is-selected .feature-switch > span { background: white; transform: translateX(14px); }
.selection-summary { display: flex; align-items: center; justify-content: space-between; min-height: 44px; color: var(--vp-c-text-3); font-size: 11px; }
.selection-summary button { padding: 6px 12px; color: var(--vp-c-text-2); text-decoration: underline; text-underline-offset: 3px; }
.selection-summary button:disabled { opacity: .4; cursor: default; text-decoration: none; }
.getting-started { display: inline-flex; align-items: center; gap: 20px; margin-top: 20px; color: var(--composer-accent); font-size: 13px; font-weight: 600; text-decoration: none; }
.getting-started:hover { text-decoration: underline; text-underline-offset: 4px; }
.composer-preview { min-width: 0; background: var(--composer-soft); }
.program-heading { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 20px 26px 0; color: var(--vp-c-text-2); font-size: 12px; }
.program-language { margin-left: 8px; color: var(--composer-accent); font: 10px var(--vp-font-family-mono); }
.copy-program { display: inline-flex; align-items: center; justify-content: center; gap: 7px; min-height: 32px; padding: 5px 10px; border: 1px solid var(--composer-border); border-radius: 5px; color: var(--vp-c-text-1); font-size: 11px; cursor: pointer; }
.copy-program:hover { border-color: var(--composer-accent); color: var(--composer-accent); }
.program-preview {
  --program-padding: 28px;
  --program-min-height: 360px;
  box-sizing: border-box;
  height: max(var(--program-min-height), calc(var(--program-lines) * 1.7em + var(--program-padding) * 2));
  margin: 0;
  padding: var(--program-padding) 26px;
  overflow-x: auto;
  overflow-y: hidden;
  background: transparent;
  font: 450 clamp(13px, 1.2vw, 16px)/1.7 var(--vp-font-family-mono);
  transition: height .32s cubic-bezier(.22, 1, .36, 1);
}
.program-preview code { display: block; width: max-content; min-width: 100%; margin: 0; padding: 0; border: 0; border-radius: 0; background: transparent; color: var(--vp-c-text-1); font: inherit; letter-spacing: -.025em; white-space: pre; }
.program-block { border-radius: 3px; }
.program-block.optional-block { color: var(--vp-c-text-1); }
.program-block.just-added { animation: added-code 1.8s ease-out; }
.program-preview :deep(.syntax-keyword) { color: var(--composer-accent); }
.program-preview :deep(.syntax-string) { color: color-mix(in srgb, var(--composer-accent) 55%, var(--vp-c-text-1)); }
.program-preview :deep(.syntax-number) { color: var(--vp-c-brand-1); }
.program-footer { display: flex; justify-content: space-between; gap: 16px; margin: 0 26px; padding: 16px 0; border-top: 1px solid var(--composer-border); color: var(--vp-c-text-2); font-size: 11px; }
.program-notes { padding: 0 26px 18px; }
.program-notes p { margin: 7px 0 0; color: var(--vp-c-text-2); font-size: 11px; line-height: 1.6; }
.program-notes a { color: var(--composer-accent); text-underline-offset: 3px; }
button:focus-visible, a:focus-visible, pre:focus-visible { outline: 2px solid var(--composer-accent); outline-offset: 4px; }
.sr-only { position: absolute; width: 1px; height: 1px; padding: 0; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
@keyframes added-code { 0%, 30% { background: color-mix(in srgb, var(--composer-accent) 22%, transparent); } 100% { background: transparent; } }
@media (max-width: 959px) {
  .server-composer { grid-template-columns: 1fr; gap: 28px; margin-top: 48px; padding-top: 32px; }
  .composer-intro h2 { font-size: 38px; }
  .composer-intro p { max-width: none; margin-bottom: 22px; }
  .feature-copy > span { max-width: none; }
  .feature-option { min-height: 76px; }
  .getting-started { margin-top: 12px; }
  .program-preview { --program-min-height: 0px; font-size: 15px; }
}
@media (max-width: 479px) {
  .composer-intro h2 { font-size: 36px; }
  .program-heading { gap: 8px; padding: 16px 16px 0; }
  .program-heading > span { font-size: 11px; }
  .program-language { display: none; }
  .program-preview { --program-padding: 24px; padding-right: 16px; padding-left: 16px; font-size: 12px; }
  .program-footer { margin: 0 16px; font-size: 10px; }
  .program-notes { padding-right: 16px; padding-left: 16px; }
}
@media (prefers-reduced-motion: reduce) {
  .feature-option, .feature-switch, .feature-switch > span, .program-preview { transition: none; }
  .program-block.just-added { animation: none; }
}
</style>
