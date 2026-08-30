<template>
  <div
    class="server-core"
    role="img"
    aria-label="SimpleW core extended by optional Authentication, Firewall, and Let's Encrypt addons"
  >
    <div class="core-zone">
      <div class="addon-layer addon-auth">
        <div class="belt belt-auth">
          <div class="bump bump-auth-1"></div>
          <div class="bump bump-auth-2"></div>
          <div class="bump bump-auth-3"></div>
        </div>
        <div class="orbit orbit-auth">
          <div class="orbit-icon belt-icon-auth" aria-hidden="true">
            <svg viewBox="0 0 24 24" class="icon icon-lock">
              <rect x="6" y="11" width="12" height="10" rx="2.5" />
              <path d="M9 11V8.5a3 3 0 0 1 6 0V11" />
              <circle cx="12" cy="16.5" r="1.5" class="lock-keyhole" />
            </svg>
          </div>
        </div>
        <div class="feature-particles particles-auth" aria-hidden="true">
          <span class="feature-dot auth-dot a1"></span>
          <span class="feature-dot auth-dot a2"></span>
          <span class="feature-dot auth-dot a3"></span>
        </div>
      </div>

      <div class="addon-layer addon-firewall">
        <div class="belt belt-firewall">
          <div class="bump bump-fw-1"></div>
          <div class="bump bump-fw-2"></div>
          <div class="bump bump-fw-3"></div>
        </div>
        <div class="orbit orbit-firewall">
          <div class="orbit-icon belt-icon-firewall" aria-hidden="true">
            <svg viewBox="0 0 24 24" class="icon icon-wall">
              <path d="M2 20V6H7V12H10V6H14V12H17V6H22V20Z" />
              <path d="M2 16H22" />
            </svg>
          </div>
        </div>
        <div class="feature-particles particles-firewall" aria-hidden="true">
          <span class="feature-dot firewall-dot fw1"></span>
          <span class="feature-dot firewall-dot fw2"></span>
          <span class="feature-dot firewall-dot fw3"></span>
        </div>
      </div>

      <div class="addon-layer addon-tls">
        <div class="belt belt-tls">
          <div class="bump bump-tls-1"></div>
          <div class="bump bump-tls-2"></div>
          <div class="bump bump-tls-3"></div>
        </div>
        <div class="orbit orbit-tls">
          <div class="orbit-icon belt-icon-tls" aria-hidden="true">
            <svg viewBox="0 0 24 24" class="icon icon-shield">
              <path d="M12 3 20 6v5c0 5-3.4 8.3-8 10-4.6-1.7-8-5-8-10V6Z" />
              <path d="m8.5 12 2.25 2.25 4.75-4.75" />
            </svg>
          </div>
        </div>
        <div class="feature-particles particles-tls" aria-hidden="true">
          <span class="feature-dot tls-dot tls1"></span>
          <span class="feature-dot tls-dot tls2"></span>
          <span class="feature-dot tls-dot tls3"></span>
        </div>
      </div>

      <div class="logo-zone">
        <div class="core-ring"></div>
        <img src="/logo.svg" alt="SimpleW" class="logo" />
      </div>
    </div>

    <div class="terminal-nav" aria-hidden="true">
      <div class="terminal-stage stage-core-intro">
        <div class="terminal-context terminal-context-strong">CORE</div>
      </div>

      <div class="terminal-stage terminal-root stage-root-auth">
        <div class="terminal-options">
          <div class="terminal-row">CORE</div>
          <div class="terminal-row">ADDONS</div>
          <span class="terminal-selection selection-root-auth"></span>
        </div>
      </div>

      <div class="terminal-stage stage-addons-auth">
        <div class="terminal-context">ADDONS</div>
        <div class="terminal-options">
          <div class="terminal-row">AUTHENTICATION</div>
          <div class="terminal-row">FIREWALL</div>
          <div class="terminal-row">ENGINE</div>
          <div class="terminal-row">LET'S ENCRYPT</div>
          <div class="terminal-row">TEMPLATE</div>
          <span class="terminal-selection selection-addons-auth"></span>
        </div>
      </div>

      <div class="terminal-stage stage-auth-methods">
        <div class="terminal-context">AUTHENTICATION</div>
        <div class="terminal-options">
          <div class="terminal-row">JWT</div>
          <div class="terminal-row">BASIC AUTH</div>
          <div class="terminal-row row-openid">OPENID</div>
          <div class="terminal-row">MUTUAL TLS</div>
          <span class="terminal-selection selection-auth-methods"></span>
        </div>
      </div>

      <div class="terminal-stage stage-core-firewall">
        <div class="terminal-context terminal-context-strong">CORE</div>
      </div>

      <div class="terminal-stage terminal-root stage-root-firewall">
        <div class="terminal-options">
          <div class="terminal-row">CORE</div>
          <div class="terminal-row">ADDONS</div>
          <span class="terminal-selection selection-root-firewall"></span>
        </div>
      </div>

      <div class="terminal-stage stage-addons-firewall">
        <div class="terminal-context">ADDONS</div>
        <div class="terminal-options">
          <div class="terminal-row">AUTHENTICATION</div>
          <div class="terminal-row row-firewall">FIREWALL</div>
          <div class="terminal-row">ENGINE</div>
          <div class="terminal-row">LET'S ENCRYPT</div>
          <div class="terminal-row">TEMPLATE</div>
          <span class="terminal-selection selection-addons-firewall"></span>
        </div>
      </div>

      <div class="terminal-stage stage-core-tls">
        <div class="terminal-context terminal-context-strong">CORE</div>
      </div>

      <div class="terminal-stage terminal-root stage-root-tls">
        <div class="terminal-options">
          <div class="terminal-row">CORE</div>
          <div class="terminal-row">ADDONS</div>
          <span class="terminal-selection selection-root-tls"></span>
        </div>
      </div>

      <div class="terminal-stage stage-addons-tls">
        <div class="terminal-context">ADDONS</div>
        <div class="terminal-options">
          <div class="terminal-row">AUTHENTICATION</div>
          <div class="terminal-row">FIREWALL</div>
          <div class="terminal-row">ENGINE</div>
          <div class="terminal-row row-tls">LET'S ENCRYPT</div>
          <div class="terminal-row">TEMPLATE</div>
          <span class="terminal-selection selection-addons-tls"></span>
        </div>
      </div>
    </div>

    <div class="traffic-layer" aria-hidden="true">
      <div class="traffic-group traffic-pass">
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
      </div>
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

  --ring-focus: 330px;
  --ring-auth: 244px;
  --ring-firewall: 280px;
  --ring-tls: 316px;

  --auth-hit-x: calc(var(--ring-auth) / 2);

  --dot: 7px;
  --req-idle: #94a3b8;
  --traffic-start: 1s;

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

  --tls-main: #10b981;
  --tls-soft: rgba(16, 185, 129, 0.2);
  --tls-glow: rgba(16, 185, 129, 0.55);

  --terminal-row: 18px;
  --terminal-text: rgba(226, 232, 240, 0.74);
  --terminal-active: rgba(226, 232, 240, 0.96);

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
  animation: core-arrive 0.45s cubic-bezier(0.22, 1, 0.36, 1) both;
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

