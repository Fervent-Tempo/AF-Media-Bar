// 原生竖排呈现。与横向引擎共用消息、字体和 WebView 生命周期，独立管理列布局与滚动。
(() => {
  const stage = document.createElement("div");
  stage.className = "vertical-stage";
  stage.hidden = true;
  document.body.appendChild(stage);
  const css = document.createElement("style");
  css.textContent = `
    @font-face { font-family:"AF Media Bar Emoji"; src:local("Segoe UI Emoji");
      unicode-range:U+200D,U+FE0F,U+2600-27BF,U+1F000-1FAFF; }
    .vertical-stage { position:fixed; inset:0; display:flex; flex-direction:row-reverse;
      justify-content:center; align-items:stretch; gap:2px; overflow:hidden; padding:2px 0;
      font-family:"AF Media Bar Emoji",var(--font-family); font-weight:var(--font-weight); text-shadow:var(--text-shadow); }
    .vertical-stage[hidden] { display:none; }
    .v-column { position:relative; flex:none; overflow:hidden; height:100%; }
    .v-track { position:absolute; top:0; left:0; width:100%; }
    .v-ink { display:block; writing-mode:vertical-rl; text-orientation:mixed; white-space:pre;
      line-height:1; letter-spacing:var(--letter-spacing,0em); width:100%; height:max-content; }
    .v-base { color:var(--primary); }
    .v-scanning .v-base { color:var(--word-scan-base); }
    .v-scan { position:absolute; top:0; left:0; color:var(--word-scan-overlay);
      clip-path:inset(0 0 100% 0); }
    .v-scan[hidden] { display:none; }
    .v-secondary .v-base { color:var(--secondary); }
    .v-translation .v-base { color:var(--translation); }
  `;
  document.head.appendChild(css);
  let enabled = false;
  let style = {};
  let frame = {};
  let identity = "";
  let changedAt = 0;
  let animationFrame = 0;
  let columns = [];
  const clamp = (x, max = 1) => Math.max(0, Math.min(max, Number(x) || 0));
  const reduced = () => style.reduceMotion || window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
  function cancel() {
    cancelAnimationFrame(animationFrame);
    animationFrame = 0;
  }
  function makeColumn(text, index, metadata) {
    const column = document.createElement("div");
    column.className = "v-column" + (index ? " v-secondary" : "") +
      (index && frame.currentTranslation ? " v-translation" : "");
    const track = document.createElement("div");
    track.className = "v-track";
    const base = document.createElement("span");
    base.className = "v-ink v-base";
    base.textContent = text;
    const scan = document.createElement("span");
    scan.className = "v-ink v-scan";
    scan.textContent = text;
    scan.setAttribute("aria-hidden", "true");
    track.append(base, scan);
    column.append(track);
    const size = index ? Number(style.secondaryFontSize) || 14 : Number(style.fontSize) || 18;
    column.style.fontSize = size + "px";
    column.style.width = Math.ceil(size) + "px";
    if (!metadata) column.style.letterSpacing = (Number(style.characterSpacingPercent) || 0) / 100 + "em";
    stage.append(column);
    return { column, track, base, scan, size };
  }
  function render(now = performance.now()) {
    animationFrame = 0;
    if (!enabled || document.hidden) return;
    const metadata = frame.scene === "metadata";
    let needsClock = false;
    for (let i = 0; i < columns.length; i++) {
      const { column, track, base, scan } = columns[i];
      const viewport = column.clientHeight;
      const length = base.scrollHeight;
      const overflow = Math.max(0, length - viewport);
      let offset = 0;
      const hasScan = !metadata && i === 0 && frame.wordScanProgress != null;
      column.classList.toggle("v-scanning", hasScan);
      scan.hidden = !hasScan;
      scan.style.clipPath = `inset(0 0 ${100 - clamp(frame.wordScanProgress) * 100}% 0)`;
      if (overflow > 0 && !reduced()) {
        if (metadata) {
          const travel = overflow / 28 * 1000;
          const period = 2000 + travel + 800 + travel;
          const elapsed = (now - changedAt) % period;
          offset = elapsed < 2000 ? 0 : elapsed < 2000 + travel
            ? (elapsed - 2000) / travel * overflow : elapsed < 2800 + travel
              ? overflow : overflow * (1 - (elapsed - 2800 - travel) / travel);
          needsClock = true;
        } else {
          const progress = clamp(hasScan ? frame.wordScanProgress : frame.progress);
          offset = clamp(progress * length - viewport * 0.65, overflow);
        }
      }
      const alignment = metadata ? style.metadataAlignment : style.textAlignment;
      const start = overflow > 0 ? 0 : alignment === "Right" ? viewport - length
        : alignment === "Center" ? (viewport - length) / 2 : 0;
      track.style.transform = `translateY(${Math.round(start - offset)}px)`;
    }
    if (needsClock) animationFrame = requestAnimationFrame(render);
  }
  function present(payload) {
    frame = payload || {};
    const nextIdentity = [frame.scene, frame.trackId, frame.currentLineIndex,
      frame.current, frame.currentTranslation || frame.next].join("\n");
    if (nextIdentity !== identity) {
      const previousIdentity = identity;
      identity = nextIdentity;
      changedAt = performance.now();
      stage.replaceChildren();
      columns = [];
      if (frame.current) columns.push(makeColumn(String(frame.current), 0, frame.scene === "metadata"));
      const secondary = frame.currentTranslation || frame.next;
      if (secondary) columns.push(makeColumn(String(secondary), 1, frame.scene === "metadata"));
      if (previousIdentity && frame.animateTransition && !reduced()) {
        stage.getAnimations().forEach(animation => animation.cancel());
        stage.animate([{ opacity:0, transform:"translateX(3px)" },
          { opacity:1, transform:"translateX(0)" }], { duration:180, easing:"ease-out" });
      }
    }
    cancel();
    render();
  }
  window.taskbarVerticalText = {
    applyStyle(payload) {
      const wasEnabled = enabled;
      enabled = payload?.isVertical === true;
      style = payload || {};
      stage.hidden = !enabled;
      document.getElementById("layout").style.display = enabled ? "none" : "";
      cancel();
      if (enabled) {
        stage.style.gap = 2 + (Number(style.lineGapPercent) || 0) / 100 * (Number(style.fontSize) || 18) + "px";
        identity = "";
        present(frame);
      } else if (wasEnabled) {
        stage.replaceChildren();
        columns = [];
        identity = "";
      }
    },
    present(payload) {
      if (!enabled) return false;
      present(payload);
      return true;
    }
  };
  new ResizeObserver(() => { cancel(); render(); }).observe(stage);
  document.addEventListener("visibilitychange", () => { cancel(); render(); });
})();
