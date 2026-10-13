<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

function getPointerApproach(x, y, width, height, logoSize) {
  // Measure from the resting logo's box, not from the moving camera or its center.
  const halfTarget = logoSize / 2 + 12;
  const dx = Math.max(0, Math.abs(x - width / 2) - halfTarget);
  const dy = Math.max(0, Math.abs(y - height / 2) - halfTarget);
  const distance = Math.hypot(dx, dy);
  const range = Math.min(150, width * 0.32);
  const proximity = Math.max(0, 1 - distance / Math.max(1, range));
  return proximity * proximity * (3 - 2 * proximity);
}

function getApproachState(value) {
  const progress = Math.max(0, Math.min(1, value));
  const zoom = 1 + 5 * progress ** 1.6;
  const reveal = Math.max(0, Math.min(1, (progress - 0.65) / 0.35));
  return {
    zoom,
    beadScale: 1 + 0.15 * progress,
    detailOpacity: reveal * reveal * (3 - 2 * reveal),
    // Compensate for zoom so the apparent speed decreases throughout approach.
    playbackRate: (1.45 - 1.33 * progress) / zoom,
  };
}

function getCameraState(value, width, logoSize) {
  const progress = Math.max(0, Math.min(1, value));
  const { zoom } = getApproachState(progress);
  // The SVG's left edge is at 46.89 / 700 of its width. Frame that edge
  // at 8% of the viewport: the close-up reveals the pipeline inside SimpleW.
  const logoEdge = width / 2 - logoSize * (0.5 - 46.89 / 700);
  const closeOffset = -width * 0.42 + (width / 2 - logoEdge) * 6;
  const offset = closeOffset * progress ** 1.6;
  const project = x => width / 2 + (x - width / 2) * zoom + offset;
  const worldAtClose = x => width / 2 + (x - width / 2 - closeOffset) / 6;
  const checkpoint = position => {
    const worldX = worldAtClose(width * position);
    return { worldX, x: project(worldX) };
  };
  return {
    offset,
    logoEdge: project(logoEdge),
    stages: [0.23, 0.44, 0.69].map(checkpoint),
    handler: checkpoint(0.93),
  };
}

const scene = ref(null)
const interaction = ref(null)
const trafficLayer = ref(null)
const intensity = ref(0)
const emit = defineEmits(['approach'])
watch(intensity, value => emit('approach', value), { immediate: true, flush: 'sync' })
const approach = computed(() => getApproachState(intensity.value))
const viewport = ref({ width: 0, height: 0, logoSize: 200 })
const camera = computed(() => getCameraState(intensity.value, viewport.value.width, viewport.value.logoSize))
const middleware = [
  { name: 'TLS', action: 'Encrypt', result: 'Encrypted', color: 'var(--tls-color)' },
  { name: 'Firewall', action: 'IP rules', result: 'Allowed', color: 'var(--firewall-color)' },
  { name: 'Authentication', action: 'Identity', result: 'Verified', color: 'var(--auth-color)' },
]
const stages = computed(() => middleware.map((stage, index) => ({ ...stage, ...camera.value.stages[index] })))
const idleRate = getApproachState(0).playbackRate
const reducedMotion = ref(false)
const connection = ref(null)
const trail = ref('')
const visibleConnections = ref([])
const impacts = ref([])
let particles = []
let animations = []
let frame = 0
let lastTime = 0
let targetIntensity = 0
let pointerIntensity = 0
let hoverHeld = false
let selected = null
let history = []
let bounds = { left: 0, top: 0, width: 0, height: 0 }
let logoSize = 200
let resizeObserver
let motionPreference
let playbackRate = idleRate

function collectAnimations() {
  animations = particles.flatMap(element => element.getAnimations())
  for (const animation of animations) animation.updatePlaybackRate(idleRate)
  playbackRate = idleRate
}

function clearConnection() {
  selected = null
  history = []
  connection.value = null
  trail.value = ''
}

function start() {
  if (!frame && !reducedMotion.value && !document.hidden) frame = requestAnimationFrame(tick)
}

function updateTarget() {
  targetIntensity = hoverHeld ? 1 : pointerIntensity
  if (targetIntensity > 0 || intensity.value > 0) start()
}