.terminal-nav {
  position: absolute;
  right: auto;
  bottom: 18px;
  left: 16px;
  z-index: 30;
  width: 170px;
  height: 118px;
  color: var(--terminal-text);
  font-family: ui-monospace, monospace;
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0;
  line-height: var(--terminal-row);
  text-transform: uppercase;
  pointer-events: none;
}

.terminal-stage {
  position: absolute;
  inset: 0;
  opacity: 0;
}

.terminal-root {
  padding-top: 38px;
}

.stage-core-intro,
.stage-core-firewall,
.stage-core-tls {
  padding-top: 46px;
}

.stage-auth-methods {
  padding-top: 10px;
}

.terminal-context {
  position: relative;
  height: var(--terminal-row);
  padding-left: 18px;
  color: rgba(148, 163, 184, 0.66);
}

.terminal-context-strong {
  color: var(--terminal-active);
  text-shadow: 0 0 10px rgba(34, 211, 238, 0.3);
}

.terminal-context-strong::before {
  content: ">";
  position: absolute;
  top: 0;
  left: 4px;
  color: var(--cyan);
}

.terminal-options {
  position: relative;
}

.terminal-row {
  position: relative;
  z-index: 1;
  height: var(--terminal-row);
  padding-left: 18px;
  color: var(--terminal-text);
  white-space: nowrap;
}

