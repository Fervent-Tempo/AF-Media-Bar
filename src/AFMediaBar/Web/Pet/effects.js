// 固定图形池由同一个呈现时钟推进；切换时保留各层当前权重，墨迹和粒子都有容量上限。
(() => {
  "use strict";
  const root = document.getElementById("effects"), particles = document.getElementById("particles");
  const ids = ["dots", "orbit", "radar", "progress", "spawning", "humming", "loading", "dictating", "writing",
    "sending", "receiving", "uploading", "notifying", "alerting", "bouncing", "powering-down"];
  const layers = ids.map(id => ({ id, element: document.getElementById("fx-" + id), weight: 0, age: 0, ink: [], pose: null }));
  const burstOrigins = Array.from({ length: 16 }, () => ({ x: 0, y: 0, dx: 0, dy: 0 }));
  const cache = new Map(), get = id => { if (!cache.has(id)) cache.set(id, document.getElementById(id)); return cache.get(id); };
  const clamp = (n, a = 0, b = 1) => Math.max(a, Math.min(b, n));
  const circle = (id, x, y, radius, opacity = 1) => {
    const node = get(id); node.setAttribute("cx", x); node.setAttribute("cy", y); node.setAttribute("r", Math.max(0, radius)); node.setAttribute("opacity", clamp(opacity));
  };
  let ribbons = 0, ribbonAge = 0, previousBurst = 0;
  function paint(layer, options) {
    const t = layer.age, phase = t % 1.6 / 1.6;
    switch (layer.id) {
      case "dots":
        for (let n = 0; n < 2; n++) {
          const pulse = (1 + Math.sin(t * 4.2 - n * 2.2)) / 2;
          circle(n ? "think-right" : "think-left", n ? 17 : -17, -pulse * 2.3, 4.4 + pulse * 0.8, 0.5 + pulse * 0.5);
        } break;
      case "orbit":
        for (let n = 0; n < 5; n++) {
          const angle = t * 1.65 + n * Math.PI * 2 / 5, depth = Math.cos(angle);
          circle("orbit-" + n, Math.sin(angle) * 17, -depth * 7, 2.4 + Math.max(0, depth) * 1.2, 0.25 + (depth + 1) * 0.375);
        } break;
      case "radar":
        for (let n = 0; n < 3; n++) { const p = (t / 1.35 + n / 3) % 1; circle("radar-" + n, 0, 0, 5.5 + p * 23, (1 - p) * 0.8); } break;
      case "progress": get("progress-arc").setAttribute("stroke-dasharray", `${clamp(t / 2.15) * 100} 100`); break;
      case "spawning":
        for (let n = 0; n < 5; n++) {
          const p = clamp((t - n * 0.13) / 1.3), remaining = (1 - p) ** 3, angle = n * 2.35 + p * 2.5;
          circle("gather-" + n, Math.cos(angle) * remaining * 26, Math.sin(angle) * remaining * 22,
            (1.7 + p * 1.3) * (1 - clamp((p - 0.85) / 0.15)), clamp(p * 5));
        } break;
      case "dictating":
        for (let n = 0; n < 4; n++) {
          const offset = [-2, -1, 1, 2][n], energy = (Math.sin(t * 9 - Math.abs(offset)) + 1) / 2;
          circle("wave-" + n, offset * 11, -energy * 1.5, 2 + energy * 2.2);
        } break;
      case "sending": {
        const flight = clamp((phase - 0.17) / 0.57), distance = flight ** 2 * 30;
        circle("send-main", distance * 0.8, -distance * 0.65, 2.8 - flight * 1.2, flight > 0 && flight < 1 ? 1 - flight ** 3 : 0);
        const tail = clamp((phase - 0.25) / 0.57), tailDistance = tail ** 2 * 30;
        circle("send-tail", tailDistance * 0.8, -tailDistance * 0.65, 1.5 - tail * 0.7, tail > 0 && tail < 1 ? 0.4 * (1 - tail) : 0);
        const ring = clamp((phase - 0.17) / 0.3); circle("send-ring", 0, 0, 5 + ring * 9, ring > 0 ? 1 - ring : 0); break;
      }
      case "receiving": {
        const p = clamp(phase / 0.6), distance = 29 * (1 - p) ** 3;
        const angle = -2.6 + Math.sin(Math.floor(t / 1.6) * 2.1) * 1.7;
        const bend = Math.sin(p * Math.PI) * 4;
        circle("receive-main", Math.cos(angle) * distance - Math.sin(angle) * bend,
          Math.sin(angle) * distance + Math.cos(angle) * bend, 1.2 + p * 1.7, p < 1 ? clamp(p * 5) : 0);
        const ring = clamp((phase - 0.58) / 0.32); circle("receive-ring", 0, 0, 5 + ring * 8, ring > 0 ? 1 - ring : 0); break;
      }
      case "uploading":
        for (let n = 0; n < 2; n++) {
          const entrance = clamp((t - 0.2 - n * 1.2) / 0.9), ease = 1 - (1 - entrance) ** 3, angle = t * 1.15 + n * Math.PI;
          circle("upload-" + n, -27 + (Math.sin(angle) * 12 + 27) * ease,
            26 + (Math.cos(angle) * 6 - 26) * ease, 2.2 + ease * 0.8, clamp(entrance * 5));
        } break;
      case "writing": {
        if (options.kind === "writing") layer.pose = options.pose;
        const pose = layer.pose ?? options.pose, angle = pose.turn * Math.PI / 180;
        const anchor = options.pencil.tipAnchorY * pose.sy + pose.offsetY;
        const end = options.pencil.tipEndY * pose.sy + pose.offsetY;
        const x = pose.x - Math.sin(angle) * end, y = pose.y + Math.cos(angle) * end;
        get("pencil-tip").setAttribute("transform", `translate(${pose.x - Math.sin(angle) * anchor} ${pose.y + Math.cos(angle) * anchor}) rotate(${pose.turn}) scale(${pose.sx} ${pose.sy})`);
        if (options.kind === "writing" && options.animate && !options.penUp && layer.weight > 0.6) {
          const last = layer.ink[layer.ink.length - 1];
          if (!last || Math.hypot(last[0] - x, last[1] - y) > 0.6) layer.ink.push([x, y]);
          if (layer.ink.length > 48) layer.ink.shift();
        } else if (options.kind === "writing" && options.penUp && layer.ink.length) layer.ink.splice(0, 2);
        get("writing-ink").setAttribute("d", layer.ink.length > 1 ? "M" + layer.ink.map(point => point.map(n => n.toFixed(2)).join(" ")).join("L") : "");
        break;
      }
      case "notifying": get("notify-badge").setAttribute("fill", options.highContrast ? options.eyeFill : "#369FE9"); break;
      case "powering-down": {
        const pulse = (Math.sin(t * 1.5) + 1) / 2;
        circle("power-halo", 0, 0, 7 + pulse * 2, 0.07 + pulse * 0.1);
        circle("power-ring", 0, 0, 26 - clamp(t / 1.5) * 22, (1 - clamp(t / 1.5)) * 0.5); break;
      }
      default: break;
    }
  }
  function paintRibbons(options) {
    const target = options.animate ? options.ribbons : 0;
    ribbons += (target - ribbons) * (options.animate ? 1 - Math.exp(-10 * options.dt) : 1);
    if (options.animate) ribbonAge += options.dt;
    for (let band = 0; band < 3; band++) {
      const front = get("ribbon-front-" + band), back = get("ribbon-back-" + band);
      front.setAttribute("opacity", ribbons); back.setAttribute("opacity", ribbons * 0.5);
      if (ribbons < 0.002) continue;
      let frontPath = "", backPath = "";
      const plane = (band - 1) * 0.65, head = options.yaw * Math.PI / 180 + ribbonAge * 1.3 + band * 2;
      let previous = null;
      for (let n = 0; n <= 36; n++) {
        const phase = n / 36, angle = head - 2.5 + phase * 2.5;
        const x = Math.cos(angle) * 28, y = Math.sin(angle) * 10;
        const point = { x: 32 + x * Math.cos(plane) - y * Math.sin(plane), y: 34 + x * Math.sin(plane) + y * Math.cos(plane), z: Math.sin(angle), width: 0.15 + phase * 1.1 };
        if (previous) {
          const dx = point.x - previous.x, dy = point.y - previous.y, length = Math.hypot(dx, dy) || 1;
          const ox = -dy / length, oy = dx / length;
          const polygon = `M${(previous.x + ox * previous.width).toFixed(2)} ${(previous.y + oy * previous.width).toFixed(2)}L${(point.x + ox * point.width).toFixed(2)} ${(point.y + oy * point.width).toFixed(2)}L${(point.x - ox * point.width).toFixed(2)} ${(point.y - oy * point.width).toFixed(2)}L${(previous.x - ox * previous.width).toFixed(2)} ${(previous.y - oy * previous.width).toFixed(2)}Z`;
          if (point.z >= 0) frontPath += polygon; else backPath += polygon;
        }
        previous = point;
      }
      for (const node of [front, back]) node.setAttribute("fill", options.highContrast ? options.eyeFill : `url(#ribbon-ink-${band})`);
      front.setAttribute("d", frontPath); back.setAttribute("d", backPath);
    }
  }
  function render(options) {
    root.setAttribute("fill", options.fill); root.setAttribute("stroke", options.fill);
    const rate = options.animate ? 1 - Math.exp(-18 * options.dt) : 1;
    for (const layer of layers) {
      const target = options.kind === layer.id ? options.mix : 0;
      layer.weight += (target - layer.weight) * rate;
      if (layer.weight < 0.002 && target === 0) {
        layer.element.style.display = "none"; layer.weight = layer.age = 0; layer.ink.length = 0;
        continue;
      }
      layer.element.style.display = "inline"; layer.element.setAttribute("opacity", layer.weight);
      if (options.kind === layer.id) layer.age = options.time;
      else if (options.animate) layer.age += options.dt;
      paint(layer, options);
    }
    paintRibbons(options);
    const burst = options.animate ? options.burst : 0, amount = burst > 0 ? Math.sin(clamp(burst) * Math.PI) : 0;
    particles.setAttribute("opacity", amount);
    if (burst > 0 && (previousBurst <= 0 || burst < previousBurst)) {
      const turn = options.pose.turn * Math.PI / 180;
      for (let n = 0; n < 16; n++) {
        const angle = n * 2.399, edge = options.boundary(angle), radius = Math.max(18, Math.hypot(...edge) * 0.87);
        const dx = Math.cos(angle + turn), dy = Math.sin(angle + turn);
        Object.assign(burstOrigins[n], { x: 32 + options.pose.x + dx * radius, y: 34 + options.pose.y + dy * radius, dx, dy });
      }
    }
    previousBurst = burst;
    if (amount > 0) for (let n = 0; n < 16; n++) {
      const piece = get("particle-" + n), origin = burstOrigins[n], distance = burst * (6 + n % 4 * 2);
      piece.setAttribute("fill", options.highContrast ? options.eyeFill : ["#EDBE68", "#6DC8AA", "#7BA9E6", "#D484BC"][n % 4]);
      piece.setAttribute("transform", `translate(${origin.x + origin.dx * distance} ${origin.y + origin.dy * distance + burst * burst * 4}) rotate(${n * 31 + burst * 210})`);
    }
  }
  window.PetEffects = Object.freeze({ render });
})();
