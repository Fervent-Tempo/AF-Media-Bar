// 独立编排状态内的动作阶段与眼形轮换；只返回目标，不创建时钟或排队输入。
(() => {
  "use strict";
  const states = Object.freeze([
    "sleeping", "waking", "idle", "listening", "thinking", "searching", "working",
    "excited", "surprised", "suspicious", "angry", "drowsy", "happy", "curious", "confused", "bored", "proud",
    "shy", "sad", "laughing", "scared", "playful", "celebrate", "orbit", "radar", "progress", "spawning",
    "humming", "loading", "dictating", "writing", "sending", "receiving", "uploading", "notifying", "alerting",
    "dragging", "bouncing", "powering-down", "music-quiet", "music-playing", "music-paused"
  ]);
  const tricks = Object.freeze(["emphasis", "hop", "spin", "spin-dizzy", "spin-wild", "particles"]);
  const clamp = (n, low = 0, high = 1) => Math.max(low, Math.min(high, n));
  const smooth = n => { n = clamp(n); return n * n * (3 - 2 * n); };
  const wave = (t, period) => Math.sin(t * Math.PI * 2 / period);
  const bump = (phase, start, length) => Math.sin(clamp((phase - start) / length) * Math.PI);
  const profiles = {
    neutral: [6, 13, 0, 0], round: [8, 15, 0, 0], wide: [8.5, 16.5, 0, 0],
    closed: [7, 1.1, 1.9, 0], sleepy: [7, 4, 1.1, 0], half: [6.5, 7, 0, 0],
    smile: [7, 2.5, -3.7, 0], joy: [8, 3, -4.5, 0], stern: [5, 8.5, 0, -24],
    curious: [6, 14, 0, -6], focused: [5.5, 10, 0, -6], low: [6.5, 6, 0.8, 0],
    shy: [5.5, 10, 0, 9], bright: [6.5, 14, 0, 0], proud: [6.5, 11, 0, -4],
    tired: [7, 2.2, 1.5, 4], worried: [5.5, 9, 0, 18], slant: [6, 12, 0, 15],
    upward: [5.5, 12, 0, -11], downward: [6, 7, 1.2, 12], squint: [7, 1.5, 0, 0],
    peek: [5.5, 6, 0, -12], soft: [6, 11, 0, 0], astonished: [8.5, 16, 0, -4]
  };
  const expressions = {
    idle: ["neutral", "soft"], listening: ["soft", "focused", "neutral"], thinking: ["half", "upward", "focused"],
    searching: ["curious", "wide", "focused"], working: ["focused", "stern", "neutral"], excited: ["bright", "joy", "wide"],
    surprised: ["wide", "astonished"], suspicious: ["half", "peek"], angry: ["stern", "squint"],
    happy: ["smile", "bright", "joy"], curious: ["curious", "round", "neutral"], confused: ["half", "upward", "peek"],
    bored: ["low", "tired", "neutral"], proud: ["proud", "bright", "smile"], shy: ["neutral", "shy", "closed"],
    sad: ["worried", "low", "tired"], laughing: ["joy", "smile"], scared: ["wide", "astonished"],
    playful: ["bright", "joy", "curious"], celebrate: ["joy", "bright", "wide"]
  };
  function turn(t, duration, turns) {
    const phase = clamp(t / duration);
    return turns * 360 * (phase - Math.sin(phase * Math.PI * 2) / (Math.PI * 2));
  }
  function target(id, t) {
    const p = { x: 0, y: 0, turn: 0, sx: 1, sy: 1, lid: 1, face: "neutral", rightFace: null,
      gazeX: 0, gazeY: 0, effect: "", morph: 0, core: "dot", particles: 0, ribbons: 0, spinY: 0, wink: -1, winkLid: 1, penUp: false };
    const list = expressions[id];
    if (list) p.face = list[Math.floor(t / (id === "searching" || id === "excited" ? 1.7 : 2.8)) % list.length];
    switch (id) {
      case "sleeping": p.face = "closed"; p.y = 2.2 * smooth(t / 1.8) + wave(t, 11) * 0.6; p.turn = 4 + wave(t, 18) * 2; p.sy = 0.98 + wave(t, 11) * 0.01; break;
      case "waking": {
        const stretch = bump(t, 0.45, 1.05);
        p.face = t < 0.5 ? "closed" : t < 1.3 ? "wide" : "neutral";
        p.y = t < 0.5 ? 1.5 : -stretch * 2.2; p.sy = 1 + stretch * 0.045;
        p.turn = t > 2 ? wave(t - 2, 0.65) * 4 * Math.exp(-(t - 2) * 2) : 0;
        p.particles = t > 0.55 && t < 1.8 ? (t - 0.55) / 1.25 : 0; break;
      }
      case "idle": p.y = wave(t, 7.4) * 0.35; p.x = wave(t, 20) * 0.25; p.turn = wave(t, 13) * 1.7; p.sy = 1 + wave(t, 7.4) * 0.006; break;
      case "music-quiet": p.face = "soft"; p.turn = wave(t, 5.2) * 1.5; p.x = wave(t, 5.2) * 0.35; break;
      case "music-playing": p.face = "soft"; p.turn = wave(t, 3.6) * 1.8; p.x = wave(t, 3.6) * 0.45; break;
      case "music-paused": p.face = "neutral"; break;
      case "listening": p.turn = 8 + wave(t, 12) * 1.2; p.x = 0.5; p.y = -0.5 + bump(t % 2.8, 1.8, 0.4) * 1.2; break;
      case "thinking": { const pulse = Math.sin(t * 4.2 - 1.1); p.effect = "dots"; p.morph = 1; p.sx = p.sy = 0.86 + pulse * 0.08; p.y = -(1 + pulse) * 1.15; break; }
      case "searching": p.turn = wave(t, 4.5) * 12; p.x = wave(t, 4.5) * 1.8; p.gazeX = wave(t, 2.2) * 2.5; p.gazeY = wave(t, 3.4); break;
      case "working": p.turn = 4 + wave(t, 0.7) * 2; p.x = 0.8; p.y = Math.max(0, wave(t, 0.7)) * 0.8; p.gazeY = 1.4; break;
      case "excited": { const jump = Math.sin((t % 0.48) / 0.48 * Math.PI); p.y = -jump * 2.8; p.turn = wave(t, 0.95) * 6; p.sy = 1 + jump * 0.025; break; }
      case "surprised": { const settle = Math.exp(-t * 2.5); p.y = -2 * settle; p.x = -settle; p.sy = 1 + settle * 0.07; p.turn = wave(t, 0.5) * settle * 2; break; }
      case "suspicious": p.rightFace = "peek"; p.turn = -6 + wave(t, 15) * 2; p.gazeX = -2; break;
      case "angry": p.turn = wave(t, 0.14) * bump(t % 2.5, 0.3, 0.45) * 4; p.y = 0.9; p.sy = 0.975; break;
      case "drowsy": {
        const phase = t % 6.2;
        const slump = phase < 3.3 ? smooth((phase - 1.8) / 1.5) : 1 - smooth((phase - 3.6) / 1.5);
        p.face = slump > 0.7 ? "closed" : "sleepy"; p.y = 1.2 + slump * 4.7; p.turn = 2 + slump * 9;
        p.sy = 0.99 - slump * 0.04; break;
      }
      case "happy": p.y = -Math.abs(wave(t, 2.7)) * 0.8; p.turn = wave(t, 5.2) * 3; p.sx = 1.01; break;
      case "curious": p.turn = 10 + wave(t, 8.7) * 5; p.x = bump(t % 3, 1.8, 0.5) * 1.8; p.gazeX = wave(t, 3) * 2; p.gazeY = -1; break;
      case "confused": p.turn = wave(t, 6.8) * 12; p.rightFace = "round"; p.gazeX = wave(t, 3.5) * 2; break;
      case "bored": p.turn = -3 + wave(t, 20) * 3; p.y = 1.5; p.gazeX = -2; break;
      case "proud": p.y = -1.2; p.sx = p.sy = 1.025; p.turn = wave(t, 15) * 2; break;
      case "shy": p.turn = -8 + wave(t, 13) * 2; p.x = -0.8; p.y = 1; p.sx = 0.98; p.gazeY = 1.5; break;
      case "sad": p.y = 2; p.turn = 3 + wave(t, 17); p.sy = 0.97; p.gazeY = 1.7; break;
      case "laughing": p.y = -Math.abs(wave(t, 0.32)) * 1.3; p.turn = wave(t, 0.32) * 4; p.sy = 1 + wave(t, 0.32) * 0.025; break;
      case "scared": p.x = wave(t, 0.14) * 0.4; p.turn = wave(t, 0.16) * 2; p.sx = 0.97; break;
      case "playful": p.turn = wave(t, 4.4) * 8; p.x = wave(t, 5.2); p.y = -Math.abs(wave(t, 2.7)) * 0.9; break;
      case "celebrate": {
        const phase = t % 6.4;
        p.spinY = turn(phase, 3.7, 8); p.turn = phase > 3.7 ? wave(phase - 3.7, 0.55) * Math.exp(-(phase - 3.7) * 1.8) * 14 : 0;
        p.ribbons = phase < 4.6 ? 1 : 0; p.particles = phase > 0.2 && phase < 1.9 ? (phase - 0.2) / 1.7 : 0; break;
      }
      case "orbit": case "radar": case "dictating": case "sending": case "receiving": case "uploading":
        p.effect = id; p.morph = 1; break;
      case "progress": p.effect = id; p.morph = t % 4.1 < 2.6 ? 1 : 0; break;
      case "spawning": p.effect = id; p.morph = t % 3.8 < 2.1 ? 1 : 0; break;
      case "humming": p.effect = id; p.spinY = (t < 1.2 ? turn(t, 1.2, 1) : 360 + (t - 1.2) * 100); p.ribbons = 1; p.turn = wave(t, 12) * 2; break;
      case "loading": p.effect = id; p.morph = 1; p.spinY = t * 185; p.ribbons = 1; break;
      case "writing": {
        const phase = t % 2.6, stroke = clamp(phase / 1.8);
        p.effect = id; p.morph = 1; p.core = "pencil"; p.penUp = phase > 1.8;
        p.x = phase < 1.8 ? -14 + 28 * smooth(stroke) : 14 - 28 * smooth((phase - 1.8) / 0.8);
        p.y = phase < 1.8 ? 1.5 + wave(t, 0.25) * 0.6 : 1.5 - bump(phase, 1.8, 0.8) * 5;
        p.turn = 18 + wave(t, 7) * 2; break;
      }
      case "notifying": p.effect = id; p.turn = 3; p.y = -bump(t, 0.1, 0.65) * 1.8; p.gazeX = -1.3; p.gazeY = 1; break;
      case "alerting": p.effect = id; p.morph = 1; p.core = "alert"; p.sy = 1 + Math.exp(-(t % 2.4) * 5) * 0.04; break;
      case "dragging": {
        const phase = t % 3.5;
        p.x = phase < 0.4 ? -4 : phase < 2.2 ? -4 + smooth((phase - 0.4) / 1.8) * 8 : 4;
        p.y = phase < 2.2 ? -5 : -5 * Math.exp(-(phase - 2.2) * 10) + bump(phase, 2.2, 0.5) * 1.4;
        p.turn = phase < 2.2 ? wave(t, 2.3) * 5 : 0; break;
      }
      case "bouncing": {
        const phase = (t % 0.7) / 0.7;
        p.effect = id; p.morph = 1; p.core = "ball"; p.y = 10 - 4 * phase * (1 - phase) * 16;
        const landing = phase > 0.88 ? (phase - 0.88) / 0.12 : phase < 0.1 ? 1 - phase / 0.1 : 0;
        p.sx = 1 + landing * 0.09; p.sy = 1 - landing * 0.12; break;
      }
      case "powering-down": p.effect = id; p.morph = 1; p.core = "standby"; break;
      case "emphasis": p.sx = p.sy = 1.04; p.face = "wide"; p.gazeX = p.gazeY = 0; break;
      case "hop": {
        const phase = t % 4.2, segment = Math.floor(phase / 0.4), local = (phase % 0.4) / 0.4;
        p.y = segment < 4 ? -4 * local * (1 - local) * 9 * Math.pow(0.55, segment) : 0; break;
      }
      case "spin": p.spinY = turn(t % 4, 1, 1); break;
      case "spin-dizzy": case "spin-wild": {
        const phase = t % 6, wild = id === "spin-wild", duration = wild ? 3.7 : 1.4;
        p.spinY = turn(phase, duration, wild ? 8 : 3);
        const wobble = phase > duration ? Math.exp(-(phase - duration) * 1.7) : 0;
        p.turn = wave(phase, 0.55) * wobble * 14; p.x = wave(phase, 0.7) * wobble * 2;
        p.face = phase > duration ? "slant" : "wide"; p.rightFace = phase > duration ? "downward" : null;
        p.ribbons = wild && phase < 4.6 ? 1 : 0; break;
      }
      case "particles": p.particles = (t % 3) < 1.8 ? (t % 3) / 1.8 : 0; break;
      default: break;
    }
    if (["idle", "happy", "excited", "curious", "playful"].includes(id) && t % 7 > 4.5 && t % 7 < 4.85) {
      p.wink = Math.floor(t / 7) % 2;
      const phase = (t % 7 - 4.5) / 0.35;
      p.winkLid = phase < 0.42 ? 1 - smooth(phase / 0.42) * 0.94 : 0.06 + smooth((phase - 0.42) / 0.58) * 0.94;
    }
    p.effectTime = id === "progress" ? t % 4.1 : id === "spawning" ? t % 3.8 : t;
    const left = profiles[p.face] ?? profiles.neutral, right = profiles[p.rightFace] ?? left;
    return { ...p, left, right };
  }
  function eyePath(width, height, bend) {
    const x = width / 2, y = height / 2;
    if (Math.abs(bend) > 0.5) {
      const cap = Math.min(height * 0.66, 2);
      return `M${-x} ${-y} C${-x / 2} ${bend - y} ${x / 2} ${bend - y} ${x} ${-y} C${x + cap} ${-y} ${x + cap} ${y} ${x} ${y} C${x / 2} ${bend + y} ${-x / 2} ${bend + y} ${-x} ${y} C${-x - cap} ${y} ${-x - cap} ${-y} ${-x} ${-y}Z`;
    }
    const r = Math.min(x, y);
    return `M${-x} ${-y + r} Q${-x} ${-y} ${-x + r} ${-y} L${x - r} ${-y} Q${x} ${-y} ${x} ${-y + r} L${x} ${y - r} Q${x} ${y} ${x - r} ${y} L${-x + r} ${y} Q${-x} ${y} ${-x} ${y - r}Z`;
  }
  function unwrapTurn(tracker, raw, current, reset) {
    if (reset) tracker.offset = Math.round((current - raw) / 360) * 360;
    else if (raw < tracker.raw - 180) tracker.offset += Math.round(tracker.raw / 360) * 360;
    tracker.raw = raw;
    return raw + tracker.offset;
  }
  window.PetMotion = Object.freeze({ states, tricks, automaticStates: Object.freeze(states.slice(0, 23)), target, eyePath, unwrapTurn });
})();
