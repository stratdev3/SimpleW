<template>
  <div class="server-core" aria-label="SimpleW protected traffic animation">
    <div class="core-zone">
      <!-- rings / belts -->
      <div class="belt belt-firewall">
        <div class="bump bump-fw-1"></div>
        <div class="bump bump-fw-2"></div>
        <div class="bump bump-fw-3"></div>
      </div>
      <div class="belt belt-auth">
        <div class="bump bump-auth-1"></div>
        <div class="bump bump-auth-2"></div>
        <div class="bump bump-auth-3"></div>
      </div>
      <div class="belt belt-routing">
        <div class="bump bump-r-1"></div>
        <div class="bump bump-r-2"></div>
        <div class="bump bump-r-3"></div>
        <div class="bump bump-p-1"></div>
        <div class="bump bump-p-2"></div>
        <div class="bump bump-p-3"></div>
        <div class="bump bump-p-4"></div>
        <div class="bump bump-p-5"></div>
        <div class="bump bump-p-6"></div>
        <div class="bump bump-p-7"></div>
        <div class="bump bump-p-8"></div>
        <div class="bump bump-p-9"></div>
        <div class="bump bump-p-10"></div>
        <div class="bump bump-p-11"></div>
        <div class="bump bump-p-12"></div>
      </div>

      <!-- orbiting icons -->
      <div class="orbit orbit-firewall">
        <div class="orbit-icon belt-icon-firewall" aria-hidden="true">
          <svg viewBox="0 0 24 24" class="icon icon-wall">
            <!-- crenellated wall (battlement) — 3 merlons, 2 gaps -->
            <path d="M2 20V6H7V12H10V6H14V12H17V6H22V20Z" />
            <!-- mortar line -->
            <path d="M2 16H22" />
          </svg>
        </div>
      </div>

      <div class="orbit orbit-auth">
        <div class="orbit-icon belt-icon-auth" aria-hidden="true">
          <svg viewBox="0 0 24 24" class="icon icon-lock">
            <rect x="6" y="11" width="12" height="10" rx="2.5" />
            <path d="M9 11V8.5a3 3 0 0 1 6 0V11" />
            <circle cx="12" cy="16.5" r="1.5" style="fill:rgba(0,0,0,0.35);stroke:none;" />
          </svg>
        </div>
      </div>

      <div class="orbit orbit-routing">
        <div class="orbit-icon belt-icon-routing" aria-hidden="true">
          <svg viewBox="0 0 24 24" class="icon icon-routing">
            <path d="M3 12h7" />
            <path d="M10 12L19 7" />
            <path d="M10 12L19 17" />
            <path d="M16.5 5.5L19 7L16.5 8.5" />
            <path d="M16.5 15.5L19 17L16.5 18.5" />
          </svg>
        </div>
      </div>

      <!-- persistent orbit labels -->
      <div class="belt-label label-firewall">Firewall</div>
      <div class="belt-label label-auth">Authentication</div>
      <div class="belt-label label-routing">Routing</div>

      <!-- center logo -->
      <div class="logo-zone">
        <div class="core-ring"></div>
        <img src="/logo.svg" alt="SimpleW" class="logo" />
      </div>
    </div>

    <div class="traffic-layer">
      <!-- valid traffic -->
      <span class="req pass p1"></span>
      <span class="req pass p2"></span>
      <span class="req pass p3"></span>
      <span class="req pass p4"></span>
      <span class="req pass p5"></span>
      <span class="req pass p6"></span>
      <span class="req pass p7"></span>
      <span class="req pass p8"></span>
      <span class="req pass p9"></span>
      <span class="req pass p10"></span>
      <span class="req pass p11"></span>
      <span class="req pass p12"></span>

      <!-- routing / redirect traffic -->
      <span class="req route r1"></span>
      <span class="req route r2"></span>
      <span class="req route r3"></span>

      <!-- blocked by firewall -->
      <span class="req block-fw fw1"></span>
      <span class="req block-fw fw2"></span>
      <span class="req block-fw fw3"></span>

      <!-- blocked by auth -->
      <span class="req block-auth a1"></span>
      <span class="req block-auth a2"></span>
      <span class="req block-auth a3"></span>
    </div>

  </div>
</template>

<style scoped>
/* =========================================================
   Scene variables
   ========================================================= */