function movePointer(event) {
  if (event.pointerType === 'touch' || reducedMotion.value) return
  bounds = interaction.value.getBoundingClientRect()
  const pointer = { x: event.clientX - bounds.left, y: event.clientY - bounds.top }
  pointerIntensity = getPointerApproach(pointer.x, pointer.y, bounds.width, bounds.height, logoSize)
  if (pointerIntensity === 1) hoverHeld = true
  updateTarget()
}

function leavePointer() {
  pointerIntensity = 0
  hoverHeld = false
  updateTarget()
}

function restartConnection(event) {
  if (event.target === selected && /^pass-\d+(?:-|$)/.test(event.animationName)) clearConnection()
}

function followConnection() {
  const trafficBounds = trafficLayer.value.getBoundingClientRect()
  const zoom = trafficBounds.width / bounds.width
  const originX = trafficBounds.left - bounds.left
  const originY = trafficBounds.top - bounds.top
  const sceneStyle = getComputedStyle(scene.value)
  const beadScale = parseFloat(sceneStyle.getPropertyValue('--bead-scale'))
  const laneScale = parseFloat(sceneStyle.getPropertyValue('--lane-scale'))
  const cy = bounds.height / 2
  const samples = particles.map((element, index) => {
    const rect = element.getBoundingClientRect()
    return {
      element,
      id: index,
      x: rect.left + rect.width / 2 - bounds.left,
      y: rect.top + rect.height / 2 - bounds.top,
      worldX: (rect.left + rect.width / 2 - trafficBounds.left) / zoom,
      worldY: ((rect.top + rect.height / 2 - trafficBounds.top) / zoom - cy) / laneScale + cy,
      radius: rect.width / 2 * beadScale,
      opacity: Number(getComputedStyle(element).opacity),
    }
  })
  const visible = samples.filter(sample => sample.opacity > 0.15 && sample.x > -30 && sample.x < bounds.width + 30 && sample.y > 65 && sample.y < bounds.height - 55)
  visibleConnections.value = visible.map(sample => ({
    id: sample.id, x: sample.x, y: sample.y, radius: sample.radius + 2,
    encrypted: sample.worldX >= stages.value[0].worldX,
    authenticated: sample.worldX >= stages.value[2].worldX,
  }))
  impacts.value = [...stages.value, camera.value.handler].map(stage => {
    const closest = visible.reduce((best, sample) => !best || Math.abs(sample.x - stage.x) < Math.abs(best.x - stage.x) ? sample : best, null)
    return closest ? { y: closest.y, strength: Math.max(0, 1 - Math.abs(closest.x - stage.x) / (closest.radius + 14)) } : null
  })
  let current = samples.find(sample => sample.element === selected)
  if (current && (current.x < 0 || current.x > bounds.width || current.y < 65 || current.y > bounds.height - 55 || current.opacity < 0.08)) {
    clearConnection()
    current = null
  }
  if (!current) {
    // Follow the request across the logo boundary and through the internal pipeline.
    const candidates = visible.filter(sample => sample.x >= 0 && sample.x < bounds.width)
    candidates.sort((a, b) => a.x - b.x)
    current = candidates[0]
    if (!current) return
    selected = current.element
  }

  // Keep the trail in scene coordinates so zoom never bends or detaches it.
  let last = history.at(-1)
  if (last && current.worldX < last.x - 10) {
    history = []
    last = null
  }
  if (!last || Math.hypot(current.worldX - last.x, current.worldY - last.y) * zoom > 0.5) history.push({ x: current.worldX, y: current.worldY })
  while (history.length > 360 || (history.length > 1 && (current.worldX - history[0].x) * zoom > 230)) history.shift()
  trail.value = [...history, { x: current.worldX, y: current.worldY }].map(point => `${(originX + point.x * zoom).toFixed(1)},${(originY + (cy + (point.y - cy) * laneScale) * zoom).toFixed(1)}`).join(' ')
  const passed = stages.value.filter(stage => current.worldX >= stage.worldX).length
  const handled = current.worldX >= camera.value.handler.worldX
  connection.value = {
    x: current.x,
    y: current.y,
    radius: current.radius + 3,
    passed,
    handled,
    phase: handled ? 'Response' : 'Request',
    detail: handled ? '200 OK' : ['GET /hello', 'Encrypted', 'IP allowed', 'Token verified'][passed],
    labelX: Math.max(55, Math.min(bounds.width - 65, current.x)),
    labelY: Math.max(93, Math.min(bounds.height - 84, current.y - current.radius - 28)),
  }
}