.terminal-selection {
  position: absolute;
  top: 0;
  left: 0;
  z-index: 0;
  width: 100%;
  height: var(--terminal-row);
  color: var(--cyan);
  border-left: 2px solid currentColor;
  border-radius: 2px;
  background: rgba(148, 163, 184, 0.1);
  box-shadow: 0 0 10px rgba(34, 211, 238, 0.08);
}

.terminal-selection::before {
  content: ">";
  position: absolute;
  left: 3px;
  line-height: var(--terminal-row);
  text-shadow: 0 0 8px currentColor;
}

.stage-core-intro { animation: terminal-stage-window-long 3.5s linear both; }
.stage-root-auth { animation: terminal-stage-window 0.8s linear 3.5s both; }
.stage-addons-auth { animation: terminal-stage-window 0.6s linear 4.3s both; }
.stage-auth-methods { animation: terminal-stage-window 1.3s linear 4.9s both; }
.stage-core-firewall { animation: terminal-stage-window 0.4s linear 7.5s both; }
.stage-root-firewall { animation: terminal-stage-window 0.8s linear 7.9s both; }
.stage-addons-firewall { animation: terminal-stage-window 1.1s linear 8.7s both; }
.stage-core-tls { animation: terminal-stage-window 0.4s linear 11.2s both; }
.stage-root-tls { animation: terminal-stage-window 0.8s linear 11.6s both; }
.stage-addons-tls { animation: terminal-stage-window 2.1s linear 12.4s both; }

.selection-root-auth { animation: terminal-select-root 0.8s ease-in-out 3.5s both; }
.selection-auth-methods { animation: terminal-select-auth 1.3s ease-in-out 4.9s both; }
.selection-root-firewall { animation: terminal-select-root 0.8s ease-in-out 7.9s both; }
.selection-addons-firewall { animation: terminal-select-firewall 1.1s ease-in-out 8.7s both; }
.selection-root-tls { animation: terminal-select-root 0.8s ease-in-out 11.6s both; }
.selection-addons-tls { animation: terminal-select-tls 2.1s ease-in-out 12.4s both; }

.row-openid { animation: terminal-confirm-auth 0.3s ease 5.8s both; }
.row-firewall { animation: terminal-confirm-firewall 0.3s ease 9.4s both; }
.row-tls { animation: terminal-confirm-tls 0.3s ease 14.05s both; }

/* =========================================================
   Belts / rings
   ========================================================= */
.addon-layer {
  position: absolute;
  top: 50%;
  left: 50%;
  width: var(--ring-focus);
  height: var(--ring-focus);
  opacity: 0;
  transform: translate(-50%, -50%);
  pointer-events: none;
}

.addon-auth {
  --addon-start: 6.1s;
  animation:
    layer-enter 0.2s ease 6.1s both,
    compact-focus-to-auth 0.35s ease-in-out 9.7s forwards;
}

.addon-firewall {
  --addon-start: 9.7s;
  animation:
    layer-enter 0.2s ease 9.7s both,
    compact-focus-to-firewall 0.35s ease-in-out 14.4s forwards;
}