.server-core {
  --scene-w: 780px;
  --scene-h: 380px;
  --logo-size: 200px;

  --belt-fw: 340px;
  --belt-auth: 280px;
  --belt-routing: 220px;

  --fw-hit-x: calc(var(--belt-fw) / 2);
  --auth-hit-x: calc(var(--belt-auth) / 2);
  --routing-hit-x: calc(var(--belt-routing) / 2);

  --dot: 7px;

  --violet: #8b5cf6;
  --violet-2: #a855f7;
  --cyan: #22d3ee;
  --cyan-2: #06b6d4;

  --fw-main: #dc2626;
  --fw-soft: rgba(220, 38, 38, 0.2);
  --fw-glow: rgba(220, 38, 38, 0.55);

  --auth-main: #eab308;
  --auth-soft: rgba(234, 179, 8, 0.2);
  --auth-glow: rgba(234, 179, 8, 0.55);

  --routing-main: #10b981;
  --routing-soft: rgba(16, 185, 129, 0.2);
  --routing-glow: rgba(16, 185, 129, 0.55);

  /* right-side normalized output band */
  --out-y-1: 42%;
  --out-y-2: 45%;
  --out-y-3: 47%;
  --out-y-4: 49%;
  --out-y-5: 51%;
  --out-y-6: 53%;
  --out-y-7: 55%;
  --out-y-8: 58%;

  --label-x-factor: 0.35;
  --label-y-factor: 0.39;

  width: min(100%, var(--scene-w));
  height: var(--scene-h);
  margin: 0 auto;
  margin-bottom: -65px;
  overflow: hidden;
  isolation: isolate;
}

/* =========================================================
   Core / center logo
   ========================================================= */
.core-zone {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  pointer-events: none;
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
  backface-visibility: hidden;
  -webkit-backface-visibility: hidden;
  will-change: auto;
}

.core-ring {
  position: absolute;
  inset: 10px;
  border-radius: 50%;
  border: 1px solid rgba(255, 255, 255, 0.08);
  animation: core-pulse 3.2s ease-in-out infinite;
}

/* =========================================================
   Belts / rings
   ========================================================= */
.belt {
  position: absolute;
  top: 50%;
  left: 50%;
  border-radius: 50%;
  transform: translate(-50%, -50%);
  border-style: solid;
  pointer-events: none;
}

.belt-firewall {
  width: var(--belt-fw);
  height: var(--belt-fw);
  border-width: 1px;
  border-color: rgba(220, 38, 38, 0.18);
  box-shadow: 0 0 0 0 rgba(220, 38, 38, 0);
  animation: fw-belt-idle 5.4s linear infinite;
}

.belt-auth {
  width: var(--belt-auth);
  height: var(--belt-auth);
  border-width: 1px;
  border-color: rgba(234, 179, 8, 0.18);
  box-shadow: 0 0 0 0 rgba(234, 179, 8, 0);
  animation: auth-belt-idle 5.8s linear infinite;
}

.belt-routing {
  width: var(--belt-routing);
  height: var(--belt-routing);
  border-width: 1px;
  border-color: var(--routing-soft);
  box-shadow: 0 0 0 0 transparent;
  animation: routing-idle 5.6s linear infinite;
}

/* =========================================================
   Belt bump overlays — one per particle, same duration/% as particle hit
   ========================================================= */
.bump {
  position: absolute;
  inset: -1px;
  border-radius: 50%;
  border: 1px solid transparent;
  box-shadow: none;
  pointer-events: none;
}

/* firewall bumps — synced with fw1 (4.8s@50%), fw2 (6.3s@56%), fw3 (7.4s@62%) */
.bump-fw-1   { animation: bump-fw-1   4.8s linear infinite; }
.bump-fw-2   { animation: bump-fw-2   6.3s linear infinite; }
.bump-fw-3   { animation: bump-fw-3   7.4s linear infinite; }

/* auth bumps — synced with a1 (5.2s@52%), a2 (6.8s@58%), a3 (8.1s@64%) */
.bump-auth-1 { animation: bump-auth-1 5.2s linear infinite; }
.bump-auth-2 { animation: bump-auth-2 6.8s linear infinite; }
.bump-auth-3 { animation: bump-auth-3 8.1s linear infinite; }

