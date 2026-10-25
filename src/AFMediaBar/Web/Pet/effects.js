// 形变图形和固定粒子池的呈现；没有逐帧 DOM 分配，也不创建独立时钟。
(() => {
  "use strict";
  const root = document.getElementById("effects"), particles = document.getElementById("particles");
  const kinds = ["dots", "orbit", "radar", "progress", "gather", "humming", "loading", "dictating", "writing",
    "sending", "receiving", "uploading", "notifying", "alerting", "bouncing", "powering-down"];
  const groups = new Map(kinds.map(id => [id, document.getElementById("fx-" + id)]));
  const pool = Array.from({ length: 16 }, (_, n) => document.getElementById("particle-" + n));
  const node = id => document.getElementById(id);
  let current = "";
  function render(kind, mix, t, burst, color, eyeColor, highContrast, morph) {
    kind = kind === "notes" ? "humming" : kind === "notify" ? "notifying" : kind;
    if (kind && kind !== current) {
      if (groups.has(current)) groups.get(current).style.display = "none";
      current = kind;
      if (groups.has(current)) groups.get(current).style.display = "inline";
    }
    root.setAttribute("opacity", mix);
    root.setAttribute("fill", morph ? color : eyeColor);
    root.setAttribute("stroke", morph ? color : eyeColor);
    const group = groups.get(current);
    if (group && mix > 0.001) {
      let turn = 0, scale = 1, x = 0, y = 0;
      switch (current) {
        case "dots":
          [...group.children].forEach((dot, n) => dot.setAttribute("transform", `translate(0 ${Math.sin(t * 5 - n) * 2})`)); break;
        case "orbit": turn = t * 55; node("orbit-dot").setAttribute("transform", `rotate(${t * 110})`); break;
        case "radar": node("radar-sweep").setAttribute("transform", `rotate(${t * 100})`); break;
        case "progress": node("progress-arc").setAttribute("stroke-dasharray", `${8 + (t % 4) / 4 * 92} 100`); break;
        case "gather":
          scale = 1 - ((t % 4) / 4) * 0.8; turn = t * 60; break;
        case "humming": x = 17; y = -5 + Math.sin(t * 3) * 2; scale = 0.45; break;
        case "loading": turn = t * 170; break;
        case "dictating":
          [...group.children].forEach((bar, n) => bar.setAttribute("transform", `scale(1 ${0.4 + Math.abs(Math.sin(t * 5 + n * 1.4)) * 0.6})`)); break;
        case "writing": node("pencil").setAttribute("transform", `translate(${Math.sin(t * 2) * 10} ${-Math.max(0, Math.cos(t * 2)) * 5}) rotate(${Math.sin(t * 7) * 6})`); break;
        case "sending": { const travel = (t % 2) / 2; x = travel * 13 - 5; y = 5 - travel * 13; scale = 1 - travel * 0.15; break; }
        case "receiving": node("receive-arrow").setAttribute("transform", `translate(0 ${Math.sin(t * 3) * 5})`); break;
        case "uploading": node("upload-arrow").setAttribute("transform", `translate(0 ${-Math.abs(Math.sin(t * 2.5)) * 5})`); break;
        case "notifying": node("notify-ring").setAttribute("opacity", 0.3 + Math.sin(t * 5) * 0.3); break;
        case "alerting": turn = Math.sin(t * 12) * 7; break;
        case "bouncing": node("bounce-ball").setAttribute("transform", `translate(0 ${6 - Math.abs(Math.sin(t * 3)) * 20})`); break;
        case "powering-down": scale = 0.94 + Math.sin(t * 1.2) * 0.03; break;
        default: break;
      }
      group.setAttribute("transform", `translate(${x} ${y}) rotate(${turn}) scale(${scale})`);
    }
    const amount = burst > 0 ? Math.sin(Math.min(burst, 1) * Math.PI) : 0;
    particles.setAttribute("opacity", amount);
    if (amount > 0) pool.forEach((piece, n) => {
      const angle = n * Math.PI * 2 / pool.length;
      const radius = 17 + burst * (10 + n % 3);
      piece.setAttribute("fill", highContrast ? eyeColor : [color, "#EFBF56", "#5CA5DA", "#53B69A"][n % 4]);
      piece.setAttribute("transform", `translate(${32 + Math.cos(angle) * radius} ${32 + Math.sin(angle) * radius + burst * burst * 2}) rotate(${n * 29 + burst * 180})`);
    });
  }
  window.PetEffects = Object.freeze({ render });
})();