.addon-tls {
  --addon-start: 14.4s;
  animation:
    layer-enter 0.2s ease 14.4s both,
    compact-focus-to-tls 0.35s ease-in-out 15.9s forwards;
}

.belt {
  position: absolute;
  inset: 0;
  border-radius: 50%;
  border: 1px solid;
  pointer-events: none;
}

.belt-auth {
  border-color: var(--auth-soft);
  animation:
    ring-reveal 0.45s cubic-bezier(0.22, 1, 0.36, 1) 6.25s both,
    auth-belt-idle 5.8s linear 6.7s infinite;
}

.belt-firewall {
  border-color: var(--fw-soft);
  animation:
    ring-reveal 0.45s cubic-bezier(0.22, 1, 0.36, 1) 9.85s both,
    fw-belt-idle 5.4s linear 10.3s infinite;
}

.belt-tls {
  border-color: var(--tls-soft);
  animation:
    ring-reveal 0.45s cubic-bezier(0.22, 1, 0.36, 1) 14.55s both,
    tls-belt-idle 6.2s linear 15s infinite;
}

/* =========================================================
   Belt bump overlays
   ========================================================= */
.bump {
  position: absolute;
  inset: -1px;
  border-radius: 50%;
  border: 1px solid transparent;
  box-shadow: none;
  pointer-events: none;
}

.belt-firewall::before,
.belt-auth::before,
.belt-tls::before {
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
  inset: 0;
  border-radius: 50%;
  pointer-events: none;
}

.orbit-auth { animation: orbit-spin-reverse 15s linear 6.55s infinite; }
.orbit-firewall {
  animation: orbit-spin 18s linear 10.15s infinite;
}
.orbit-tls { animation: orbit-spin-reverse 21s linear 14.85s infinite; }

.orbit-icon {
  position: absolute;
  top: 0;
  left: 50%;
  width: 28px;
  height: 28px;
  margin-left: -14px;
  margin-top: -14px;
  display: grid;
  place-items: center;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.04);
  transform-origin: center center;
  backdrop-filter: blur(3px);
  animation: icon-launch 0.45s cubic-bezier(0.45, 0, 0.55, 1) var(--addon-start) both;
}

.orbit-icon::before {
  content: "+";
  position: absolute;
  top: 50%;
  right: calc(100% + 5px);
  font-family: ui-monospace, monospace;
  font-size: 13px;
  font-weight: 700;
  line-height: 1;
  color: currentColor;
  text-shadow: 0 0 9px currentColor;
  transform: translateY(-50%);
  animation: launch-plus 0.45s cubic-bezier(0.45, 0, 0.55, 1) var(--addon-start) both;
}

/* =========================================================
   Orbit icon badges
   ========================================================= */
.belt-icon-firewall {
  color: var(--fw-main);
  border: 1px solid rgba(220, 38, 38, 0.35);
  box-shadow: 0 0 14px rgba(220, 38, 38, 0.18);
}

.belt-icon-auth {
  color: var(--auth-main);
  border: 1px solid rgba(234, 179, 8, 0.35);
  box-shadow: 0 0 14px rgba(234, 179, 8, 0.16);
}

