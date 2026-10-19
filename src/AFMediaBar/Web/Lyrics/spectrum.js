// 频谱采样目标、绘制帧和动画请求由本模块闭包独占。
const spectrumPresentation = (() => {
let spectrumBarEls = Array.from(document.querySelectorAll(".spectrum span"));
let hasAudioDrivenSpectrum = false;
let spectrumAnimationFrame = 0;
let lastSpectrumFrameTime = 0;
let spectrumTargets = spectrumBarEls.map(() => 0);
let spectrumVisuals = spectrumBarEls.map(() => 0);
let spectrumSilence = spectrumBarEls.map(() => 0);
const spectrumTuning = {
  rise: 0.56,
  fall: 0.24,
  minHeight: 5,
  heightRange: 17,
  opacity: 0.78
};

// 频谱柱数量、几何和音频帧绘制。
function ensureSpectrumBarCount(value) {
  const count = Math.max(8, Math.min(32, Math.round(Number(value) || spectrumBarEls.length || 21)));
  if (!spectrumEl || spectrumBarEls.length === count) {
    return;
  }

  stopSpectrumRenderer();
  const fragment = document.createDocumentFragment();
  for (let index = 0; index < count; index++) {
    const bar = document.createElement("span");
    bar.style.setProperty("--i", index.toString());
    fragment.appendChild(bar);
  }

  spectrumEl.replaceChildren(fragment);
  spectrumBarEls = Array.from(spectrumEl.querySelectorAll("span"));
  spectrumTargets = spectrumBarEls.map(() => 0);
  spectrumVisuals = spectrumBarEls.map(() => 0);
  spectrumSilence = spectrumBarEls.map(() => 0);
  updateSpectrumGeometry();
  if (isSpectrumMode) {
    startSpectrumRenderer();
  }
}

function alignDownToPhysicalPixel(value) {
  const pixelsPerDip = Number(window.devicePixelRatio) || 1;
  return Math.floor(Math.max(0, value) * pixelsPerDip) / pixelsPerDip;
}

function updateSpectrumGeometry() {
  if (!spectrumEl || spectrumBarEls.length === 0) {
    return;
  }

  const availableWidth = spectrumEl.clientWidth;
  if (!Number.isFinite(availableWidth) || availableWidth <= 0) {
    return;
  }

  const barCount = spectrumBarEls.length;
  const gapCount = Math.max(0, barCount - 1);
  const desiredWidth = (requestedSpectrumBarWidthPx * barCount) + (requestedSpectrumGapPx * gapCount);
  let fittedBarWidth = requestedSpectrumBarWidthPx;
  let fittedGap = requestedSpectrumGapPx;

  if (desiredWidth > availableWidth) {
    const fitRatio = availableWidth / desiredWidth;
    const minimumBarWidth = 1 / (Number(window.devicePixelRatio) || 1);
    fittedBarWidth = Math.max(
      minimumBarWidth,
      alignDownToPhysicalPixel(requestedSpectrumBarWidthPx * fitRatio));
    const remainingWidth = Math.max(0, availableWidth - (fittedBarWidth * barCount));
    fittedGap = gapCount > 0
      ? alignDownToPhysicalPixel(Math.min(requestedSpectrumGapPx, remainingWidth / gapCount))
      : 0;
  }

  spectrumEl.style.setProperty("--spectrum-fitted-bar-width", `${fittedBarWidth}px`);
  spectrumEl.style.setProperty("--spectrum-fitted-gap", `${fittedGap}px`);
}

function setSpectrumTargetValues(values) {
  const hasValues = Array.isArray(values) && values.length > 0;
  for (let i = 0; i < spectrumTargets.length; i++) {
    spectrumTargets[i] = hasValues ? clamp01(values[i] ?? 0) : 0;
  }
}

function startSpectrumRenderer() {
  if (spectrumAnimationFrame) {
    return;
  }

  lastSpectrumFrameTime = 0;
  spectrumAnimationFrame = window.requestAnimationFrame(renderSpectrumFrame);
}

function stopSpectrumRenderer() {
  if (!spectrumAnimationFrame) {
    return;
  }

  window.cancelAnimationFrame(spectrumAnimationFrame);
  spectrumAnimationFrame = 0;
  lastSpectrumFrameTime = 0;
}

function renderSpectrumFrame(now) {
  if (!lastSpectrumFrameTime) {
    lastSpectrumFrameTime = now;
  }

  const elapsedFrames = Math.max(0.5, Math.min(2.4, (now - lastSpectrumFrameTime) / 16.67));
  lastSpectrumFrameTime = now;
  let isSettled = true;

  for (let i = 0; i < spectrumBarEls.length; i++) {
    const target = spectrumTargets[i] ?? 0;
    const current = spectrumVisuals[i] ?? 0;
    const baseRate = target > current ? spectrumTuning.rise : spectrumTuning.fall;
    const rate = 1 - Math.pow(1 - baseRate, elapsedFrames);
    const next = current + ((target - current) * rate);
    spectrumVisuals[i] = Math.abs(next - target) < 0.002 ? target : next;

    if (Math.abs(spectrumVisuals[i] - target) >= 0.002) {
      isSettled = false;
    }

    const level = spectrumVisuals[i];
    const height = Math.max(1, Math.round((spectrumTuning.minHeight + (level * spectrumTuning.heightRange)) * layoutScaleFactor));
    const bar = spectrumBarEls[i];
    bar.style.height = `${height}px`;
    bar.style.transform = "scaleY(1)";
    bar.style.opacity = spectrumTuning.opacity.toFixed(3);
  }

  if (hasAudioDrivenSpectrum || !isSettled) {
    spectrumAnimationFrame = window.requestAnimationFrame(renderSpectrumFrame);
  } else {
    spectrumAnimationFrame = 0;
    lastSpectrumFrameTime = 0;
  }
}

function setAudioDrivenSpectrum(values) {
  if (!Array.isArray(values) || values.length === 0) {
    hasAudioDrivenSpectrum = false;
    layoutEl.classList.remove("spectrum-audio-driven");
    setSpectrumTargetValues([]);
    startSpectrumRenderer();
    return;
  }

  hasAudioDrivenSpectrum = true;
  layoutEl.classList.add("spectrum-audio-driven");
  setSpectrumTargetValues(values);
  startSpectrumRenderer();
}

function clearSpectrumBars() {
  hasAudioDrivenSpectrum = false;
  setSpectrumTargetValues([]);
  stopSpectrumRenderer();
  layoutEl.classList.remove("spectrum-audio-driven");
  for (let i = 0; i < spectrumBarEls.length; i++) {
    spectrumVisuals[i] = 0;
    const bar = spectrumBarEls[i];
    bar.style.height = "";
    bar.style.transform = "";
    bar.style.opacity = "";
  }
}

function setSpectrum(values) {
    if (!isSpectrumMode) {
      return;
    }

    if (Array.isArray(values) && values.length > 0) {
      ensureSpectrumBarCount(values.length);
    }
    setAudioDrivenSpectrum(values);
  }

function setSpectrumTuning(payload) {
    if (!payload || typeof payload !== "object") {
      return;
    }

    ensureSpectrumBarCount(payload.barCount);
    spectrumTuning.rise = Math.max(0.02, Math.min(1, Number(payload.rise) || spectrumTuning.rise));
    spectrumTuning.fall = Math.max(0.02, Math.min(1, Number(payload.fall) || spectrumTuning.fall));
    spectrumTuning.minHeight = Math.max(1, Math.min(18, Number(payload.minHeight) || spectrumTuning.minHeight));
    spectrumTuning.heightRange = Math.max(2, Math.min(40, Number(payload.heightRange) || spectrumTuning.heightRange));
    spectrumTuning.opacity = Math.max(0.2, Math.min(1, Number(payload.opacity) || spectrumTuning.opacity));
    if (isSpectrumMode) {
      startSpectrumRenderer();
    }
  }


return { ensureSpectrumBarCount, updateSpectrumGeometry, startSpectrumRenderer, stopSpectrumRenderer, setAudioDrivenSpectrum, clearSpectrumBars, setSpectrum, setSpectrumTuning };
})();
const { ensureSpectrumBarCount, updateSpectrumGeometry, startSpectrumRenderer, stopSpectrumRenderer, setAudioDrivenSpectrum, clearSpectrumBars } = spectrumPresentation;