function tick(time) {
  frame = 0
  if (document.hidden || reducedMotion.value) return
  const delta = Math.min(lastTime ? time - lastTime : 16, 64)
  lastTime = time
  const easing = 1 - Math.exp(-delta / 230)
  intensity.value += (targetIntensity - intensity.value) * easing
  if (Math.abs(targetIntensity - intensity.value) < 0.001) intensity.value = targetIntensity
  // Change playback rate, keeping every animation's current time and color in sync.
  if (playbackRate !== approach.value.playbackRate) {
    playbackRate = approach.value.playbackRate
    for (const animation of animations) animation.updatePlaybackRate(playbackRate)
  }
  // Start following a little before the close-up reveals the annotations.
  if (intensity.value > 0.6) followConnection()
  else {
    clearConnection()
    visibleConnections.value = []
    impacts.value = []
  }
  if (intensity.value > 0 || targetIntensity > 0) start()
  else lastTime = 0
}

function resetInspection() {
  cancelAnimationFrame(frame)
  frame = 0
  lastTime = 0
  targetIntensity = 0
  pointerIntensity = 0
  hoverHeld = false
  intensity.value = 0
  for (const animation of animations) animation.updatePlaybackRate(idleRate)
  playbackRate = idleRate
  clearConnection()
  visibleConnections.value = []
  impacts.value = []
}

function measure() {
  resetInspection()
  bounds = interaction.value.getBoundingClientRect()
  logoSize = parseFloat(getComputedStyle(scene.value).getPropertyValue('--logo-size'))
  viewport.value = { width: bounds.width, height: bounds.height, logoSize }
  collectAnimations()
}

function changeMotionPreference() {
  resetInspection()
  reducedMotion.value = motionPreference.matches
  collectAnimations()
}

function changeVisibility() {
  if (document.hidden) resetInspection()
}

onMounted(() => {
  particles = [...scene.value.querySelectorAll('.req')]
  motionPreference = window.matchMedia('(prefers-reduced-motion: reduce)')
  reducedMotion.value = motionPreference.matches
  motionPreference.addEventListener('change', changeMotionPreference)
  resizeObserver = new ResizeObserver(measure)
  resizeObserver.observe(interaction.value)
  window.addEventListener('resize', measure)
  window.addEventListener('blur', resetInspection)
  window.addEventListener('scroll', resetInspection, { passive: true })
  document.addEventListener('visibilitychange', changeVisibility)
  measure()
})

onBeforeUnmount(() => {
  resetInspection()
  resizeObserver?.disconnect()
  motionPreference?.removeEventListener('change', changeMotionPreference)
  window.removeEventListener('resize', measure)
  window.removeEventListener('blur', resetInspection)
  window.removeEventListener('scroll', resetInspection)
  document.removeEventListener('visibilitychange', changeVisibility)
})
</script>

