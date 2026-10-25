// 一个可中断的呈现时钟拥有姿态、眼形、形变和自然眨眼；暂停后不重放事件。
(() => {
  "use strict";
  const root = document.documentElement;
  const pose = document.getElementById("pose"), gaze = document.getElementById("gaze");
  const lids = document.getElementById("lids"), body = document.getElementById("body");
  const character = document.getElementById("character");
  const bodyClip = document.getElementById("body-clip"), stage = document.getElementById("stage"), breath = document.getElementById("breath");
  const eyeNodes = [document.getElementById("eye-left"), document.getElementById("eye-right")];
  const motion = window.PetMotion, effects = window.PetEffects, geometry = window.PetGeometry;
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
  const clamp = (n, a, b) => Math.max(a, Math.min(b, n));
  const axis = (x = 0) => ({ x, v: 0 });
  const values = { eyeX: axis(), eyeY: axis(), x: axis(), y: axis(), turn: axis(), yaw: axis(),
    sx: axis(1), sy: axis(1), lift: axis(), squash: axis(), morph: axis(), effect: axis(), lid: axis(1), fit: axis(1) };
  const eyes = [0, 1].map(() => [axis(6), axis(13), axis(), axis()]);
  const winks = [axis(1), axis(1)];
  let state = null, active = false, frame = 0, last = 0, natural = 0, nextFrameAt = 0;
  let age = 0, autoIndex = 2, autoAge = 0, selected = "idle";
  let blinkAt = -Infinity, blinkFrom = 1, opening = 1, pressUntil = 0;
  let seenBlink = 0, seenPress = 0;
  let wasLooping = false;
  const turn = { raw: 0, offset: 0 };
  const frameInterval = 1000 / 60;
  function spring(value, target, dt, stiffness = 100, damping = 10) {
    const steps = Math.max(1, Math.ceil(dt * 120)), step = dt / steps;
    for (let n = 0; n < steps; n++) {
      value.v += ((target - value.x) * stiffness - value.v * damping) * step;
      value.x += value.v * step;
    }
    if (Math.abs(value.x - target) < 0.005 && Math.abs(value.v) < 0.01) { value.x = target; value.v = 0; }
    return value.v !== 0 || value.x !== target;
  }
  const fullMotion = () => state?.motion === 0 && !reduced.matches;
  const automatic = () => !state?.animation || state.animation === "auto";
  const continuous = () => fullMotion() && ((!automatic() && state.animation !== "idle") || state.idleAnimationsEnabled);
  function scheduleNatural() {
    clearTimeout(natural); natural = 0;
    const quiet = ["sleeping", "waking", "drowsy", "thinking", "orbit", "radar", "progress", "spawning", "loading", "dictating", "writing", "sending", "receiving", "uploading", "alerting", "bouncing", "powering-down"].includes(selected);
    if (active && fullMotion() && state.idleAnimationsEnabled && !quiet) {
      const delay = selected === "idle" ? 6500 + Math.random() * 5500 : selected === "excited" ? 2100 + Math.random() * 2400 : 3500 + Math.random() * 3500;
      natural = setTimeout(() => { blink(); scheduleNatural(); }, delay);
    }
  }
  function wake() {
    if (active && !frame) { last = performance.now(); nextFrameAt = last; frame = requestAnimationFrame(tick); }
  }
  function blink() { blinkFrom = opening; blinkAt = performance.now(); wake(); }
  function resolveAnimation() {
    if (automatic()) return state.idleAnimationsEnabled ? motion.automaticStates[autoIndex] : "idle";
    return motion.states.includes(state.animation) || motion.tricks.includes(state.animation) ? state.animation : "idle";
  }
  function tick(now) {
    frame = 0;
    if (!active || !state) return;
    if (now + 0.5 < nextFrameAt) { frame = requestAnimationFrame(tick); return; }
    nextFrameAt = Math.max(nextFrameAt + frameInterval, now + frameInterval / 2);
    const dt = clamp((now - last) / 1000, 0, 0.1); last = now;
    const full = fullMotion(), looping = continuous();
    if (looping) {
      age += dt; autoAge += dt;
      const duration = selected === "sleeping" ? 6 : selected === "waking" ? 4 : 5;
      if (automatic() && autoAge >= duration) {
        autoIndex = (autoIndex + 1) % motion.automaticStates.length; autoAge = age = 0;
      }
    }
    const next = resolveAnimation();
    const changed = selected !== next;
    if (changed) { selected = next; age = 0; scheduleNatural(); }
    const p = motion.target(selected, looping ? age : 0);
    const animatedPose = full && (looping || state.hovered);
    const targets = {
      eyeX: state.gazeX + (looping ? p.gazeX * 0.2 : 0),
      eyeY: state.gazeY + (looping ? p.gazeY * 0.2 : 0) - (full && state.hovered ? 0.5 : 0),
      x: animatedPose ? p.x : 0, y: animatedPose ? p.y : 0,
      turn: animatedPose ? p.turn + (state.hovered ? state.gazeX * 0.4 : 0) : 0,
      yaw: looping ? p.spinY : 0, sx: animatedPose ? p.sx : 1, sy: animatedPose ? p.sy : 1,
      lift: full && state.hovered ? 0.6 : 0, squash: full && now < pressUntil ? 1 : 0,
      morph: p.morph, effect: p.effect ? (["humming", "notifying"].includes(p.effect) ? 1 : p.morph) : 0, lid: p.lid
    };
    targets.yaw = looping ? motion.unwrapTurn(turn, targets.yaw, values.yaw.x, changed || !wasLooping)
      : Math.round(values.yaw.x / 360) * 360;
    wasLooping = looping;
    let moving = false;
    for (const key of Object.keys(values)) {
      if (key === "fit") continue;
      const tune = key === "eyeX" || key === "eyeY" ? [220, 28] : key === "squash" ? [320, 22] :
        key === "yaw" || key === "morph" || key === "effect" ? [150, 22] : [36, 11];
      if (full) moving = spring(values[key], targets[key], dt, ...tune) || moving;
      else { values[key].x = targets[key]; values[key].v = 0; }
    }
    for (let n = 0; n < 2; n++) {
      const shape = n === 0 ? p.left : p.right;
      for (let k = 0; k < 4; k++) {
        if (full) moving = spring(eyes[n][k], shape[k], dt, 180, 26) || moving;
        else { eyes[n][k].x = shape[k]; eyes[n][k].v = 0; }
      }
      const [width, height, bend, angle] = eyes[n].map(value => value.x);
      eyeNodes[n].setAttribute("d", motion.eyePath(Math.max(1, width), Math.max(1, height), bend));
      const projected = geometry.eye(n === 0 ? 24 : 40, 32, values.yaw.x, full && state.hovered ? -2 : 0);
      const winkTarget = looping && p.wink === n ? p.winkLid : 1;
      if (full) moving = spring(winks[n], winkTarget, dt, 400, 32) || moving;
      else { winks[n].x = 1; winks[n].v = 0; }
      const wink = winks[n].x;
      eyeNodes[n].setAttribute("transform", `translate(${projected.x} ${projected.y}) rotate(${n === 0 ? angle : -angle}) scale(${projected.width} ${wink})`);
      eyeNodes[n].setAttribute("opacity", projected.opacity);
    }
    const elapsed = now - blinkAt;
    if (state.motion === 2) opening = elapsed < 100 ? 0.08 : 1;
    else if (elapsed < 60) opening = blinkFrom + (0.08 - blinkFrom) * clamp(elapsed / 60, 0, 1);
    else if (elapsed < 95) opening = 0.08;
    else if (elapsed < 200) opening = 0.08 + 0.92 * (1 - Math.pow(1 - (elapsed - 95) / 105, 2));
    else opening = 1;
    const v = key => values[key].x;
    gaze.setAttribute("transform", `translate(${clamp(v("eyeX"), -4.5, 4.5)} ${clamp(v("eyeY"), -3.5, 3.5)})`);
    gaze.setAttribute("opacity", 1 - clamp(v("morph") * 1.8, 0, 1));
    lids.setAttribute("transform", `translate(0 32) scale(1 ${opening * v("lid")}) translate(0 -32)`);
    const amount = clamp(v("squash"), -0.2, 1.15);
    pose.setAttribute("transform", `translate(${32 + v("x")} ${34 + v("y") - v("lift")}) rotate(${v("turn")}) scale(${v("sx")} ${v("sy")}) translate(-32 -34)`);
    breath.setAttribute("transform", `translate(32 59) scale(${1 + amount * 0.025} ${1 - amount * 0.065}) translate(-32 -59)`);
    character.setAttribute("opacity", 1);
    const shape = geometry.update(clamp(v("morph"), 0, 1), p.core, v("yaw"), dt, full);
    if (body.getAttribute("d") !== shape.d) { body.setAttribute("d", shape.d); bodyClip.setAttribute("d", shape.d); }
    moving ||= shape.moving;
    const fit = Math.min(1, 32 / (shape.radius * Math.max(v("sx"), v("sy")) + Math.max(Math.abs(v("x")), Math.abs(v("y") - v("lift"))) + 3));
    if (full) moving = spring(values.fit, fit, dt, 180, 26) || moving;
    else values.fit.x = fit;
    stage.setAttribute("transform", `translate(32 32) scale(${v("fit")}) translate(-32 -32)`);
    effects.render({ kind: p.effect, mix: clamp(v("effect"), 0, 1), time: p.effectTime,
      burst: looping ? p.particles : 0, fill: state.bodyColor, eyeFill: state.eyeColor, highContrast: state.highContrast,
      dt, animate: full && (looping || moving), pose: { x: v("x"), y: v("y") - v("lift"), turn: v("turn"),
        sx: v("sx") * (1 + amount * 0.025), sy: v("sy") * (1 - amount * 0.065), offsetY: 25 * amount * 0.065 * v("sy") }, penUp: p.penUp,
      ribbons: looping ? p.ribbons : 0, yaw: v("yaw"), boundary: geometry.boundary, pencil: geometry.pencil });
    root.setAttribute("data-animation", selected);
    if (looping || moving || elapsed < 200 || now < pressUntil) frame = requestAnimationFrame(tick);
  }
  function syncActive() {
    const next = !!state?.active && !document.hidden;
    root.classList.toggle("active", next);
    root.classList.toggle("idle", !!state?.idleAnimationsEnabled);
    root.classList.toggle("full", fullMotion());
    if (next === active) return;
    active = next;
    cancelAnimationFrame(frame); frame = 0;
    clearTimeout(natural); natural = 0;
    // 短暂停冻结所有逻辑年龄与旋转累计，只清除输入事件和帧基准。
    blinkAt = -Infinity; pressUntil = 0; opening = 1;
    lids.setAttribute("transform", "");
    for (const value of [...Object.values(values), ...eyes.flat(), ...winks]) value.v = 0;
    if (active) { wake(); scheduleNatural(); }
  }
  function receive(message) {
    if (message?.type !== "state" || !message.state) return;
    const previous = state; state = message.state;
    body.setAttribute("fill", state.bodyColor);
    body.setAttribute("stroke", state.highContrast ? state.eyeColor : "none");
    body.setAttribute("stroke-width", "1.5");
    for (const eye of eyeNodes) eye.setAttribute("fill", state.eyeColor);
    if (previous?.animation !== state.animation) { age = autoAge = 0; autoIndex = 2; }
    syncActive();
    if (message.initial || !active) {
      seenBlink = state.blinkSequence; seenPress = state.pressSequence;
      blinkAt = -Infinity; pressUntil = 0; opening = 1;
      lids.setAttribute("transform", "");
    } else {
      if (state.blinkSequence !== seenBlink) blink();
      if (state.pressSequence !== seenPress && fullMotion()) pressUntil = performance.now() + 120;
      seenBlink = state.blinkSequence; seenPress = state.pressSequence;
    }
    if (previous?.idleAnimationsEnabled !== state.idleAnimationsEnabled || previous?.motion !== state.motion) scheduleNatural();
    if (active) wake();
  }
  document.addEventListener("visibilitychange", syncActive);
  reduced.addEventListener("change", () => { syncActive(); scheduleNatural(); wake(); });
  window.chrome.webview.addEventListener("message", event => {
    receive(event.data);
    window.chrome.webview.postMessage({ type: "ack", id: event.data.id });
  });
  window.chrome.webview.postMessage({ type: "ready" });
})();