/* routing bumps — synced with r1 (4.2s@52%), r2 (5.4s@58%), r3 (6.1s@68%) */
.bump-r-1    { animation: bump-r-1    4.2s linear infinite; }
.bump-r-2    { animation: bump-r-2    5.4s linear infinite; }
.bump-r-3    { animation: bump-r-3    6.1s linear infinite; }

/* pass-through routing bumps — synced with p1-p12 crossing the routing ring */
.bump-p-1, .bump-p-2, .bump-p-3, .bump-p-4, .bump-p-5, .bump-p-6,
.bump-p-7, .bump-p-8, .bump-p-9, .bump-p-10, .bump-p-11, .bump-p-12 { opacity: 0.3; }

.bump-p-1    { animation: bump-p-1    1.9s linear infinite; }
.bump-p-2    { animation: bump-p-2    1.4s linear infinite; }
.bump-p-3    { animation: bump-p-3    2.1s linear infinite; }
.bump-p-4    { animation: bump-p-4    1.6s linear infinite; }
.bump-p-5    { animation: bump-p-5    2.4s linear infinite; }
.bump-p-6    { animation: bump-p-6    1.3s linear infinite; }
.bump-p-7    { animation: bump-p-7    1.8s linear infinite; }
.bump-p-8    { animation: bump-p-8    2.2s linear infinite; }
.bump-p-9    { animation: bump-p-9    1.5s linear infinite; }
.bump-p-10   { animation: bump-p-10   2.0s linear infinite; }
.bump-p-11   { animation: bump-p-11   1.7s linear infinite; }
.bump-p-12   { animation: bump-p-12   2.5s linear infinite; }

.belt-firewall::before,
.belt-auth::before {
  content: "";
  position: absolute;
  inset: 0;
  border-radius: inherit;
  border: 1px dashed rgba(255, 255, 255, 0.04);
  transform: scale(1.04);
}

/* =========================================================
   Orbit containers
   ========================================================= */
.orbit {
  position: absolute;
  top: 50%;
  left: 50%;
  border-radius: 50%;
  transform: translate(-50%, -50%);
  pointer-events: none;
}

.orbit-firewall {
  width: var(--belt-fw);
  height: var(--belt-fw);
  animation: orbit-spin 18s linear infinite;
}

.orbit-auth {
  width: var(--belt-auth);
  height: var(--belt-auth);
  animation: orbit-spin-reverse 15s linear infinite;
}

.orbit-routing {
  width: var(--belt-routing);
  height: var(--belt-routing);
  animation: orbit-spin 12s linear infinite;
}

.orbit-icon {
  position: absolute;
  top: 50%;
  left: 50%;
  width: 28px;
  height: 28px;
  margin-left: -14px;
  margin-top: calc(var(--size, 14px) * -1);
  display: grid;
  place-items: center;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.04);
  transform-origin: center center;
  backdrop-filter: blur(3px);
}

/* =========================================================
   Persistent orbit labels
   ========================================================= */
.belt-label {
  --label-ring-size: 0px;
  position: absolute;
  left: calc(50% + var(--label-ring-size) * var(--label-x-factor));
  top: calc(50% - var(--label-ring-size) * var(--label-y-factor));
  font-size: 10px;
  font-family: ui-monospace, monospace;
  letter-spacing: 0.07em;
  text-transform: uppercase;
  white-space: nowrap;
  pointer-events: none;
  transform: translateY(-50%);
  opacity: 0.7;
}

.label-firewall {
  --label-ring-size: var(--belt-fw);
  color: var(--fw-main);
}

.label-auth {
  --label-ring-size: var(--belt-auth);
  color: var(--auth-main);
}

.label-routing {
  --label-ring-size: var(--belt-routing);
  color: var(--routing-main);
}

.orbit-firewall .orbit-icon {
  transform: rotate(0deg) translateY(calc(var(--belt-fw) / -2));
}

.orbit-auth .orbit-icon {
  transform: rotate(180deg) translateY(calc(var(--belt-auth) / -2));
}

.orbit-routing .orbit-icon {
  transform: rotate(90deg) translateY(calc(var(--belt-routing) / -2));
}

/* =========================================================
   Orbit icon badges
   ========================================================= */
.belt-icon-firewall {
  border: 1px solid rgba(220, 38, 38, 0.35);
  box-shadow: 0 0 14px rgba(220, 38, 38, 0.18);
}

.belt-icon-auth {
  border: 1px solid rgba(234, 179, 8, 0.35);
  box-shadow: 0 0 14px rgba(234, 179, 8, 0.16);
}