<template>
  <div
    ref="scene"
    class="server-core"
    :style="{
      '--flux-scale': approach.zoom,
      '--bead-scale': approach.beadScale,
      '--camera-x': `${camera.offset}px`,
      '--detail-opacity': approach.detailOpacity,
      '--lane-scale': 1 - 0.72 * intensity,
      '--bead-stretch': 1 / (1 - 0.72 * intensity),
      '--approach': intensity,
    }"
  >
    <div class="traffic-window">
      <div ref="trafficLayer" class="traffic-layer">
        <div class="logo-zone">
          <img src="/logo.svg" alt="SimpleW" class="logo" />
          <svg class="logo-shell" viewBox="0 0 700 700" aria-hidden="true">
            <path d="M325 14.4338C340.47 5.50212 359.53 5.50212 375 14.4338L628.109 160.566C643.579 169.498 653.109 186.004 653.109 203.868V496.132C653.109 513.996 643.579 530.502 628.109 539.434L375 685.566C359.53 694.498 340.47 694.498 325 685.566L71.8911 539.434C56.4211 530.502 46.8911 513.996 46.8911 496.132V203.868C46.8911 186.004 56.4211 169.498 71.8911 160.566L325 14.4338Z" vector-effect="non-scaling-stroke" />
          </svg>
        </div>
        <div class="traffic-group" aria-hidden="true" @animationiteration="restartConnection">
          <span v-for="index in 12" :key="index" class="req pass" :class="`p${index}`"></span>
          <div class="detail-traffic">
            <span
              v-for="index in 24" :key="index" class="req pass"
              :class="`p${(index - 1) % 12 + 1}`"
              :style="{ animationDelay: `calc(var(--traffic-start) - var(--duration) * ${(Math.floor((index - 1) / 12) + 1) / 3})` }"
            ></span>
          </div>
        </div>
      </div>
    </div>

    <svg class="connection-details" :style="{ opacity: approach.detailOpacity }" aria-hidden="true">
      <text :transform="`translate(${camera.logoEdge - 10}, ${viewport.height / 2}) rotate(-90)`" class="logo-boundary-label">SimpleW</text>
      <g v-for="(stage, index) in stages" :key="stage.name" class="middleware" :style="{ color: stage.color }" :transform="`translate(${stage.x}, 0)`">
        <rect x="-10" y="64" width="20" :height="Math.max(0, viewport.height - 124)" class="middleware-volume" />
        <line x1="0" x2="0" y1="64" :y2="viewport.height - 60" class="middleware-line" />
        <path d="M -5 -5 V 0 H 5 V -5" :transform="`translate(0, ${viewport.height - 60})`" class="middleware-foot" />
        <text y="28" class="middleware-number">0{{ index + 1 }}</text>
        <text y="47" class="middleware-label">{{ stage.name }}</text>
        <text :y="viewport.height - 34" class="middleware-caption">{{ stage.action }}</text>
        <text v-if="connection && connection.passed > index" :y="viewport.height - 18" class="middleware-result">{{ stage.result }} ✓</text>
        <g v-if="impacts[index]" :opacity="impacts[index].strength">
          <line x1="0" x2="0" :y1="impacts[index].y - 15" :y2="impacts[index].y + 15" class="middleware-impact" />
          <circle cx="0" :cy="impacts[index].y" r="5" class="middleware-contact" />
        </g>
      </g>
      <g :transform="`translate(${camera.handler.x}, 0)`" class="core-annotation">
        <text y="28" class="middleware-number">04</text>
        <text :x="viewport.width - 8 - camera.handler.x" y="47" class="middleware-label handler-label">Handler</text>
        <line x1="0" x2="0" :y1="viewport.height / 2 - 26" :y2="viewport.height / 2 + 26" class="middleware-line" />
        <circle cx="0" :cy="viewport.height / 2" r="7" class="handler-node" />
        <circle v-if="impacts[3]" cx="0" :cy="impacts[3].y" r="10" class="handler-impact" :opacity="impacts[3].strength" />
        <text :y="viewport.height - 34" class="middleware-caption">Route</text>
        <text v-if="connection?.handled" :y="viewport.height - 18" class="middleware-result">200 OK</text>
      </g>
      <polyline :points="trail" class="connection-trail" />
      <g v-for="bead in visibleConnections" :key="bead.id">
        <circle v-if="bead.encrypted" :cx="bead.x" :cy="bead.y" :r="bead.radius" class="encrypted-marker" />
        <circle v-if="bead.authenticated" :cx="bead.x" :cy="bead.y" r="2.5" class="token-marker" />
      </g>
      <g v-if="connection">
        <circle :cx="connection.x" :cy="connection.y" :r="connection.radius" class="connection-marker" />
        <line :x1="connection.x" :y1="connection.y - connection.radius - 3" :x2="connection.labelX" :y2="connection.labelY + 20" class="connection-leader" />
        <g :transform="`translate(${connection.labelX}, ${connection.labelY})`">
          <text class="connection-label">{{ connection.phase }}</text>
          <text y="15" class="connection-caption">{{ connection.detail }}</text>
        </g>
      </g>
    </svg>

    <div
      ref="interaction"
      class="inspection-surface"
      aria-hidden="true"
      @pointerenter="movePointer"
      @pointermove="movePointer"
      @pointerleave="leavePointer"
      @pointercancel="leavePointer"
    ></div>
  </div>
</template>

