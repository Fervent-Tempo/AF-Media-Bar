// 自绘角色的动作目标；只产生几何参数，不拥有 DOM、时钟或输入订阅。
(() => {
  "use strict";
  const states = Object.freeze([
    "sleeping", "waking", "idle", "listening", "thinking", "searching", "working",
    "excited", "surprised", "suspicious", "angry", "drowsy", "happy", "curious", "confused", "bored", "proud",
    "shy", "sad", "laughing", "scared", "playful", "celebrate", "orbit", "radar", "progress", "spawning",
    "humming", "loading", "dictating", "writing", "sending", "receiving", "uploading", "notifying", "alerting",
    "dragging", "bouncing", "powering-down"
  ]);
  const tricks = Object.freeze(["emphasis", "hop", "spin", "spin-dizzy", "spin-wild", "particles"]);
  const clamp = (n, low, high) => Math.max(low, Math.min(high, n));
  const pulse = (t, period) => Math.max(0, Math.sin(t * Math.PI * 2 / period));
  const ease = n => 1 - Math.pow(1 - clamp(n, 0, 1), 3);
  // 左右眼可独立改变宽、高、弯曲和倾斜；所有眼形均由这些参数生成。
  const faces = {
    neutral: [6, 13, 0, 0], round: [8, 15, 0, 0], wide: [8.5, 17, 0, 0],
    closed: [8, 1.2, 2.3, 0], sleepy: [7, 4, 1.3, 0], half: [7, 7, 0, 0],
    smile: [8, 3, -4, 0], joy: [9, 3.5, -5, 0], stern: [7.5, 6, 0, -18],
    curious: [6.5, 15, 0, -7], focused: [5.5, 10, 0, -5], low: [6.5, 8, 1.4, 0],
    shy: [5, 10, 1, 9], tiny: [4, 5, 0, 0], bright: [7, 14, -0.5, 0],
    proud: [7, 10, -1.8, -6], tired: [7.5, 3, 2, 5], worried: [6.5, 10, 1.5, 18],
    slant: [6, 13, 0, 14], upward: [6, 12, -2, -10], downward: [7, 7, 2, 10],
    squint: [8, 2, 0, 0], peek: [5, 7, 0, -12], soft: [6, 11, -1, 0], astonished: [9, 16, 0, -4]
  };
  function target(id, t) {
    const p = { x: 0, y: 0, turn: 0, sx: 1, sy: 1, lid: 1, face: "neutral", rightFace: null,
      gazeX: 0, gazeY: 0, effect: "", morph: 0, particles: 0, spinY: 0, faceOpacity: 1 };
    const sway = Math.sin(t * 1.7), slow = Math.sin(t * 0.8);
    switch (id) {
      case "sleeping":
        p.face = "closed"; p.y = 3 + slow; p.turn = 7 + slow * 2; p.sy = 0.96 + slow * 0.012; break;
      case "waking": {
        const phase = t % 4;
        p.face = phase < 0.6 ? "closed" : phase < 1.6 ? "wide" : "soft";
        p.y = phase < 0.6 ? 3 : -5 * Math.sin(clamp((phase - 0.6) / 1.2, 0, 1) * Math.PI);
        p.sy = 1 + 0.06 * Math.sin(clamp((phase - 0.6) / 1.5, 0, 1) * Math.PI);
        p.particles = phase > 0.6 && phase < 1.6 ? (phase - 0.6) : 0; break;
      }
      case "idle": p.y = slow * 0.45; p.turn = Math.sin(t * 0.5) * 1.3; p.sy = 1 + slow * 0.008; break;
      case "listening": p.turn = 9; p.y = -1 + pulse(t, 2.7) * 1.8; p.face = "soft"; break;
      case "thinking": p.turn = -10 + slow * 4; p.gazeX = 1.6; p.gazeY = -1.5; p.effect = "dots"; p.morph = 1; break;
      case "searching": p.x = sway * 2; p.turn = sway * 12; p.gazeX = sway * 2; p.face = "focused"; break;
      case "working": p.turn = 4 + sway * 2; p.y = pulse(t, 0.65) * 1.5; p.face = "focused"; p.gazeY = 1; break;
      case "excited": p.y = -pulse(t, 0.7) * 5; p.turn = sway * 8; p.face = "bright"; p.sy = 1 + sway * 0.025; break;
      case "surprised": p.face = "astonished"; p.y = -3 * Math.exp(-(t % 3) * 3); p.sx = 0.95; p.sy = 1.06; break;
      case "suspicious": p.face = "half"; p.rightFace = "peek"; p.turn = -7; p.gazeX = -2.5; break;
      case "angry": p.face = "stern"; p.turn = Math.sin(t * 35) * pulse(t, 2.3) * 3; p.sy = 0.96; break;
      case "drowsy": {
        const nod = pulse(t, 5);
        p.face = nod > 0.7 ? "closed" : "tired"; p.y = 2 + nod * 5; p.turn = nod * 10; p.sy = 1 - nod * 0.05; break;
      }
      case "happy": p.face = "smile"; p.y = -pulse(t, 1.9) * 2; p.turn = slow * 4; break;
      case "curious": p.face = "curious"; p.rightFace = "round"; p.turn = 12 + slow * 6; p.x = sway; break;
      case "confused": p.face = "half"; p.rightFace = "wide"; p.turn = sway * 13; p.gazeY = -0.8; break;
      case "bored": p.face = "low"; p.y = 2; p.turn = slow * 4 - 4; p.gazeX = -2; break;
      case "proud": p.face = "proud"; p.y = -2; p.sx = p.sy = 1.025; p.turn = slow * 2; break;
      case "shy": p.face = "shy"; p.turn = -11; p.y = 1.5; p.gazeX = -1.5; p.sx = 0.96; break;
      case "sad": p.face = "worried"; p.y = 3.5; p.sy = 0.95; p.gazeY = 1.6; break;
      case "laughing": p.face = "joy"; p.y = -Math.abs(Math.sin(t * 10)) * 2.7; p.turn = Math.sin(t * 10) * 4; break;
      case "scared": p.face = "wide"; p.x = Math.sin(t * 31) * 0.7; p.turn = Math.sin(t * 27) * 2; p.sx = 0.94; break;
      case "playful": p.face = "joy"; p.rightFace = t % 4 < 0.5 ? "closed" : "bright"; p.turn = sway * 12; p.y = -pulse(t, 1.4) * 2.5; break;
      case "celebrate": p.face = "joy"; p.spinY = ease((t % 5) / 1.6) * 360; p.y = -pulse(t, 0.9) * 3; p.particles = (t % 2) / 2; break;
      case "orbit": case "radar": case "progress": case "loading": case "dictating": case "writing":
      case "sending": case "receiving": case "uploading": case "bouncing": case "powering-down":
        p.effect = id; p.morph = 1; break;
      case "spawning": {
        const phase = (t % 4) / 4;
        p.effect = "gather"; p.morph = phase < 0.55 ? 1 : 1 - ease((phase - 0.55) / 0.25);
        p.face = "round"; p.sx = p.sy = 0.9 + ease(phase / 0.7) * 0.1; break;
      }
      case "humming": p.effect = "notes"; p.turn = sway * 4; p.face = "soft"; p.y = slow; break;
      case "notifying": p.effect = "notify"; p.face = "bright"; p.y = -pulse(t, 3) * 3; break;
      case "alerting": p.effect = "alerting"; p.morph = 1; break;
      case "dragging": {
        const phase = (t % 4) / 4;
        p.x = Math.sin(phase * Math.PI * 2) * 5; p.y = -Math.sin(phase * Math.PI) * 5;
        p.turn = -sway * 8; p.face = "upward"; p.sy = 1.04; break;
      }
      case "emphasis": { const amount = pulse(t, 2); p.sx = 1 + amount * 0.1; p.sy = 1 - amount * 0.06; p.face = "bright"; break; }
      case "hop": {
        const phase = t % 4, segment = Math.floor(phase / 0.45), local = (phase % 0.45) / 0.45;
        p.y = segment < 4 ? -Math.sin(local * Math.PI) * 7 * Math.pow(0.5, segment) : 0;
        p.sy = 1 - pulse(t + 0.2, 4) * 0.03; p.face = "bright"; break;
      }
      case "spin": p.spinY = ease((t % 4) / 1.1) * 360; break;
      case "spin-dizzy": case "spin-wild": {
        const phase = t % 6, wild = id === "spin-wild", duration = wild ? 2.5 : 1.5;
        p.spinY = ease(phase / duration) * (wild ? 2160 : 1080);
        const wobble = phase > duration ? Math.exp(-(phase - duration) * 1.6) : 0;
        p.turn = Math.sin(phase * 17) * wobble * 17; p.x = Math.cos(phase * 17) * wobble * 2;
        p.face = phase > duration ? "slant" : "wide"; p.rightFace = phase > duration ? "downward" : null; break;
      }
      case "particles": p.particles = (t % 2) / 2; p.face = "smile"; break;
      default: break;
    }
    const left = faces[p.face] ?? faces.neutral, right = faces[p.rightFace] ?? left;
    return { ...p, left, right };
  }
  // 曲线眼形由二次曲线与圆端生成，不使用参考库的路径或采样点。
  function eyePath(width, height, bend) {
    const x = width / 2, y = height / 2, cap = Math.min(x, y);
    return `M${-x} ${-y + cap} Q${-x} ${-y} ${-x + cap} ${-y} Q0 ${-y + bend} ${x - cap} ${-y} Q${x} ${-y} ${x} ${-y + cap} L${x} ${y - cap} Q${x} ${y} ${x - cap} ${y} Q0 ${y + bend} ${-x + cap} ${y} Q${-x} ${y} ${-x} ${y - cap} Z`;
  }
  // 展开循环角度时根据目标的换周递增，不能按落后的弹簧位置选择最近一圈。
  function unwrapTurn(tracker, raw, current, reset) {
    if (reset) tracker.offset = Math.round(current / 360) * 360;
    else if (raw < tracker.raw - 180) tracker.offset += Math.round(tracker.raw / 360) * 360;
    tracker.raw = raw;
    return raw + tracker.offset;
  }
  window.PetMotion = Object.freeze({ states, tricks, automaticStates: Object.freeze(states.slice(0, 23)), target, eyePath, unwrapTurn });
})();