.belt-icon-tls {
  color: var(--tls-main);
  border: 1px solid var(--tls-soft);
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

.lock-keyhole {
  fill: rgba(0, 0, 0, 0.35);
  stroke: none;
}

.icon-shield {
  width: 17px;
  height: 17px;
  stroke: var(--tls-main);
  stroke-width: 1.8;
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

.traffic-group {
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

.pass { --req-active: var(--cyan); }

/* always-on connection bindings */
.p1  { --req-active: var(--violet);   animation: pass-1 1.9s linear var(--traffic-start) infinite, color-at-42 1.9s linear var(--traffic-start) infinite; }
.p2  { --req-active: var(--cyan);     animation: pass-2 1.4s linear var(--traffic-start) infinite, color-at-40 1.4s linear var(--traffic-start) infinite; }
.p3  { --req-active: var(--violet-2); animation: pass-3 2.1s linear var(--traffic-start) infinite, color-at-45 2.1s linear var(--traffic-start) infinite; }
.p4  { --req-active: var(--cyan-2);   animation: pass-4 1.6s linear var(--traffic-start) infinite, color-at-44 1.6s linear var(--traffic-start) infinite; }
.p5  { --req-active: var(--violet);   animation: pass-5 2.4s linear var(--traffic-start) infinite, color-at-49 2.4s linear var(--traffic-start) infinite; }
.p6  { --req-active: var(--cyan);     animation: pass-6 1.3s linear var(--traffic-start) infinite, color-at-39 1.3s linear var(--traffic-start) infinite; }
.p7  { --req-active: var(--violet-2); animation: pass-7 1.8s linear var(--traffic-start) infinite, color-at-52 1.8s linear var(--traffic-start) infinite; }
.p8  { --req-active: var(--cyan-2);   animation: pass-8 2.2s linear var(--traffic-start) infinite, color-at-54 2.2s linear var(--traffic-start) infinite; }
.p9  { --req-active: var(--violet);   animation: pass-9 1.5s linear var(--traffic-start) infinite, color-at-46 1.5s linear var(--traffic-start) infinite; }
.p10 { --req-active: var(--cyan);     animation: pass-10 2s linear var(--traffic-start) infinite, color-at-48 2s linear var(--traffic-start) infinite; }
.p11 { --req-active: var(--cyan-2);   animation: pass-11 1.7s linear var(--traffic-start) infinite, color-at-42 1.7s linear var(--traffic-start) infinite; }
.p12 { --req-active: var(--violet-2); animation: pass-12 2.5s linear var(--traffic-start) infinite, color-at-50 2.5s linear var(--traffic-start) infinite; }

/* addon-specific particles stay anchored to their moving ring */
.feature-particles {
  position: absolute;
  inset: 0;
  z-index: 3;
  opacity: 0;
  overflow: visible;
  pointer-events: none;
  animation: feature-reveal 0.25s ease calc(var(--addon-start) + 0.3s) both;
}

.feature-dot {
  position: absolute;
  left: -28%;
  top: 50%;
  width: var(--dot);
  height: var(--dot);
  margin-top: calc(var(--dot) * -0.5);
  border-radius: 50%;
  color: var(--req-idle);
  opacity: 0;
}

.feature-dot::after {
  content: "";
  position: absolute;
  inset: 0;
  border-radius: inherit;
  background: currentColor;
  box-shadow: 0 0 10px currentColor;
}

.auth-dot { --reject-color: var(--auth-main); }
.firewall-dot { --reject-color: var(--fw-main); }
.tls-dot { color: var(--req-idle); }

.a1 { animation: reject-auth 4.8s linear -0.5s infinite; }
.a2 { animation: reject-auth 6.1s linear -2.4s infinite; }
.a3 { animation: reject-auth 7.2s linear -4.1s infinite; }
.bump-auth-1 { --hit-color: var(--auth-main); --hit-glow: var(--auth-glow); animation: ring-hit 4.8s linear -0.5s infinite; }
.bump-auth-2 { --hit-color: var(--auth-main); --hit-glow: var(--auth-glow); animation: ring-hit 6.1s linear -2.4s infinite; }
.bump-auth-3 { --hit-color: var(--auth-main); --hit-glow: var(--auth-glow); animation: ring-hit 7.2s linear -4.1s infinite; }

.fw1 { animation: reject-firewall 5.2s linear -1.4s infinite; }
.fw2 { animation: reject-firewall 6.5s linear -3.1s infinite; }
.fw3 { animation: reject-firewall 7.6s linear -5s infinite; }
.bump-fw-1 { --hit-color: var(--fw-main); --hit-glow: var(--fw-glow); animation: ring-hit 5.2s linear -1.4s infinite; }
.bump-fw-2 { --hit-color: var(--fw-main); --hit-glow: var(--fw-glow); animation: ring-hit 6.5s linear -3.1s infinite; }
.bump-fw-3 { --hit-color: var(--fw-main); --hit-glow: var(--fw-glow); animation: ring-hit 7.6s linear -5s infinite; }

.tls1 { animation: secure-connection 4.4s linear -1s infinite; }
.tls2 { animation: secure-connection 5.6s linear -3.2s infinite; }
.tls3 { animation: secure-connection 6.8s linear -4.8s infinite; }
.bump-tls-1 { --hit-color: var(--tls-main); --hit-glow: var(--tls-glow); animation: ring-hit 4.4s linear -1s infinite; }
.bump-tls-2 { --hit-color: var(--tls-main); --hit-glow: var(--tls-glow); animation: ring-hit 5.6s linear -3.2s infinite; }
.bump-tls-3 { --hit-color: var(--tls-main); --hit-glow: var(--tls-glow); animation: ring-hit 6.8s linear -4.8s infinite; }

/* particles start neutral, then adopt the color of the ring they touch */
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
   Addon-specific particle behavior
   ========================================================= */
@keyframes reject-auth {
  0%, 22% { left: -30%; top: 30%; color: var(--req-idle); opacity: 0; transform: scale(0.85); }
  28%, 50% { color: var(--req-idle); opacity: 1; }
  61.9% { color: var(--req-idle); }
  62% { left: 0; top: 44%; color: var(--reject-color); opacity: 1; transform: scale(1.22); }
  76% { left: -19%; top: 37%; color: var(--reject-color); opacity: 1; transform: scale(0.95); }
  100% { left: -34%; top: 25%; color: var(--reject-color); opacity: 0; transform: scale(0.78); }
}

@keyframes reject-firewall {
  0%, 24% { left: -34%; top: 68%; color: var(--req-idle); opacity: 0; transform: scale(0.85); }
  30%, 50% { color: var(--req-idle); opacity: 1; }
  61.9% { color: var(--req-idle); }
  62% { left: 0; top: 58%; color: var(--reject-color); opacity: 1; transform: scale(1.25); }
  76% { left: -23%; top: 69%; color: var(--reject-color); opacity: 1; transform: scale(0.95); }
  100% { left: -38%; top: 82%; color: var(--reject-color); opacity: 0; transform: scale(0.78); }
}

@keyframes secure-connection {
  0%, 20% { left: -34%; top: 42%; color: var(--req-idle); opacity: 0; transform: scale(0.82); }
  26%, 50% { color: var(--req-idle); opacity: 1; }
  62% { left: 0; top: 48%; color: var(--req-idle); opacity: 1; transform: scale(1.25); }
  78% { left: 30%; top: 50%; color: var(--req-idle); opacity: 1; transform: scale(1); }
  100% { left: 56%; top: 50%; color: var(--req-idle); opacity: 0; transform: scale(0.78); }
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
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

@keyframes orbit-spin-reverse {
  from { transform: rotate(360deg); }
  to { transform: rotate(0deg); }
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

@keyframes tls-belt-idle {
  0%, 100% { box-shadow: 0 0 0 rgba(16, 185, 129, 0); }
  50% { box-shadow: 0 0 14px rgba(16, 185, 129, 0.07); }
}

/* =========================================================
   Belt reactions synchronized with local addon particles
   ========================================================= */
@keyframes ring-hit {
  0%, 57%, 69%, 100% {
    border-color: transparent;
    border-width: 1px;
    box-shadow: none;
  }
  62% {
    border-color: var(--hit-color);
    border-width: 3px;
    box-shadow: 0 0 16px var(--hit-glow);
  }
}
/* =========================================================
   Responsive
   ========================================================= */
@media (max-width: 900px) {
  .server-core {
    --scene-h: 320px;
    --logo-size: 150px;
    --ring-focus: 280px;
    --ring-auth: 202px;
    --ring-firewall: 234px;
    --ring-tls: 266px;
    --terminal-row: 17px;
    --dot: 6px;
  }

  .terminal-nav {
    bottom: 14px;
    left: 12px;
    width: 150px;
    height: 108px;
    font-size: 10px;
  }

  .orbit-icon {
    width: 24px;
    height: 24px;
    margin-left: -12px;
    margin-top: -12px;
  }

  .icon {
    width: 13px;
    height: 13px;
  }

}

@media (max-width: 640px) {
  .server-core {
    --scene-h: 236px;
    --logo-size: 110px;
    --ring-focus: 198px;
    --ring-auth: 142px;
    --ring-firewall: 164px;
    --ring-tls: 186px;
    --terminal-row: 14px;
    --dot: 5px;
    margin-bottom: -65px;
  }

  .terminal-nav {
    top: calc(var(--scene-h) - 92px);
    right: auto;
    bottom: auto;
    left: 0;
    width: 100px;
    height: 88px;
    font-size: 8px;
  }

  .terminal-root {
    padding-top: 24px;
  }

  .stage-core-intro,
  .stage-core-firewall,
  .stage-core-tls {
    padding-top: 32px;
  }

  .stage-auth-methods {
    padding-top: 4px;
  }

  .terminal-context,
  .terminal-row {
    padding-left: 12px;
  }

  .terminal-context-strong::before,
  .terminal-selection::before {
    left: 2px;
  }

  .core-ring {
    inset: 6px;
  }

  .orbit-icon {
    width: 21px;
    height: 21px;
    margin-left: -10.5px;
    margin-top: -10.5px;
  }

  .icon {
    width: 11px;
    height: 11px;
  }

}

/* =========================================================
   Progressive assembly
   ========================================================= */
@keyframes core-arrive {
  from {
    opacity: 0;
    transform: translate(-50%, -50%) scale(0.92);
  }
  to {
    opacity: 1;
    transform: translate(-50%, -50%) scale(1);
  }
}

@keyframes terminal-stage-window {
  0%, 100% { opacity: 0; }
  12%, 88% { opacity: 1; }
}

@keyframes terminal-stage-window-long {
  0%, 100% { opacity: 0; }
  3%, 97% { opacity: 1; }
}

@keyframes terminal-select-root {
  0%, 35% { transform: translateY(0); }
  55%, 100% { transform: translateY(var(--terminal-row)); }
}

@keyframes terminal-select-auth {
  0%, 22% {
    color: var(--cyan);
    background-color: rgba(148, 163, 184, 0.1);
    transform: translateY(0);
  }
  32%, 48% {
    color: var(--cyan);
    background-color: rgba(148, 163, 184, 0.1);
    transform: translateY(var(--terminal-row));
  }
  58%, 72% {
    color: var(--cyan);
    background-color: rgba(148, 163, 184, 0.1);
    transform: translateY(calc(var(--terminal-row) * 2));
  }
  82%, 100% {
    color: var(--auth-main);
    background-color: var(--auth-soft);
    box-shadow: 0 0 12px var(--auth-glow);
    transform: translateY(calc(var(--terminal-row) * 2));
  }
}

@keyframes terminal-select-firewall {
  0%, 35% {
    color: var(--cyan);
    background-color: rgba(148, 163, 184, 0.1);
    transform: translateY(0);
  }
  55%, 72% {
    color: var(--cyan);
    background-color: rgba(148, 163, 184, 0.1);
    transform: translateY(var(--terminal-row));
  }
  82%, 100% {
    color: var(--fw-main);
    background-color: var(--fw-soft);
    box-shadow: 0 0 12px var(--fw-glow);
    transform: translateY(var(--terminal-row));
  }
}

@keyframes terminal-select-tls {
  0%, 15% {
    color: var(--cyan);
    background-color: rgba(148, 163, 184, 0.1);
    transform: translateY(0);
  }
  26%, 37% {
    color: var(--cyan);
    background-color: rgba(148, 163, 184, 0.1);
    transform: translateY(var(--terminal-row));
  }
  48%, 59% {
    color: var(--cyan);
    background-color: rgba(148, 163, 184, 0.1);
    transform: translateY(calc(var(--terminal-row) * 2));
  }
  70%, 82% {
    color: var(--cyan);
    background-color: rgba(148, 163, 184, 0.1);
    transform: translateY(calc(var(--terminal-row) * 3));
  }
  90%, 100% {
    color: var(--tls-main);
    background-color: var(--tls-soft);
    box-shadow: 0 0 12px var(--tls-glow);
    transform: translateY(calc(var(--terminal-row) * 3));
  }
}

@keyframes terminal-confirm-auth {
  from { color: var(--terminal-text); text-shadow: none; }
  to { color: var(--auth-main); text-shadow: 0 0 8px var(--auth-glow); }
}

@keyframes terminal-confirm-firewall {
  from { color: var(--terminal-text); text-shadow: none; }
  to { color: var(--fw-main); text-shadow: 0 0 8px var(--fw-glow); }
}

@keyframes terminal-confirm-tls {
  from { color: var(--terminal-text); text-shadow: none; }
  to { color: var(--tls-main); text-shadow: 0 0 8px var(--tls-glow); }
}

@keyframes layer-enter {
  from {
    opacity: 0;
    transform: translate(-50%, -50%) scale(0.9);
  }
  to {
    opacity: 1;
    transform: translate(-50%, -50%) scale(1);
  }
}

@keyframes ring-reveal {
  from {
    opacity: 0;
    transform: scale(0.86);
  }
  to {
    opacity: 1;
    transform: scale(1);
  }
}

@keyframes icon-launch {
  0% {
    top: 50%;
    opacity: 0;
    transform: scale(0.45);
  }
  22% {
    opacity: 1;
  }
  76% {
    opacity: 1;
    transform: scale(1.08);
  }
  100% {
    top: 0;
    opacity: 1;
    transform: scale(1);
  }
}

@keyframes launch-plus {
  0%, 12% { opacity: 0; transform: translateY(-50%) scale(0.6); }
  28%, 62% { opacity: 0.9; transform: translateY(-50%) scale(1); }
  100% { opacity: 0; transform: translateY(-50%) scale(0.75); }
}

@keyframes feature-reveal {
  from { opacity: 0; }
  to { opacity: 1; }
}

@keyframes compact-focus-to-tls {
  from { width: var(--ring-focus); height: var(--ring-focus); }
  to { width: var(--ring-tls); height: var(--ring-tls); }
}

@keyframes compact-focus-to-firewall {
  from { width: var(--ring-focus); height: var(--ring-focus); }
  to { width: var(--ring-firewall); height: var(--ring-firewall); }
}

@keyframes compact-focus-to-auth {
  from { width: var(--ring-focus); height: var(--ring-focus); }
  to { width: var(--ring-auth); height: var(--ring-auth); }
}

/* =========================================================
   Reduced motion
   ========================================================= */
@media (prefers-reduced-motion: reduce) {
  .req,
  .feature-dot,
  .feature-particles,
  .logo-zone,
  .core-ring,
  .terminal-nav,
  .addon-layer,
  .belt-firewall,
  .belt-auth,
  .belt-tls,
  .orbit-firewall,
  .orbit-auth,
  .orbit-tls,
  .orbit-icon,
  .bump {
    animation: none !important;
  }

  .addon-layer {
    opacity: 1;
  }

  .addon-auth { width: var(--ring-auth); height: var(--ring-auth); }
  .addon-firewall { width: var(--ring-firewall); height: var(--ring-firewall); }
  .addon-tls { width: var(--ring-tls); height: var(--ring-tls); }

  .traffic-layer,
  .feature-particles,
  .terminal-nav {
    display: none;
  }

  .orbit-icon::before {
    display: none;
  }
}
</style>