<style scoped>
.server-core {
  user-select: none;
  --scene-w: 780px;
  --scene-h: 380px;
  --logo-size: 200px;
  --incoming-x: 122px;
  --dot: 7px;
  --req-idle: #94a3b8;
  --tls-color: #047857;
  --firewall-color: #dc2626;
  --auth-color: #a16207;
  --traffic-start: 1s;
  --violet: #8b5cf6;
  --violet-2: #a855f7;
  --cyan: #22d3ee;
  --cyan-2: #06b6d4;

  --out-y-1: 42%;
  --out-y-2: 45%;
  --out-y-3: 47%;
  --out-y-4: 49%;
  --out-y-5: 51%;
  --out-y-6: 53%;
  --out-y-7: 55%;
  --out-y-8: 58%;

  width: min(100%, var(--scene-w));
  height: var(--scene-h);
  margin: 0 auto;
  margin-bottom: -65px;
  overflow: visible;
  isolation: isolate;
}

:global(.dark .server-core) {
  --tls-color: #6ee7b7;
  --firewall-color: #fca5a5;
  --auth-color: #fcd34d;
}

.logo-zone {
  position: absolute;
  top: 50%;
  left: 50%;
  width: var(--logo-size);
  height: var(--logo-size);
  transform: translate(-50%, -50%);
  z-index: 20;
  display: grid;
  place-items: center;
}

.logo {
  width: 100%;
  height: 100%;
  display: block;
  object-fit: contain;
  transform: translateZ(0);
  opacity: calc(1 - var(--detail-opacity));
  backface-visibility: hidden;
  -webkit-backface-visibility: hidden;
  will-change: auto;
}

.logo-shell {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  opacity: var(--detail-opacity);
  fill: none;
  stroke: var(--violet);
  stroke-width: 1.5;
  pointer-events: none;
}

.logo-boundary-label {
  text-anchor: middle;
  fill: var(--vp-c-brand-1);
  font: 500 11px ui-monospace, monospace;
  letter-spacing: 0.08em;
  stroke: var(--vp-c-bg);
  stroke-width: 2px;
  paint-order: stroke fill;
}

.inspection-surface,
.connection-details {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
}

.inspection-surface {
  z-index: 30;
}

.connection-details {
  z-index: 6;
  overflow: hidden;
  pointer-events: none;
  color: var(--vp-c-brand-1);
}

.connection-trail {
  fill: none;
  stroke: currentColor;
  stroke-width: 1;
  stroke-linecap: round;
  stroke-linejoin: round;
  opacity: 0.7;
}

.middleware-volume { fill: currentColor; opacity: 0.035; }
.middleware-line, .middleware-foot { fill: none; stroke: currentColor; stroke-width: 1; opacity: 0.5; }
.middleware-impact { stroke: currentColor; stroke-width: 3; stroke-linecap: round; }
.middleware-contact { fill: var(--vp-c-bg); stroke: currentColor; stroke-width: 1.5; }
.middleware-number, .middleware-label, .middleware-caption, .middleware-result {
  text-anchor: middle;
  fill: currentColor;
  stroke: var(--vp-c-bg);
  stroke-width: 2px;
  paint-order: stroke fill;
  stroke-linejoin: round;
}
.middleware-number { font: 10px ui-monospace, monospace; opacity: 0.65; }
.middleware-label { font: 600 13px ui-monospace, monospace; }
.middleware-caption { font: 11px ui-monospace, monospace; fill: var(--vp-c-text-2); }
.middleware-result { font: 10px ui-monospace, monospace; }
.core-annotation { color: var(--vp-c-brand-1); }
.core-annotation .handler-label { text-anchor: end; }
.handler-node { fill: var(--vp-c-bg); stroke: currentColor; stroke-width: 1.25; }
.handler-impact { fill: none; stroke: currentColor; stroke-width: 2; }
.encrypted-marker { fill: none; stroke: var(--tls-color); stroke-width: 1.25; }
.token-marker { fill: white; }
.connection-leader { stroke: var(--vp-c-text-2); stroke-width: 0.65; opacity: 0.5; }

.connection-marker {
  fill: none;
  stroke: currentColor;
  stroke-width: 1.25;
}

.connection-label,
.connection-caption {
  text-anchor: middle;
  fill: var(--vp-c-text-1);
  stroke: var(--vp-c-bg);
  stroke-width: 3px;
  stroke-linejoin: round;
  paint-order: stroke fill;
}

.connection-label {
  font: 600 11px ui-monospace, monospace;
}