.belt-icon-routing {
  border: 1px solid var(--routing-soft);
  box-shadow: 0 0 14px rgba(16, 185, 129, 0.18);
}

/* =========================================================
   SVG icons
   ========================================================= */
.icon {
  width: 15px;
  height: 15px;
  display: block;
}

.icon-wall {
  fill: rgba(220, 38, 38, 0.16);
  stroke: var(--fw-main);
  stroke-width: 1.5;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.icon-lock {
  fill: var(--auth-main);
  stroke: var(--auth-main);
  stroke-width: 2;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.icon-routing {
  width: 18px;
  height: 18px;
  stroke: var(--routing-main);
  stroke-width: 2.6;
  fill: none;
  stroke-linecap: round;
  stroke-linejoin: round;
}

/* =========================================================
   Traffic particles
   ========================================================= */
.traffic-layer {
  position: absolute;
  inset: 0;
  pointer-events: none;
}

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
  box-shadow: 0 0 10px currentColor;
}

.pass { color: var(--cyan); }
.route { color: var(--routing-main); }
.block-fw { color: var(--fw-main); }
.block-auth { color: var(--auth-main); }

/* particle bindings */
.p1  { color: var(--violet);   animation: pass-1 1.9s linear infinite; }
.p2  { color: var(--cyan);     animation: pass-2 1.4s linear infinite; }
.p3  { color: var(--violet-2); animation: pass-3 2.1s linear infinite; }
.p4  { color: var(--cyan-2);   animation: pass-4 1.6s linear infinite; }
.p5  { color: var(--violet);   animation: pass-5 2.4s linear infinite; }
.p6  { color: var(--cyan);     animation: pass-6 1.3s linear infinite; }
.p7  { color: var(--violet-2); animation: pass-7 1.8s linear infinite; }
.p8  { color: var(--cyan-2);   animation: pass-8 2.2s linear infinite; }
.p9  { color: var(--violet);   animation: pass-9 1.5s linear infinite; }
.p10 { color: var(--cyan);     animation: pass-10 2s linear infinite; }
.p11 { color: var(--cyan-2);   animation: pass-11 1.7s linear infinite; }
.p12 { color: var(--violet-2); animation: pass-12 2.5s linear infinite; }

.r1 { animation: route-1 4.2s linear infinite; }
.r2 { animation: route-2 5.4s linear infinite; }
.r3 { animation: route-3 6.1s linear infinite; }

.fw1 { animation: block-fw-1 4.8s linear infinite; }
.fw2 { animation: block-fw-2 6.3s linear infinite; }
.fw3 { animation: block-fw-3 7.4s linear infinite; }

.a1 { animation: block-auth-1 5.2s linear infinite; }
.a2 { animation: block-auth-2 6.8s linear infinite; }
.a3 { animation: block-auth-3 8.1s linear infinite; }

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
  40%      { left: calc(50% - var(--auth-hit-x)); top: 44%; opacity: 1; }
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
  44%      { left: calc(50% - var(--auth-hit-x)); top: 50%; opacity: 1; }
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
  46%      { left: calc(50% - var(--auth-hit-x)); top: 58%; opacity: 1; }
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
  42%      { left: calc(50% - var(--auth-hit-x)); top: 46%; opacity: 1; }
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

/* =========================================================
   Traffic trajectories - routing
   ========================================================= */
@keyframes route-1 {
  0%, 20% {
    left: -40px;
    top: 30%;
    opacity: 0;
    transform: scale(0.9);
  }
  24%, 44% {
    opacity: 1;
  }
  52% {
    left: calc(50% - var(--routing-hit-x));
    top: 46%;
    opacity: 1;
    transform: scale(1.05);
  }
  62% {
    left: calc(50% + 10px);
    top: 28%;
    opacity: 1;
  }
  100% {
    left: calc(100% - 60px);
    top: 8%;
    opacity: 0;
    transform: scale(0.92);
  }
}

@keyframes route-2 {
  0%, 28% {
    left: -40px;
    top: 55%;
    opacity: 0;
    transform: scale(0.9);
  }
  32%, 50% {
    opacity: 1;
  }
  58% {
    left: calc(50% - var(--routing-hit-x));
    top: 52%;
    opacity: 1;
    transform: scale(1.05);
  }
  68% {
    left: calc(50% + 4px);
    top: 38%;
    opacity: 1;
  }
  100% {
    left: calc(100% - 90px);
    top: 18%;
    opacity: 0;
    transform: scale(0.92);
  }
}

@keyframes route-3 {
  0%, 40% {
    left: -40px;
    top: 65%;
    opacity: 0;
    transform: scale(0.9);
  }
  44%, 60% {
    opacity: 1;
  }
  68% {
    left: calc(50% - var(--routing-hit-x));
    top: 55%;
    opacity: 1;
    transform: scale(1.04);
  }
  76% {
    left: calc(50% - calc(var(--routing-hit-x) - 28px));
    top: 50%;
    opacity: 0;
    transform: scale(0.45);
  }
  100% {
    opacity: 0;
  }
}

/* =========================================================
   Traffic trajectories - blocked
   ========================================================= */
@keyframes block-fw-1 {
  /* incoming: →↘ 16°, normal: ←↑, reflected: ←↑ 17° */
  0%, 20%  { left: -40px; top: 16%; opacity: 0; }
  24%, 40% { opacity: 1; }
  50%      { left: calc(50% - var(--fw-hit-x)); top: 36%; opacity: 1; transform: scale(1.22); }
  64%      { left: calc(50% - calc(var(--fw-hit-x) + 73px)); top: 30%; opacity: 1; transform: scale(1); }
  100%     { left: -40px; top: 13%; opacity: 0; }
}

@keyframes block-fw-2 {
  /* incoming: → horizontal, normal: ← radial, reflected: ← purely horizontal */
  0%, 28%  { left: -40px; top: 50%; opacity: 0; }
  32%, 48% { opacity: 1; }
  56%      { left: calc(50% - var(--fw-hit-x)); top: 50%; opacity: 1; transform: scale(1.22); }
  70%      { left: calc(50% - calc(var(--fw-hit-x) + 83px)); top: 50%; opacity: 1; transform: scale(1); }
  100%     { left: -40px; top: 50%; opacity: 0; }
}

@keyframes block-fw-3 {
  /* incoming: →↗ 10°, normal: ←↘, reflected: ←↘ 25° */
  0%, 36%  { left: -40px; top: 76%; opacity: 0; }
  40%, 56% { opacity: 1; }
  62%      { left: calc(50% - var(--fw-hit-x)); top: 64%; opacity: 1; transform: scale(1.22); }
  76%      { left: calc(50% - calc(var(--fw-hit-x) + 96px)); top: 76%; opacity: 1; transform: scale(1); }
  100%     { left: -40px; top: 96%; opacity: 0; }
}

@keyframes block-auth-1 {
  /* incoming: →↘ 15°, normal: ←↑ (léger), reflected: ←↑ 10° */
  0%, 18%  { left: -40px; top: 22%; opacity: 0; }
  22%, 40% { opacity: 1; }
  52%      { left: calc(50% - var(--auth-hit-x)); top: 42%; opacity: 1; transform: scale(1.18); }
  66%      { left: calc(50% - calc(var(--auth-hit-x) + 85px)); top: 38%; opacity: 1; transform: scale(1); }
  100%     { left: -40px; top: 29%; opacity: 0; }
}

@keyframes block-auth-2 {
  /* incoming: →↘ 4°, normal: ← quasi-radial, reflected: ← quasi-horizontal */
  0%, 30%  { left: -40px; top: 42%; opacity: 0; }
  34%, 50% { opacity: 1; }
  58%      { left: calc(50% - var(--auth-hit-x)); top: 48%; opacity: 1; transform: scale(1.18); }
  72%      { left: calc(50% - calc(var(--auth-hit-x) + 97px)); top: 47%; opacity: 1; transform: scale(1); }
  100%     { left: -40px; top: 46%; opacity: 0; }
}

@keyframes block-auth-3 {
  /* incoming: →↗ 7°, normal: ←↘, reflected: ←↘ 17° */
  0%, 40%  { left: -40px; top: 68%; opacity: 0; }
  44%, 58% { opacity: 1; }
  64%      { left: calc(50% - var(--auth-hit-x)); top: 58%; opacity: 1; transform: scale(1.18); }
  78%      { left: calc(50% - calc(var(--auth-hit-x) + 113px)); top: 67%; opacity: 1; transform: scale(1); }
  100%     { left: -40px; top: 81%; opacity: 0; }
}

/* =========================================================
   Logo animations
   ========================================================= */
@keyframes core-pulse {
  0%, 100% {
    transform: scale(0.96);
    opacity: 0.18;
  }
  49% {
    transform: scale(0.96);
    opacity: 0.18;
  }
  54% {
    transform: scale(1.05);
    opacity: 0.44;
  }
  62% {
    transform: scale(0.98);
    opacity: 0.14;
  }
}

/* =========================================================
   Orbit rotation
   ========================================================= */
@keyframes orbit-spin {
  from { transform: translate(-50%, -50%) rotate(0deg); }
  to   { transform: translate(-50%, -50%) rotate(360deg); }
}

@keyframes orbit-spin-reverse {
  from { transform: translate(-50%, -50%) rotate(360deg); }
  to   { transform: translate(-50%, -50%) rotate(0deg); }
}

/* =========================================================
   Belt idle states
   ========================================================= */
@keyframes fw-belt-idle {
  0%, 100% { box-shadow: 0 0 0 0 rgba(220, 38, 38, 0); }
  50%      { box-shadow: 0 0 12px rgba(220, 38, 38, 0.06); }
}

@keyframes auth-belt-idle {
  0%, 100% { box-shadow: 0 0 0 0 rgba(234, 179, 8, 0); }
  50%      { box-shadow: 0 0 12px rgba(234, 179, 8, 0.05); }
}

@keyframes routing-idle {
  0%, 100% { box-shadow: none; }
  50%      { box-shadow: 0 0 10px rgba(16, 185, 129, 0.08); }
}

/* =========================================================
   Belt bump reactions — each keyframe synced to its particle
   ========================================================= */

/* fw1: 4.8s, hits at 50% */
@keyframes bump-fw-1 {
  0%, 47%, 56%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  50% { border-color: var(--fw-main); box-shadow: 0 0 16px var(--fw-glow); border-width: 3px; }
}

/* fw2: 6.3s, hits at 56% */
@keyframes bump-fw-2 {
  0%, 53%, 62%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  56% { border-color: var(--fw-main); box-shadow: 0 0 16px var(--fw-glow); border-width: 3px; }
}

/* fw3: 7.4s, hits at 62% */
@keyframes bump-fw-3 {
  0%, 59%, 68%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  62% { border-color: var(--fw-main); box-shadow: 0 0 16px var(--fw-glow); border-width: 3px; }
}

/* a1: 5.2s, hits at 52% */
@keyframes bump-auth-1 {
  0%, 49%, 58%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  52% { border-color: var(--auth-main); box-shadow: 0 0 16px var(--auth-glow); border-width: 3px; }
}

/* a2: 6.8s, hits at 58% */
@keyframes bump-auth-2 {
  0%, 55%, 64%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  58% { border-color: var(--auth-main); box-shadow: 0 0 16px var(--auth-glow); border-width: 3px; }
}

/* a3: 8.1s, hits at 64% */
@keyframes bump-auth-3 {
  0%, 61%, 70%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  64% { border-color: var(--auth-main); box-shadow: 0 0 16px var(--auth-glow); border-width: 3px; }
}

/* r1: 4.2s, hits at 52% */
@keyframes bump-r-1 {
  0%, 49%, 58%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  52% { border-color: var(--routing-main); box-shadow: 0 0 14px var(--routing-glow); border-width: 2px; }
}

/* r2: 5.4s, hits at 58% */
@keyframes bump-r-2 {
  0%, 55%, 64%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  58% { border-color: var(--routing-main); box-shadow: 0 0 14px var(--routing-glow); border-width: 2px; }
}

/* r3: 6.1s, hits at 68% */
@keyframes bump-r-3 {
  0%, 65%, 74%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  68% { border-color: var(--routing-main); box-shadow: 0 0 14px var(--routing-glow); border-width: 2px; }
}

/* pass-through: softer glow, particles cross routing ring on the way to center */
/* p1: 1.9s, crosses at ~43% */
@keyframes bump-p-1 {
  0%, 40%, 46%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  43% { border-color: var(--routing-main); box-shadow: 0 0 10px rgba(16, 185, 129, 0.4); border-width: 2px; }
}
/* p2: 1.4s, crosses at ~42% */
@keyframes bump-p-2 {
  0%, 39%, 45%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  42% { border-color: var(--routing-main); box-shadow: 0 0 10px rgba(16, 185, 129, 0.4); border-width: 2px; }
}
/* p3: 2.1s, crosses at ~45% */
@keyframes bump-p-3 {
  0%, 42%, 48%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  45% { border-color: var(--routing-main); box-shadow: 0 0 10px rgba(16, 185, 129, 0.4); border-width: 2px; }
}
/* p4: 1.6s, crosses at ~46% */
@keyframes bump-p-4 {
  0%, 43%, 49%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  46% { border-color: var(--routing-main); box-shadow: 0 0 10px rgba(16, 185, 129, 0.4); border-width: 2px; }
}
/* p5: 2.4s, crosses at ~50% */
@keyframes bump-p-5 {
  0%, 47%, 53%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  50% { border-color: var(--routing-main); box-shadow: 0 0 10px rgba(16, 185, 129, 0.4); border-width: 2px; }
}
/* p6: 1.3s, crosses at ~40% */
@keyframes bump-p-6 {
  0%, 37%, 43%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  40% { border-color: var(--routing-main); box-shadow: 0 0 10px rgba(16, 185, 129, 0.4); border-width: 2px; }
}
/* p7: 1.8s, crosses at ~53% */
@keyframes bump-p-7 {
  0%, 50%, 56%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  53% { border-color: var(--routing-main); box-shadow: 0 0 10px rgba(16, 185, 129, 0.4); border-width: 2px; }
}
/* p8: 2.2s, crosses at ~54% */
@keyframes bump-p-8 {
  0%, 51%, 57%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  54% { border-color: var(--routing-main); box-shadow: 0 0 10px rgba(16, 185, 129, 0.4); border-width: 2px; }
}
/* p9: 1.5s, crosses at ~48% */
@keyframes bump-p-9 {
  0%, 45%, 51%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  48% { border-color: var(--routing-main); box-shadow: 0 0 10px rgba(16, 185, 129, 0.4); border-width: 2px; }
}
/* p10: 2.0s, crosses at ~48% */
@keyframes bump-p-10 {
  0%, 45%, 51%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  48% { border-color: var(--routing-main); box-shadow: 0 0 10px rgba(16, 185, 129, 0.4); border-width: 2px; }
}
/* p11: 1.7s, crosses at ~44% */
@keyframes bump-p-11 {
  0%, 41%, 47%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  44% { border-color: var(--routing-main); box-shadow: 0 0 10px rgba(16, 185, 129, 0.4); border-width: 2px; }
}
/* p12: 2.5s, crosses at ~51% */
@keyframes bump-p-12 {
  0%, 48%, 54%, 100% { border-color: transparent; box-shadow: none; border-width: 1px; }
  51% { border-color: var(--routing-main); box-shadow: 0 0 10px rgba(16, 185, 129, 0.4); border-width: 2px; }
}

/* =========================================================
   Responsive
   ========================================================= */
@media (max-width: 900px) {
  .server-core {
    --scene-h: 320px;
    --logo-size: 150px;
    --belt-routing: 180px;
    --belt-auth: 230px;
    --belt-fw: 280px;
    --dot: 6px;
  }

  .orbit-icon {
    width: 24px;
    height: 24px;
    margin-left: -12px;
  }

  .icon {
    width: 13px;
    height: 13px;
  }

  .icon-routing {
    width: 15px;
    height: 15px;
  }

}

@media (max-width: 640px) {
  .server-core {
    --scene-h: 236px;
    --logo-size: 110px;
    --belt-routing: 126px;
    --belt-auth: 162px;
    --belt-fw: 198px;
    --dot: 5px;
    margin-bottom: -65px;
  }

  .core-ring {
    inset: 6px;
  }

  .orbit-icon {
    width: 21px;
    height: 21px;
    margin-left: -10.5px;
  }

  .icon {
    width: 11px;
    height: 11px;
  }

  .icon-routing {
    width: 13px;
    height: 13px;
  }

}

/* =========================================================
   Reduced motion
   ========================================================= */
@media (prefers-reduced-motion: reduce) {
  .req,
  .core-ring,
  .belt-firewall,
  .belt-auth,
  .belt-routing,
  .orbit-firewall,
  .orbit-auth,
  .orbit-routing,
  .bump {
    animation: none !important;
  }

  .traffic-layer {
    display: none;
  }
}
</style>