.connection-caption {
  font: 10px ui-monospace, monospace;
  fill: var(--vp-c-text-2);
}
.traffic-window {
  --edge-fade: calc(var(--detail-opacity) * clamp(24px, 4vw, 44px));
  position: absolute;
  inset: 0;
  z-index: 5;
  overflow: hidden;
  pointer-events: none;
  /* Soften the cropped logo and traffic at the edges; annotations stay crisp. */
  -webkit-mask-image:
    linear-gradient(to right, transparent, #000 var(--edge-fade), #000 calc(100% - var(--edge-fade)), transparent),
    linear-gradient(to bottom, transparent, #000 var(--edge-fade), #000 calc(100% - var(--edge-fade)), transparent);
  mask-image:
    linear-gradient(to right, transparent, #000 var(--edge-fade), #000 calc(100% - var(--edge-fade)), transparent),
    linear-gradient(to bottom, transparent, #000 var(--edge-fade), #000 calc(100% - var(--edge-fade)), transparent);
  -webkit-mask-composite: source-in;
  mask-composite: intersect;
}

.traffic-layer {
  position: absolute;
  inset: 0;
  pointer-events: none;
  transform: translateX(var(--camera-x)) scale(var(--flux-scale));
  transform-origin: center;
}

.traffic-group, .detail-traffic {
  position: absolute;
  inset: 0;
  pointer-events: none;
}

.detail-traffic { opacity: var(--detail-opacity); }
.traffic-group { transform: scaleY(var(--lane-scale)); }

.req {
  position: absolute;
  left: -40px;
  top: 50%;
  width: var(--dot);
  height: var(--dot);
  margin-top: calc(var(--dot) * -0.5);
  border-radius: 50%;
  opacity: 0;
  z-index: 5;
}

.req::after {
  content: "";
  position: absolute;
  inset: 0;
  border-radius: inherit;
  background: currentColor;
  transform: scale(var(--bead-scale)) scaleY(var(--bead-stretch));
  box-shadow: 0 0 calc(10px - var(--approach) * 9.7px) currentColor;
}

.pass { --req-active: var(--cyan); }

/* always-on connection bindings */
.p1  { --duration: 1.9s; --req-active: var(--violet);   animation: pass-1 var(--duration) linear var(--traffic-start) infinite, color-at-42 var(--duration) linear var(--traffic-start) infinite; }
.p2  { --duration: 1.4s; --req-active: var(--cyan);     animation: pass-2 var(--duration) linear var(--traffic-start) infinite, color-at-40 var(--duration) linear var(--traffic-start) infinite; }
.p3  { --duration: 2.1s; --req-active: var(--violet-2); animation: pass-3 var(--duration) linear var(--traffic-start) infinite, color-at-45 var(--duration) linear var(--traffic-start) infinite; }
.p4  { --duration: 1.6s; --req-active: var(--cyan-2);   animation: pass-4 var(--duration) linear var(--traffic-start) infinite, color-at-44 var(--duration) linear var(--traffic-start) infinite; }
.p5  { --duration: 2.4s; --req-active: var(--violet);   animation: pass-5 var(--duration) linear var(--traffic-start) infinite, color-at-49 var(--duration) linear var(--traffic-start) infinite; }
.p6  { --duration: 1.3s; --req-active: var(--cyan);     animation: pass-6 var(--duration) linear var(--traffic-start) infinite, color-at-39 var(--duration) linear var(--traffic-start) infinite; }
.p7  { --duration: 1.8s; --req-active: var(--violet-2); animation: pass-7 var(--duration) linear var(--traffic-start) infinite, color-at-52 var(--duration) linear var(--traffic-start) infinite; }
.p8  { --duration: 2.2s; --req-active: var(--cyan-2);   animation: pass-8 var(--duration) linear var(--traffic-start) infinite, color-at-54 var(--duration) linear var(--traffic-start) infinite; }
.p9  { --duration: 1.5s; --req-active: var(--violet);   animation: pass-9 var(--duration) linear var(--traffic-start) infinite, color-at-46 var(--duration) linear var(--traffic-start) infinite; }
.p10 { --duration: 2s; --req-active: var(--cyan);     animation: pass-10 var(--duration) linear var(--traffic-start) infinite, color-at-48 var(--duration) linear var(--traffic-start) infinite; }
.p11 { --duration: 1.7s; --req-active: var(--cyan-2);   animation: pass-11 var(--duration) linear var(--traffic-start) infinite, color-at-42 var(--duration) linear var(--traffic-start) infinite; }
.p12 { --duration: 2.5s; --req-active: var(--violet-2); animation: pass-12 var(--duration) linear var(--traffic-start) infinite, color-at-50 var(--duration) linear var(--traffic-start) infinite; }

/* Connections start neutral, then change color as they approach the logo. */
@keyframes color-at-39 {
  0%, 38.9% { color: var(--req-idle); }
  39%, 100% { color: var(--req-active); }
}

@keyframes color-at-40 {
  0%, 39.9% { color: var(--req-idle); }
  40%, 100% { color: var(--req-active); }
}

@keyframes color-at-42 {
  0%, 41.9% { color: var(--req-idle); }
  42%, 100% { color: var(--req-active); }
}

@keyframes color-at-44 {
  0%, 43.9% { color: var(--req-idle); }
  44%, 100% { color: var(--req-active); }
}

@keyframes color-at-45 {
  0%, 44.9% { color: var(--req-idle); }
  45%, 100% { color: var(--req-active); }
}

@keyframes color-at-46 {
  0%, 45.9% { color: var(--req-idle); }
  46%, 100% { color: var(--req-active); }
}

@keyframes color-at-48 {
  0%, 47.9% { color: var(--req-idle); }
  48%, 100% { color: var(--req-active); }
}

@keyframes color-at-49 {
  0%, 48.9% { color: var(--req-idle); }
  49%, 100% { color: var(--req-active); }
}

@keyframes color-at-50 {
  0%, 49.9% { color: var(--req-idle); }
  50%, 100% { color: var(--req-active); }
}

@keyframes color-at-52 {
  0%, 51.9% { color: var(--req-idle); }
  52%, 100% { color: var(--req-active); }
}

@keyframes color-at-54 {
  0%, 53.9% { color: var(--req-idle); }
  54%, 100% { color: var(--req-active); }
}

/* =========================================================
   Traffic trajectories - pass through
   ========================================================= */
@keyframes pass-1 {
  0%, 8%   { left: -40px; top: 14%; opacity: 0; transform: scale(0.85); }
  12%, 32% { opacity: 1; }
  42%      { left: calc(50% - 124px); top: 38%; opacity: 1; }
  50%      { left: 50%; top: 50%; opacity: 1; transform: scale(1.18); }
  62%      { left: calc(50% + 124px); top: var(--out-y-1); opacity: 1; }
  100%     { left: calc(100% + 14px); top: var(--out-y-1); opacity: 0; transform: scale(0.85); }
}

@keyframes pass-2 {
  0%, 4%   { left: -40px; top: 24%; opacity: 0; }
  8%, 28%  { opacity: 1; }
  40%      { left: calc(50% - var(--incoming-x)); top: 44%; opacity: 1; }
  49%      { left: 50%; top: 50%; opacity: 1; transform: scale(1.15); }
  60%      { left: calc(50% + 118px); top: var(--out-y-2); opacity: 1; }
  100%     { left: calc(100% + 14px); top: var(--out-y-2); opacity: 0; }
}

@keyframes pass-3 {
  0%, 14%  { left: -40px; top: 34%; opacity: 0; }
  18%, 38% { opacity: 1; }
  45%      { left: calc(50% - 112px); top: 48%; opacity: 1; }
  53%      { left: 50%; top: 50%; opacity: 1; transform: scale(1.2); }
  64%      { left: calc(50% + 120px); top: var(--out-y-3); opacity: 1; }
  100%     { left: calc(100% + 14px); top: var(--out-y-3); opacity: 0; }
}

@keyframes pass-4 {
  0%, 10%  { left: -40px; top: 48%; opacity: 0; }
  14%, 34% { opacity: 1; }
  44%      { left: calc(50% - var(--incoming-x)); top: 50%; opacity: 1; }
  52%      { left: 50%; top: 50%; opacity: 1; transform: scale(1.16); }
  63%      { left: calc(50% + 122px); top: var(--out-y-4); opacity: 1; }
  100%     { left: calc(100% + 14px); top: var(--out-y-4); opacity: 0; }
}

@keyframes pass-5 {
  0%, 18%  { left: -40px; top: 62%; opacity: 0; }
  22%, 42% { opacity: 1; }
  49%      { left: calc(50% - 120px); top: 54%; opacity: 1; }
  57%      { left: 50%; top: 50%; opacity: 1; transform: scale(1.18); }
  68%      { left: calc(50% + 122px); top: var(--out-y-5); opacity: 1; }
  100%     { left: calc(100% + 14px); top: var(--out-y-5); opacity: 0; }
}

@keyframes pass-6 {
  0%, 6%   { left: -40px; top: 76%; opacity: 0; transform: scale(0.8); }
  10%, 26% { opacity: 1; }
  39%      { left: calc(50% - 122px); top: 60%; opacity: 1; }
  48%      { left: 50%; top: 50%; opacity: 1; transform: scale(1.14); }
  59%      { left: calc(50% + 118px); top: var(--out-y-6); opacity: 1; }
  100%     { left: calc(100% + 14px); top: var(--out-y-6); opacity: 0; }
}

@keyframes pass-7 {
  0%, 22%  { left: -40px; top: 18%; opacity: 0; }
  26%, 46% { opacity: 1; }
  52%      { left: calc(50% - 120px); top: 42%; opacity: 1; }
  60%      { left: 50%; top: 50%; opacity: 1; transform: scale(1.15); }
  71%      { left: calc(50% + 118px); top: var(--out-y-7); opacity: 1; }
  100%     { left: calc(100% + 14px); top: var(--out-y-7); opacity: 0; }
}

@keyframes pass-8 {
  0%, 26%  { left: -40px; top: 54%; opacity: 0; }
  30%, 48% { opacity: 1; }
  54%      { left: calc(50% - 116px); top: 52%; opacity: 1; }
  62%      { left: 50%; top: 50%; opacity: 1; transform: scale(1.13); }
  73%      { left: calc(50% + 116px); top: var(--out-y-8); opacity: 1; }
  100%     { left: calc(100% + 14px); top: var(--out-y-8); opacity: 0; }
}

@keyframes pass-9 {
  0%, 12%  { left: -40px; top: 70%; opacity: 0; }
  16%, 36% { opacity: 1; }
  46%      { left: calc(50% - var(--incoming-x)); top: 58%; opacity: 1; }
  55%      { left: 50%; top: 50%; opacity: 1; transform: scale(1.16); }
  66%      { left: calc(50% + 118px); top: var(--out-y-3); opacity: 1; }
  100%     { left: calc(100% + 14px); top: var(--out-y-3); opacity: 0; }
}

@keyframes pass-10 {
  0%, 16%  { left: -40px; top: 40%; opacity: 0; }
  20%, 40% { opacity: 1; }
  48%      { left: calc(50% - 116px); top: 50%; opacity: 1; }
  56%      { left: 50%; top: 50%; opacity: 1; transform: scale(1.18); }
  67%      { left: calc(50% + 120px); top: var(--out-y-4); opacity: 1; }
  100%     { left: calc(100% + 14px); top: var(--out-y-4); opacity: 0; }
}

@keyframes pass-11 {
  0%, 10%  { left: -40px; top: 28%; opacity: 0; }
  14%, 30% { opacity: 1; }
  42%      { left: calc(50% - var(--incoming-x)); top: 46%; opacity: 1; }
  51%      { left: 50%; top: 50%; opacity: 1; transform: scale(1.14); }
  63%      { left: calc(50% + 122px); top: var(--out-y-5); opacity: 1; }
  100%     { left: calc(100% + 14px); top: var(--out-y-5); opacity: 0; }
}

@keyframes pass-12 {
  0%, 20%  { left: -40px; top: 58%; opacity: 0; }
  24%, 42% { opacity: 1; }
  50%      { left: calc(50% - 120px); top: 54%; opacity: 1; }
  59%      { left: 50%; top: 50%; opacity: 1; transform: scale(1.17); }
  71%      { left: calc(50% + 124px); top: var(--out-y-6); opacity: 1; }
  100%     { left: calc(100% + 14px); top: var(--out-y-6); opacity: 0; }
}

@media (max-width: 900px) {
  .server-core {
    --scene-h: 320px;
    --logo-size: 150px;
    --incoming-x: 101px;
    --dot: 6px;
  }
}

@media (max-width: 640px) {
  .middleware-label { font-size: 11px; }

  .server-core {
    --scene-h: 236px;
    --logo-size: 110px;
    --incoming-x: 71px;
    --dot: 5px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .req { animation: none !important; }
  .traffic-group,
  .connection-details { display: none; }
}
</style>
