const layoutEl = document.getElementById("layout");
const lyricsLayerEl = document.getElementById("lyricsLayer");
const spectrumLayerEl = document.getElementById("spectrumLayer");
const viewportEl = document.getElementById("viewport");
const trackEl = document.getElementById("track");
const currentLineEl = document.getElementById("currentLine");
const nextLineEl = document.getElementById("nextLine");
const incomingLineEl = document.getElementById("incomingLine");
const currentLineTextEl = document.getElementById("currentLineText");
const currentLineScanTextEl = document.getElementById("currentLineScanText");
const nextLineTextEl = document.getElementById("nextLineText");
const nextLineScanTextEl = document.getElementById("nextLineScanText");
const incomingLineTextEl = document.getElementById("incomingLineText");
const incomingTranslationPairEl = document.getElementById("incomingTranslationPair");
const incomingTranslationOriginalLineEl = document.getElementById("incomingTranslationOriginalLine");
const incomingTranslationOriginalTextEl = document.getElementById("incomingTranslationOriginalText");
const incomingTranslationOriginalScanTextEl = document.getElementById("incomingTranslationOriginalScanText");
const incomingTranslationLineEl = document.getElementById("incomingTranslationLine");
const incomingTranslationTextEl = document.getElementById("incomingTranslationText");
const coverEl = document.getElementById("cover");
const coverImageEl = document.getElementById("coverImage");
const coverImageNextEl = document.getElementById("coverImageNext");
const coverFallbackEl = document.getElementById("coverFallback");
const root = document.documentElement;
const scriptFontStyleEl = document.createElement("style");
document.head.appendChild(scriptFontStyleEl);
const quoteCssFont = (name) => `"${String(name || "").replace(/\\/g, "\\\\").replace(/"/g, '\\"').replace(/[\r\n\f]/g, " ")}"`;
const spectrumEl = document.querySelector(".spectrum");
let spectrumBarEls = Array.from(document.querySelectorAll(".spectrum span"));

let displayedCurrent = currentLineTextEl?.textContent || "";
let displayedNext = nextLineTextEl?.textContent || "";
let requestedFontSize = 13;
let currentSize = 13;
let viewportDescenderBufferPx = 2;
let layoutScaleFactor = 1;
let requestedSpectrumBarWidthPx = 3;
let requestedSpectrumGapPx = 3;
let rowHeightPx = 14;
let rowGapPx = 1;
let requestedLineGapPercent = 0;
let linePitchPx = 15;
let transitionStartTime = 0;
let transitionBaseNextOpacity = 0.72;
let transitionPromotedLine = "";
let transitionPromotedLineIndex = -1;
let transitionWordScanProgress = null;
let transitionUsesTranslationPair = false;
let isPlaybackPlaying = false;
let wordScanFreezeState = null;
let wordScanResumeCatchUpState = null;
let isTranslationMode = false;
let secondaryOpacity = 0.72;
let lastLineProgress = Number.NaN;
let lastCurrentLineIndex = -1;
let lastTrackId = "";
let metricsUpdatePending = false;
const lineScrollElements = new WeakMap();
const horizontalScrollMetrics = new WeakMap();
const transitionDurationMs = 560;
const translationPairTransitionDurationMs = 760;
const wordScanResumeCatchUpDurationMs = 180;
const currentLineRestOpacity = 0.98;
const leavingLineOpacity = 0.16;
const coverSwapDelayMs = 180;
const coverSwitchMinVisibleMs = 420;
const horizontalScrollAnchorRatio = 0.65;
const SEARCHING_TEXT = "\u6b63\u5728\u68c0\u7d22\u6b4c\u8bcd...";
const LEGACY_SEARCHING_TEXT = "\u6b63\u5728\u5339\u914d\u6b4c\u8bcd...";
let trackSwitchSearchTransitionActive = false;
let spectrumExitState = null;
let coverUpdateTimer = 0;
let coverStateTimer = 0;
let coverSwitchStartedAt = 0;
let activeCoverImageEl = coverImageEl;
let standbyCoverImageEl = coverImageNextEl;
let currentCoverUri = "";
let coverGeneration = 0;
let isSpectrumMode = false;
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

const lyricsTextAlignments = new Set(["Left", "Center", "Right"]);

const presentationApi = window.taskbarLyricsPresentation;
const presentationPlanner = new presentationApi.PresentationPlanner();
const presentationCoordinator = new presentationApi.PresentationCoordinator(presentationPlanner);
const transitionOperations = Object.freeze({
  LYRICS_FRAME: "lyricsFrame",
  SPECTRUM_ENTRY: "spectrumEntry",
  SPECTRUM_EXIT: "spectrumExit"
});
const transitionDispatcher = new presentationApi.TransitionDispatcher({
  patchTransition: executePatchTransition,
  replaceTransition: executeReplaceTransition,
  rollTransition: executeRollTransition,
  layerTransition: executeLayerTransition
});

function clamp01(value) {
  const parsed = Number(value);
  if (Number.isNaN(parsed)) {
    return 0;
  }
  return Math.max(0, Math.min(1, parsed));
}

function prefersReducedMotion() {
  return window.matchMedia?.("(prefers-reduced-motion: reduce)").matches === true;
}

function normalizeWordScanProgress(value) {
  if (value === null || value === undefined || value === "") {
    return null;
  }

  const parsed = Number(value);
  return Number.isFinite(parsed) ? clamp01(parsed) : null;
}

function normalizeTrackId(trackId) {
  if (trackId === null || trackId === undefined) {
    return "";
  }

  return String(trackId);
}

function normalizeLineIndex(currentLineIndex) {
  const parsed = Number(currentLineIndex);
  return Number.isInteger(parsed) ? parsed : -1;
}

function getCurrentWordScanVisualLineIndex() {
  return transitionPromotedLineIndex >= 0
    ? transitionPromotedLineIndex
    : lastCurrentLineIndex;
}

function isCurrentWordScanVisualIdentity(trackId, currentLineIndex) {
  return normalizeTrackId(trackId) === lastTrackId &&
    normalizeLineIndex(currentLineIndex) === getCurrentWordScanVisualLineIndex();
}

function getCurrentWordScanVisualLine(trackId, currentLineIndex) {
  if (!isCurrentWordScanVisualIdentity(trackId, currentLineIndex)) {
    return null;
  }

  if (transitionPromotedLineIndex >= 0) {
    return transitionUsesTranslationPair
      ? incomingTranslationOriginalLineEl
      : nextLineEl;
  }

  return currentLineEl;
}

function readInterpolatedWordScanProgress(lineElement, targetProgress) {
  if (!lineElement?.classList.contains("word-scan-smoothing") ||
      typeof window.getComputedStyle !== "function") {
    return null;
  }

  const inlineProgress = Number.parseFloat(
    lineElement.style.getPropertyValue("--word-scan-progress")) / 100;
  if (!Number.isFinite(inlineProgress) || targetProgress === null ||
      targetProgress < inlineProgress - 0.02 ||
      targetProgress - inlineProgress > 0.35) {
    return null;
  }

  const computedValue = window.getComputedStyle(lineElement)
    .getPropertyValue("--word-scan-progress");
  const parsed = Number.parseFloat(computedValue);
  return Number.isFinite(parsed) ? clamp01(parsed / 100) : null;
}

function hasSameWordScanProgress(left, right) {
  return left === right || (left === null && right === null);
}

function smootherStep(progress) {
  const normalized = clamp01(progress);
  return normalized * normalized * normalized *
    (normalized * ((normalized * 6) - 15) + 10);
}

function startWordScanResumeCatchUp(trackId, currentLineIndex, wordScanProgress) {
  wordScanResumeCatchUpState = null;
  if (!wordScanFreezeState || prefersReducedMotion()) {
    return;
  }

  const normalizedTrackId = normalizeTrackId(trackId);
  const normalizedLineIndex = normalizeLineIndex(currentLineIndex);
  const normalizedProgress = normalizeWordScanProgress(wordScanProgress);
  const frozenProgress = wordScanFreezeState.progress;
  const hasSameIdentity = wordScanFreezeState.trackId === normalizedTrackId &&
    wordScanFreezeState.currentLineIndex === normalizedLineIndex;
  const catchUpDistance = normalizedProgress === null || frozenProgress === null
    ? 0
    : normalizedProgress - frozenProgress;
  if (!hasSameIdentity || catchUpDistance <= 0) {
    return;
  }

  wordScanResumeCatchUpState = {
    trackId: normalizedTrackId,
    currentLineIndex: normalizedLineIndex,
    correction: catchUpDistance,
    startedAt: window.performance.now()
  };
}

function updateWordScanFreezeState(
  wasPlaying,
  isPlaying,
  trackId,
  currentLineIndex,
  wordScanProgress) {
  if (isPlaying) {
    if (!wasPlaying) {
      startWordScanResumeCatchUp(trackId, currentLineIndex, wordScanProgress);
    } else if (wordScanResumeCatchUpState &&
        (wordScanResumeCatchUpState.trackId !== normalizeTrackId(trackId) ||
         wordScanResumeCatchUpState.currentLineIndex !== normalizeLineIndex(currentLineIndex))) {
      wordScanResumeCatchUpState = null;
    }
    wordScanFreezeState = null;
    return;
  }

  wordScanResumeCatchUpState = null;

  const normalizedTrackId = normalizeTrackId(trackId);
  const normalizedLineIndex = normalizeLineIndex(currentLineIndex);
  const normalizedProgress = normalizeWordScanProgress(wordScanProgress);
  const hasSameFreezeIdentity = wordScanFreezeState?.trackId === normalizedTrackId &&
    wordScanFreezeState.currentLineIndex === normalizedLineIndex;

  if (wasPlaying || !hasSameFreezeIdentity ||
      !hasSameWordScanProgress(wordScanFreezeState.authoritativeProgress, normalizedProgress)) {
    const activeLine = wasPlaying
      ? getCurrentWordScanVisualLine(trackId, currentLineIndex)
      : null;
    wordScanFreezeState = {
      trackId: normalizedTrackId,
      currentLineIndex: normalizedLineIndex,
      progress: readInterpolatedWordScanProgress(activeLine, normalizedProgress) ?? normalizedProgress,
      authoritativeProgress: normalizedProgress
    };
  }
}

function isWordScanFreezeLine(lineElement) {
  if (!wordScanFreezeState || !lineElement) {
    return false;
  }

  if (lineElement === currentLineEl) {
    return wordScanFreezeState.currentLineIndex === getCurrentWordScanVisualLineIndex();
  }

  return (lineElement === nextLineEl || lineElement === incomingTranslationOriginalLineEl) &&
    wordScanFreezeState.currentLineIndex === transitionPromotedLineIndex;
}

function resolveWordScanProgress(lineElement, normalizedProgress) {
  if (normalizedProgress === null) {
    return normalizedProgress;
  }

  if (!isPlaybackPlaying) {
    return isWordScanFreezeLine(lineElement)
      ? wordScanFreezeState.progress
      : normalizedProgress;
  }

  if (!wordScanResumeCatchUpState || !isWordScanResumeCatchUpLine(lineElement)) {
    return normalizedProgress;
  }

  const elapsed = window.performance.now() - wordScanResumeCatchUpState.startedAt;
  if (elapsed >= wordScanResumeCatchUpDurationMs) {
    wordScanResumeCatchUpState = null;
    return normalizedProgress;
  }

  const remainingCorrection = wordScanResumeCatchUpState.correction *
    (1 - smootherStep(elapsed / wordScanResumeCatchUpDurationMs));
  return clamp01(normalizedProgress - remainingCorrection);
}

function isWordScanResumeCatchUpLine(lineElement) {
  if (!wordScanResumeCatchUpState || !lineElement) {
    return false;
  }

  if (lineElement === currentLineEl) {
    return wordScanResumeCatchUpState.currentLineIndex === getCurrentWordScanVisualLineIndex();
  }

  return (lineElement === nextLineEl || lineElement === incomingTranslationOriginalLineEl) &&
    wordScanResumeCatchUpState.currentLineIndex === transitionPromotedLineIndex;
}

function toDisplayLine(line, fallback = " ") {
  const text = (line ?? "").toString().trim();
  return text.length > 0 ? text : fallback;
}

function resolveTranslationDisplay(line) {
  const text = (line ?? "").toString().trim();
  return {
    text: text.length > 0 ? text : "…",
    isPlaceholder: text.length === 0
  };
}

function setTrackOffset(rowCount) {
  const offset = snapToPhysicalPixel(-linePitchPx * rowCount);
  if (offset === 0) {
    trackEl.style.removeProperty("transform");
    return;
  }

  trackEl.style.transform = `translateY(${offset}px)`;
}

function snapToPhysicalPixel(value) {
  const devicePixelRatio = Number(window.devicePixelRatio) || 1;
  return Math.round(value * devicePixelRatio) / devicePixelRatio;
}

function getLineScrollElements(lineElement) {
  if (!lineElement) {
    return null;
  }

  const cached = lineScrollElements.get(lineElement);
  if (cached) {
    return cached;
  }

  const textViewport = lineElement.querySelector(".line-text-viewport");
  const textStack = lineElement.querySelector(".line-text-stack");
  const baseText = lineElement.querySelector(".line-text-base");
  if (!textViewport || !textStack || !baseText) {
    return null;
  }

  const elements = { textViewport, textStack, baseText };
  lineScrollElements.set(lineElement, elements);
  return elements;
}

function clearLineHorizontalScroll(lineElement, discardMetrics = true) {
  if (!lineElement) {
    return;
  }

  if (discardMetrics) {
    horizontalScrollMetrics.delete(lineElement);
  }
  lineElement.classList.remove("horizontal-scrolling");
  getLineScrollElements(lineElement)?.textStack.style.removeProperty("--line-scroll-offset");
}

function measureLineHorizontalScroll(lineElement) {
  const cached = horizontalScrollMetrics.get(lineElement);
  if (cached) {
    return cached;
  }

  const elements = getLineScrollElements(lineElement);
  if (!elements) {
    return null;
  }

  const viewportWidth = elements.textViewport.clientWidth;
  const contentWidth = elements.baseText.scrollWidth;
  const metrics = {
    textStack: elements.textStack,
    viewportWidth,
    contentWidth,
    overflowWidth: Math.max(0, contentWidth - viewportWidth)
  };
  if (viewportWidth > 0 && contentWidth > 0) {
    horizontalScrollMetrics.set(lineElement, metrics);
  }
  return metrics;
}

function updateLineHorizontalScroll(lineElement, normalizedProgress) {
  if (normalizedProgress === null) {
    clearLineHorizontalScroll(lineElement);
    return;
  }

  const metrics = measureLineHorizontalScroll(lineElement);
  if (!metrics || metrics.viewportWidth <= 0 || metrics.overflowWidth < 0.5) {
    clearLineHorizontalScroll(lineElement, false);
    return;
  }

  const scanHeadPosition = normalizedProgress * metrics.contentWidth;
  const anchorPosition = metrics.viewportWidth * horizontalScrollAnchorRatio;
  const rawOffset = Math.max(0, Math.min(metrics.overflowWidth, scanHeadPosition - anchorPosition));
  const offset = snapToPhysicalPixel(rawOffset);
  lineElement.classList.add("horizontal-scrolling");
  metrics.textStack.style.setProperty("--line-scroll-offset", `${offset === 0 ? 0 : -offset}px`);
}

function refreshLineHorizontalScroll(lineElement) {
  horizontalScrollMetrics.delete(lineElement);
  const progress = Number.parseFloat(
    lineElement.style.getPropertyValue("--word-scan-progress")) / 100;
  updateLineHorizontalScroll(lineElement, Number.isFinite(progress) ? clamp01(progress) : null);
}

function setLineText(lineElement, baseTextElement, scanTextElement, text) {
  const isChanged = baseTextElement?.textContent !== text ||
    (scanTextElement && scanTextElement.textContent !== text);
  if (!isChanged) {
    return;
  }

  clearLineHorizontalScroll(lineElement);
  if (baseTextElement) {
    baseTextElement.textContent = text;
  }
  if (scanTextElement) {
    scanTextElement.textContent = text;
  }
}

function setCurrentLine(line) {
  const safe = toDisplayLine(line, SEARCHING_TEXT);
  setLineText(currentLineEl, currentLineTextEl, currentLineScanTextEl, safe);
  displayedCurrent = safe;
}

function setLineWordScanProgress(lineElement, progress, allowSmoothing = true) {
  const normalized = resolveWordScanProgress(
    lineElement,
    normalizeWordScanProgress(progress));
  const isScanning = normalized !== null;
  const previousProgress = Number.parseFloat(
    lineElement.style.getPropertyValue("--word-scan-progress")) / 100;
  const isContinuousProgress = Number.isFinite(previousProgress) &&
    normalized !== null &&
    normalized >= previousProgress - 0.02 &&
    normalized - previousProgress <= 0.35;
  lineElement.classList.toggle("word-scanning", isScanning);
  lineElement.classList.toggle(
    "word-scan-smoothing",
    isScanning &&
      normalized > 0 &&
      normalized < 1 &&
      isPlaybackPlaying &&
      !prefersReducedMotion() &&
      allowSmoothing &&
      isContinuousProgress);
  if (normalized === null) {
    lineElement.style.removeProperty("--word-scan-progress");
    updateLineHorizontalScroll(lineElement, null);
    return;
  }

  lineElement.style.setProperty("--word-scan-progress", `${(normalized * 100).toFixed(3)}%`);
  updateLineHorizontalScroll(lineElement, normalized);
}

function setWordScanProgress(progress, allowSmoothing = true) {
  setLineWordScanProgress(currentLineEl, progress, allowSmoothing);
}

function updateTransitionWordScanProgress(progress, allowSmoothing = true) {
  transitionWordScanProgress = progress;
  const targetLine = transitionUsesTranslationPair
    ? incomingTranslationOriginalLineEl
    : nextLineEl;
  setLineWordScanProgress(targetLine, progress, allowSmoothing);
  trackEl.classList.toggle(
    "word-scan-transition",
    !transitionUsesTranslationPair && normalizeWordScanProgress(progress) !== null);
}

function setSecondaryLine(line) {
  const translationDisplay = isTranslationMode
    ? resolveTranslationDisplay(line)
    : null;
  const safe = translationDisplay?.text ?? toDisplayLine(line, " ");
  nextLineEl.classList.toggle(
    "translation-placeholder",
    translationDisplay?.isPlaceholder === true);
  setLineText(nextLineEl, nextLineTextEl, nextLineScanTextEl, safe);
  displayedNext = safe;
}

function setIncomingLine(line) {
  setLineText(incomingLineEl, incomingLineTextEl, null, toDisplayLine(line, " "));
}

function setTranslationMode(enabled) {
  isTranslationMode = Boolean(enabled);
  layoutEl.classList.toggle("translation-mode", isTranslationMode);
  if (isTranslationMode) {
    setLineWordScanProgress(nextLineEl, null);
    nextLineEl.style.opacity = "";
  } else {
    clearIncomingTranslationPair();
  }
}

function setIncomingTranslationPair(original, translation, wordScanProgress) {
  const translationDisplay = resolveTranslationDisplay(translation);
  setLineText(
    incomingTranslationOriginalLineEl,
    incomingTranslationOriginalTextEl,
    incomingTranslationOriginalScanTextEl,
    toDisplayLine(original, SEARCHING_TEXT));
  setLineText(
    incomingTranslationLineEl,
    incomingTranslationTextEl,
    null,
    translationDisplay.text);
  incomingTranslationLineEl.classList.toggle(
    "translation-placeholder",
    translationDisplay.isPlaceholder);
  setLineWordScanProgress(incomingTranslationOriginalLineEl, wordScanProgress, false);
}

function clearIncomingTranslationPair() {
  incomingTranslationPairEl.classList.remove("preparing", "entering", "no-anim");
  incomingTranslationPairEl.style.opacity = "";
  incomingTranslationPairEl.style.transform = "";
  setLineWordScanProgress(incomingTranslationOriginalLineEl, null);
  setLineText(incomingTranslationOriginalLineEl, incomingTranslationOriginalTextEl, incomingTranslationOriginalScanTextEl, " ");
  setLineText(incomingTranslationLineEl, incomingTranslationTextEl, null, " ");
  incomingTranslationLineEl.classList.remove("translation-placeholder");
}

function updateSecondaryOpacity(progress) {
  if (isTranslationMode) {
    nextLineEl.style.opacity = "";
    return;
  }

  const p = clamp01(progress);
  const target = 0.58 + ((1 - p) * 0.16);
  secondaryOpacity += (target - secondaryOpacity) * 0.28;
  nextLineEl.style.opacity = secondaryOpacity.toFixed(3);
}

function easeOutCubic(t) {
  const x = 1 - clamp01(t);
  return 1 - (x * x * x);
}

function getFadeOutEase(t) {
  const normalized = clamp01(t / 0.74);
  if (normalized >= 0.97) {
    return 1;
  }

  return easeOutCubic(normalized);
}

function getFadeInEase(t) {
  const normalized = clamp01(t / 0.72);
  if (normalized >= 0.96) {
    return 1;
  }

  return easeOutCubic(normalized);
}

function stopTransitionOpacityAnimation() {
  presentationCoordinator.clearAnimationFrames();
}

function normalizeLyricsTextAlignment(value) {
  return lyricsTextAlignments.has(value) ? value : "Left";
}

function applyLyricsTextAlignment(value) {
  const alignment = normalizeLyricsTextAlignment(value);
  layoutEl.dataset.textAlignment = alignment;
  root.style.setProperty("--line-transform-origin", `${alignment.toLowerCase()} center`);
}

function isSearchingLine(line) {
  return line === SEARCHING_TEXT || line === LEGACY_SEARCHING_TEXT;
}

function setDisplayMode(showSpectrum) {
  const shouldShowSpectrum = Boolean(showSpectrum);
  if (isSpectrumMode === shouldShowSpectrum) {
    return;
  }

  isSpectrumMode = shouldShowSpectrum;
  layoutEl.classList.toggle("spectrum-mode", shouldShowSpectrum);
}

function createPresentationFrame({
  scene: requestedScene,
  current,
  next,
  progress,
  currentLineIndex,
  trackId,
  isPureMusic,
  isPlaying,
  wordScanProgress,
  currentTranslation,
  nextTranslation,
  translationMode
}) {
  const normalizedCurrent = String(current ?? "");
  const lineIndex = Number(currentLineIndex);
  const derivedScene = isPureMusic === true
    ? presentationApi.SCENES.SPECTRUM
    : isSearchingLine(normalizedCurrent)
      ? presentationApi.SCENES.SEARCHING
      : Number.isInteger(lineIndex) && lineIndex >= 0
        ? presentationApi.SCENES.LYRICS
        : presentationApi.SCENES.MESSAGE;
  return presentationApi.normalizeFrame({
    scene: requestedScene || derivedScene,
    layout: translationMode ? presentationApi.LAYOUTS.TRANSLATION_PAIR : presentationApi.LAYOUTS.SINGLE,
    current: normalizedCurrent,
    next,
    progress,
    currentLineIndex: lineIndex,
    trackId,
    isPureMusic,
    isPlaying,
    wordScanProgress,
    currentTranslation,
    nextTranslation,
    translationMode
  });
}

function clearSpectrumTransitionStyles() {
  layoutEl.classList.remove("spectrum-transitioning", "spectrum-entry-active");
  layoutEl.style.removeProperty("--layer-transition-duration");
  lyricsLayerEl?.style.removeProperty("opacity");
  lyricsLayerEl?.style.removeProperty("transform");
  spectrumLayerEl?.style.removeProperty("opacity");
  spectrumLayerEl?.style.removeProperty("transform");
}

function clearSpectrumExitStyles() {
  layoutEl.classList.remove("spectrum-exiting", "spectrum-exit-active");
  layoutEl.style.removeProperty("--layer-transition-duration");
  lyricsLayerEl?.style.removeProperty("opacity");
  lyricsLayerEl?.style.removeProperty("transform");
  spectrumLayerEl?.style.removeProperty("opacity");
  spectrumLayerEl?.style.removeProperty("transform");
}

function setLayerTransitionDuration(durationMs) {
  const normalizedDuration = Math.max(0, Number(durationMs) || 0);
  layoutEl.style.setProperty("--layer-transition-duration", `${normalizedDuration}ms`);
}

function isSpectrumExitTransitionActive() {
  return spectrumExitState !== null &&
    presentationCoordinator.activeTransition?.plan?.kind === presentationApi.TRANSITIONS.LAYER_SWITCH;
}

function shouldKeepStableContentForSameTrackSearch(targetFrame) {
  const currentFrame = presentationCoordinator.currentFrame;
  return !isSpectrumExitTransitionActive() &&
    (currentFrame.scene === presentationApi.SCENES.LYRICS ||
      currentFrame.scene === presentationApi.SCENES.SPECTRUM) &&
    targetFrame.scene === presentationApi.SCENES.SEARCHING &&
    targetFrame.trackId.length > 0 &&
    targetFrame.trackId === currentFrame.trackId;
}

function renderSpectrumExitTarget(frame) {
  const normalized = presentationApi.normalizeFrame(frame);
  renderLyricsFrameContent(normalized, false);
  return normalized;
}

function queueSpectrumExitTarget(frame) {
  if (!spectrumExitState) {
    return null;
  }

  const normalized = presentationApi.normalizeFrame(frame);
  spectrumExitState.latestFrame = normalized;
  if (normalized.scene === presentationApi.SCENES.SEARCHING ||
      normalized.scene === presentationApi.SCENES.NO_PLAYBACK) {
    // Searching metadata and the terminal no-playback state may replace the
    // hidden exit target. Keep ordinary lyric or message frames out of the
    // layer until exit completes so fast results cannot flash through it.
    renderSpectrumExitTarget(normalized);
    if (normalized.scene === presentationApi.SCENES.SEARCHING) {
      spectrumExitState.sawSearching = true;
      spectrumExitState.searchingFrame = normalized;
    }
  }
  presentationCoordinator.queueLatest(normalized);
  return normalized;
}

function applySpectrumExitImmediately(targetFrame) {
  cancelActiveTransition();
  spectrumExitState = null;
  clearSpectrumExitStyles();
  clearSpectrumTransitionStyles();
  clearSpectrumBars();
  const normalized = presentationApi.normalizeFrame(targetFrame);
  renderLyricsFrameContent(normalized, false);
  setDisplayMode(false);
  presentationCoordinator.setCurrentFrame(normalized);
}

function completeSpectrumExitTransition(initialTargetFrame) {
  const state = spectrumExitState;
  spectrumExitState = null;
  const pending = presentationCoordinator.takeLatest();
  const finalFrame = pending?.frame || state?.latestFrame || initialTargetFrame;
  const normalizedFinal = presentationApi.normalizeFrame(finalFrame);
  const searchingFrame = state?.searchingFrame;
  const shouldResumeFromSearch = state?.sawSearching === true &&
    normalizedFinal.scene === presentationApi.SCENES.LYRICS &&
    searchingFrame;

  if (shouldResumeFromSearch) {
    renderLyricsFrameContent(searchingFrame, false);
    clearSpectrumExitStyles();
    clearSpectrumTransitionStyles();
    setDisplayMode(false);
    presentationCoordinator.setCurrentFrame(searchingFrame);
    applyFrameAfterSearchTransition({ targetFrame: normalizedFinal });
    return;
  }

  renderLyricsFrameContent(normalizedFinal, false);
  clearSpectrumExitStyles();
  clearSpectrumTransitionStyles();
  setDisplayMode(false);
  presentationCoordinator.setCurrentFrame(normalizedFinal);
}

function startSpectrumExitTransition(targetFrame, plan) {
  const normalizedTarget = presentationApi.normalizeFrame(targetFrame);
  if (isSpectrumExitTransitionActive()) {
    queueSpectrumExitTarget(normalizedTarget);
    return;
  }

  clearSpectrumTransitionStyles();
  clearSpectrumExitStyles();
  setLayerTransitionDuration(plan.durationMs);
  setDisplayMode(true);
  const preparedTarget = renderSpectrumExitTarget(normalizedTarget);
  spectrumExitState = {
    latestFrame: preparedTarget,
    searchingFrame: preparedTarget.scene === presentationApi.SCENES.SEARCHING
      ? preparedTarget
      : null,
    sawSearching: preparedTarget.scene === presentationApi.SCENES.SEARCHING
  };
  presentationCoordinator.queueLatest(preparedTarget);

  let hasFinished = false;
  const finish = () => {
    if (hasFinished) {
      return;
    }

    hasFinished = true;
    context.finish();
  };
  const context = presentationCoordinator.beginTransition(
    plan,
    {
      complete: () => completeSpectrumExitTransition(normalizedTarget)
    });

  context.listenTransitionEnd(spectrumLayerEl, event => {
    if (event?.propertyName === "transform" || event?.propertyName === "opacity") {
      finish();
    }
  });
  context.listenTransitionEnd(lyricsLayerEl, event => {
    if (event?.propertyName === "transform" || event?.propertyName === "opacity") {
      finish();
    }
  });

  layoutEl.classList.add("spectrum-exiting");
  if (lyricsLayerEl) {
    lyricsLayerEl.style.opacity = "0";
    lyricsLayerEl.style.transform = `translateY(${linePitchPx}px)`;
  }
  if (spectrumLayerEl) {
    spectrumLayerEl.style.opacity = "1";
    spectrumLayerEl.style.transform = "translateY(0)";
  }
  void layoutEl.offsetHeight;
  context.requestFrame(() => {
    if (!context.isCurrent()) {
      return;
    }

    layoutEl.classList.add("spectrum-exit-active");
    if (lyricsLayerEl) {
      lyricsLayerEl.style.opacity = "1";
      lyricsLayerEl.style.transform = "translateY(0)";
    }
    if (spectrumLayerEl) {
      spectrumLayerEl.style.opacity = "0";
      spectrumLayerEl.style.transform = `translateY(${-linePitchPx}px)`;
    }
  });
  context.scheduleFallback(context.finish, plan.durationMs + 120);
}

function applySpectrumImmediately(targetFrame = null) {
  cancelActiveTransition();
  spectrumExitState = null;
  clearSpectrumExitStyles();
  clearSpectrumTransitionStyles();
  setDisplayMode(true);
  presentationCoordinator.setCurrentFrame(targetFrame || {
    scene: presentationApi.SCENES.SPECTRUM,
    isPureMusic: true,
    current: displayedCurrent,
    currentLineIndex: -1
  });
}

function isSpectrumEntryRollPlan(plan) {
  return plan?.kind === presentationApi.TRANSITIONS.SEARCHING_TO_SPECTRUM_ROLL ||
    plan?.kind === presentationApi.TRANSITIONS.NO_PLAYBACK_TO_SPECTRUM_ROLL;
}

function startSpectrumEntryRollTransition(targetFrame, plan) {
  if (isSpectrumEntryRollPlan(presentationCoordinator.activeTransition?.plan)) {
    presentationCoordinator.queueLatest(targetFrame);
    return;
  }

  const currentScene = presentationCoordinator.currentFrame.scene;
  if ((currentScene !== presentationApi.SCENES.SEARCHING &&
       currentScene !== presentationApi.SCENES.NO_PLAYBACK) ||
      prefersReducedMotion()) {
    applySpectrumImmediately(targetFrame);
    return;
  }

  cancelActiveTransition();
  let hasFinished = false;
  const finish = () => {
    if (hasFinished) {
      return;
    }

    hasFinished = true;
    context.finish();
  };
  const context = presentationCoordinator.beginTransition(plan, {
    complete: () => {
      const latestTarget = presentationCoordinator.takeLatest()?.frame || targetFrame;
      clearSpectrumTransitionStyles();
      setDisplayMode(true);
      presentationCoordinator.setCurrentFrame(latestTarget);
    }
  });

  context.listenTransitionEnd(spectrumLayerEl, event => {
    if (event?.propertyName === "transform" || event?.propertyName === "opacity") {
      finish();
    }
  });

  clearSpectrumTransitionStyles();
  setLayerTransitionDuration(plan.durationMs);
  setDisplayMode(false);
  layoutEl.classList.add("spectrum-transitioning");
  if (lyricsLayerEl) {
    lyricsLayerEl.style.opacity = "1";
    lyricsLayerEl.style.transform = "translateY(0)";
  }
  if (spectrumLayerEl) {
    spectrumLayerEl.style.opacity = "0";
    spectrumLayerEl.style.transform = `translateY(${linePitchPx}px)`;
  }
  void layoutEl.offsetHeight;
  setDisplayMode(true);
  context.requestFrame(() => {
    if (!context.isCurrent()) {
      return;
    }

    layoutEl.classList.add("spectrum-entry-active");
    lyricsLayerEl?.style.removeProperty("opacity");
    lyricsLayerEl?.style.removeProperty("transform");
    spectrumLayerEl?.style.removeProperty("opacity");
    spectrumLayerEl?.style.removeProperty("transform");
  });
  // The class transition provides the line-pitch roll; the fallback keeps
  // the layer switch deterministic when WebView2 omits transitionend.
  context.scheduleFallback(finish, plan.durationMs + 120);
}

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

function setCoverLoadingState(isLoading) {
  if (!coverEl) {
    return;
  }

  if (coverStateTimer) {
    window.clearTimeout(coverStateTimer);
    coverStateTimer = 0;
  }

  if (isLoading) {
    coverSwitchStartedAt = window.performance.now();
    coverEl.classList.add("switching");
    return;
  }

  const elapsed = coverSwitchStartedAt > 0
    ? window.performance.now() - coverSwitchStartedAt
    : coverSwitchMinVisibleMs;
  const delay = Math.max(0, coverSwitchMinVisibleMs - elapsed);
  coverStateTimer = window.setTimeout(() => {
    coverStateTimer = 0;
    coverSwitchStartedAt = 0;
    coverEl.classList.remove("switching");
  }, delay);
}

function clearCoverUpdateTimer() {
  if (coverUpdateTimer) {
    window.clearTimeout(coverUpdateTimer);
    coverUpdateTimer = 0;
  }
}

function swapCoverImageLayers() {
  const previous = activeCoverImageEl;
  activeCoverImageEl = standbyCoverImageEl;
  standbyCoverImageEl = previous;
}

function clearImageElement(imageEl) {
  if (!imageEl) {
    return;
  }

  imageEl.onload = null;
  imageEl.onerror = null;
  imageEl.style.opacity = "0";
  imageEl.removeAttribute("src");
}

function crossfadeToCoverImage(uri, generation, onDone) {
  if (!activeCoverImageEl || !standbyCoverImageEl) {
    if (typeof onDone === "function") {
      onDone();
    }
    return;
  }

  const incoming = standbyCoverImageEl;
  const outgoing = activeCoverImageEl;
  incoming.onload = null;
  incoming.onerror = null;
  incoming.style.opacity = "0";
  incoming.src = uri;

  window.requestAnimationFrame(() => {
    if (generation !== coverGeneration) {
      return;
    }

    incoming.style.opacity = "1";
    outgoing.style.opacity = "0";
    if (coverFallbackEl) {
      coverFallbackEl.style.opacity = "0";
    }
  });

  coverUpdateTimer = window.setTimeout(() => {
    coverUpdateTimer = 0;
    if (generation !== coverGeneration) {
      return;
    }

    clearImageElement(outgoing);
    swapCoverImageLayers();
    currentCoverUri = uri;
    if (coverFallbackEl) {
      coverFallbackEl.style.display = "none";
      coverFallbackEl.style.opacity = "1";
    }
    if (typeof onDone === "function") {
      onDone();
    }
  }, 460);
}

function applyFallbackCover(text, fallbackColor) {
  if (coverFallbackEl) {
    coverFallbackEl.textContent = text;
  }

  if (coverEl && fallbackColor && CSS.supports("color", fallbackColor)) {
    coverEl.style.backgroundColor = fallbackColor;
  }
}

function scheduleFallbackCoverUpdate(text, fallbackColor, onApplied) {
  clearCoverUpdateTimer();
  coverUpdateTimer = window.setTimeout(() => {
    coverUpdateTimer = 0;
    applyFallbackCover(text, fallbackColor);
    if (typeof onApplied === "function") {
      onApplied();
    }
  }, coverSwapDelayMs);
}

function resolveQueuedPresentationFrame(frame) {
  return presentationApi.normalizeFrame(frame?.targetFrame || frame);
}

function applyFrameAfterSearchTransition(frame) {
  const target = resolveQueuedPresentationFrame(frame);
  applyFrame(
    target.current,
    target.next,
    target.progress,
    target.currentLineIndex,
    target.wordScanProgress,
    target.currentTranslation,
    target.nextTranslation,
    target.translationMode,
    target);
}

function applyPendingPresentation(pending) {
  if (pending?.options?.presentation === "spectrum") {
    presentSpectrumFrame(
      pending.frame,
      pending.options.isPlaying,
      pending.options.animateTransition);
    return;
  }

  applyFrameAfterSearchTransition(pending?.frame);
}

function unsupportedTransitionOperation(primitive, operation) {
  throw new RangeError(`Unsupported ${primitive} operation: ${String(operation)}`);
}

function executePatchTransition(_plan, parameters) {
  if (parameters?.operation !== transitionOperations.LYRICS_FRAME) {
    unsupportedTransitionOperation("patchTransition", parameters?.operation);
  }

  applyProgressPatch(parameters.frame);
}

function executeReplaceTransition(_plan, parameters) {
  const frame = parameters?.frame;
  switch (parameters?.operation) {
    case transitionOperations.LYRICS_FRAME:
      applyFrameWithoutTransition(
        frame.current,
        frame.next,
        frame.progress,
        frame.currentLineIndex,
        frame.wordScanProgress,
        frame.currentTranslation,
        frame.nextTranslation,
        frame.translationMode,
        frame);
      return;
    case transitionOperations.SPECTRUM_ENTRY:
      applySpectrumFrame(frame, parameters.isPlaying);
      return;
    case transitionOperations.SPECTRUM_EXIT:
      applySpectrumExitImmediately(frame);
      return;
    default:
      unsupportedTransitionOperation("replaceTransition", parameters?.operation);
  }
}

function executeRollTransition(_plan, parameters) {
  if (parameters?.operation !== transitionOperations.LYRICS_FRAME) {
    unsupportedTransitionOperation("rollTransition", parameters?.operation);
  }

  const frame = parameters.frame;
  startTransition(
    frame.current,
    frame.next,
    frame.progress,
    frame.currentLineIndex,
    frame.wordScanProgress,
    frame.currentTranslation,
    frame.nextTranslation,
    frame.layout === presentationApi.LAYOUTS.TRANSLATION_PAIR,
    frame);
}

function executeLayerTransition(plan, parameters) {
  const frame = parameters?.frame;
  switch (parameters?.operation) {
    case transitionOperations.SPECTRUM_ENTRY:
      if (isSpectrumEntryRollPlan(plan)) {
        startSpectrumEntryRollTransition(frame, plan);
        if (parameters.isPlaying === false) {
          setAudioDrivenSpectrum(spectrumSilence);
        }
      } else {
        // Preserve the existing ordinary lyrics/message-to-spectrum behavior.
        applySpectrumFrame(frame, parameters.isPlaying);
      }
      return;
    case transitionOperations.SPECTRUM_EXIT:
      startSpectrumExitTransition(frame, plan);
      return;
    default:
      unsupportedTransitionOperation("layerTransition", parameters?.operation);
  }
}

function applyFrameWithoutTransition(
  safeCurrent,
  safeNext,
  progress,
  currentLineIndex,
  wordScanProgress,
  currentTranslation = "",
  nextTranslation = "",
  translationMode = false,
  targetFrame = null) {
  const resolvedTargetFrame = targetFrame || createPresentationFrame({
    current: safeCurrent,
    next: safeNext,
    progress,
    currentLineIndex,
    currentTranslation,
    nextTranslation,
    translationMode
  });
  cancelActiveTransition();
  renderLyricsFrame(resolvedTargetFrame, false);
}

function renderLyricsFrameContent(frame, allowWordScanSmoothing = true) {
  const normalized = presentationApi.normalizeFrame(frame);
  const useTranslationPair = normalized.layout === presentationApi.LAYOUTS.TRANSLATION_PAIR;
  const safeCurrent = toDisplayLine(normalized.current, SEARCHING_TEXT);
  const visibleSecondary = useTranslationPair
    ? toDisplayLine(normalized.currentTranslation, " ")
    : toDisplayLine(normalized.next, " ");

  setTranslationMode(useTranslationPair);
  setIncomingLine("");
  setCurrentLine(safeCurrent);
  setWordScanProgress(normalized.wordScanProgress, allowWordScanSmoothing);
  setSecondaryLine(visibleSecondary);
  updateSecondaryOpacity(normalized.progress);
  lastCurrentLineIndex = normalized.currentLineIndex >= 0
    ? normalized.currentLineIndex
    : -1;
  lastLineProgress = normalized.progress;
  if (normalized.trackId.length > 0) {
    lastTrackId = normalized.trackId;
  }
  return normalized;
}

function renderLyricsFrame(frame, allowWordScanSmoothing = true) {
  const normalized = renderLyricsFrameContent(frame, allowWordScanSmoothing);
  presentationCoordinator.setCurrentFrame(normalized);
  return normalized;
}

function applyProgressPatch(frame) {
  const normalized = presentationApi.normalizeFrame(frame);
  const useTranslationPair = normalized.layout === presentationApi.LAYOUTS.TRANSLATION_PAIR;
  const visibleSecondary = useTranslationPair
    ? toDisplayLine(normalized.currentTranslation, " ")
    : toDisplayLine(normalized.next, " ");

  setTranslationMode(useTranslationPair);
  setWordScanProgress(normalized.wordScanProgress);
  setSecondaryLine(visibleSecondary);
  updateSecondaryOpacity(normalized.progress);
  lastCurrentLineIndex = normalized.currentLineIndex >= 0
    ? normalized.currentLineIndex
    : -1;
  lastLineProgress = normalized.progress;
  if (normalized.trackId.length > 0) {
    lastTrackId = normalized.trackId;
  }
  presentationCoordinator.setCurrentFrame(normalized);
}

function cancelActiveTransition() {
  const wasSpectrumExit = isSpectrumExitTransitionActive();
  presentationCoordinator.cancelTransition();
  trackSwitchSearchTransitionActive = false;
  if (wasSpectrumExit) {
    spectrumExitState = null;
  }
  stopTransitionOpacityAnimation();
  presentationCoordinator.takeLatest();
  clearSpectrumExitStyles();
  trackEl.classList.add("no-anim");
  trackEl.classList.remove("animating");
  trackEl.classList.remove("translation-pair-animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  setLineWordScanProgress(nextLineEl, null);
  trackEl.classList.remove("word-scan-transition");
  transitionPromotedLine = "";
  transitionPromotedLineIndex = -1;
  transitionWordScanProgress = null;
  transitionUsesTranslationPair = false;
  setTrackOffset(0);
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  nextLineEl.style.fontSize = "";
  nextLineEl.style.removeProperty("transform");
  nextLineEl.style.removeProperty("--promotion-scale");
  incomingLineEl.style.opacity = secondaryOpacity.toFixed(3);
  clearIncomingTranslationPair();
  void trackEl.offsetHeight;
  trackEl.classList.remove("no-anim");
}

function resetForTrackSwitch(
  safeCurrent,
  safeNext,
  progress,
  currentLineIndex,
  trackId,
  wordScanProgress,
  currentTranslation,
  nextTranslation,
  translationMode,
  targetFrame) {
  cancelActiveTransition();
  setTranslationMode(false);
  lastTrackId = trackId;
  lastCurrentLineIndex = -1;
  lastLineProgress = 0;
  setCoverLoadingState(true);

  const target = presentationApi.normalizeFrame(targetFrame || {
    current: safeCurrent,
    next: safeNext,
    progress,
    currentLineIndex,
    trackId,
    currentTranslation,
    nextTranslation,
    translationMode
  });
  const hasLyricFrame = target.scene === presentationApi.SCENES.LYRICS &&
    Number.isInteger(target.currentLineIndex) &&
    target.currentLineIndex >= 0;
  if (hasLyricFrame) {
    // The host already has the lyrics. A track change (or the first timed line
    // after an instrumental intro) must not invent a visible searching state.
    trackSwitchSearchTransitionActive = false;
    renderLyricsFrame(target, false);
    return;
  }
  const searchFrame = target.scene === presentationApi.SCENES.SEARCHING
    ? target
    : presentationApi.normalizeFrame({
      scene: presentationApi.SCENES.SEARCHING,
      current: SEARCHING_TEXT,
      next: " ",
      progress: 0,
      currentLineIndex: -1,
      trackId
    });
  const currentIsSearching = presentationCoordinator.currentFrame.scene ===
    presentationApi.SCENES.SEARCHING;

  if (!currentIsSearching) {
    trackSwitchSearchTransitionActive = true;
    applyFrame(
      searchFrame.current,
      searchFrame.next,
      0,
      -1,
      null,
      "",
      "",
      false,
      searchFrame);
    trackSwitchSearchTransitionActive = presentationCoordinator.isTransitioning;
  } else if (target.scene === presentationApi.SCENES.SEARCHING) {
    // A fast switch between tracks can keep the searching scene active. Apply
    // the new metadata frame so the previous track's title cannot linger.
    trackSwitchSearchTransitionActive = true;
    applyFrame(
      searchFrame.current,
      searchFrame.next,
      searchFrame.progress,
      searchFrame.currentLineIndex,
      searchFrame.wordScanProgress,
      searchFrame.currentTranslation,
      searchFrame.nextTranslation,
      searchFrame.translationMode,
      searchFrame);
    trackSwitchSearchTransitionActive = presentationCoordinator.isTransitioning;
  } else {
    presentationCoordinator.setCurrentFrame(searchFrame);
    setSecondaryLine(" ");
    updateSecondaryOpacity(0);
  }
}


function runTransitionOpacityAnimation(now) {
  if (!presentationCoordinator.isTransitioning) {
    return;
  }

  const elapsed = Math.max(0, now - transitionStartTime);
  const t = clamp01(elapsed / transitionDurationMs);
  const fadeOutE = getFadeOutEase(t);
  const fadeInE = getFadeInEase(t);

  currentLineEl.style.opacity = String(currentLineRestOpacity + ((leavingLineOpacity - currentLineRestOpacity) * fadeOutE));
  nextLineEl.style.opacity = String(transitionBaseNextOpacity + ((currentLineRestOpacity - transitionBaseNextOpacity) * fadeInE));
  incomingLineEl.style.opacity = secondaryOpacity.toFixed(3);

  if (t < 1) {
    const generation = presentationCoordinator.activeGeneration;
    presentationCoordinator.requestFrame(generation, runTransitionOpacityAnimation);
  }
}

function applyFrame(
  safeCurrent,
  safeNext,
  progress,
  currentLineIndex,
  wordScanProgress,
  currentTranslation = "",
  nextTranslation = "",
  translationMode = false,
  targetFrame = null) {
  const resolvedTargetFrame = targetFrame || createPresentationFrame({
    current: safeCurrent,
    next: safeNext,
    progress,
    currentLineIndex,
    currentTranslation,
    nextTranslation,
    translationMode
  });
  if (presentationCoordinator.isTransitioning) {
    presentationCoordinator.queueLatest(resolvedTargetFrame);
    const updatesPromotedLine = transitionPromotedLineIndex >= 0
      ? currentLineIndex === transitionPromotedLineIndex
      : safeCurrent === transitionPromotedLine;
    if (updatesPromotedLine) {
      updateTransitionWordScanProgress(wordScanProgress);
    }
    return;
  }

  const plan = presentationPlanner.plan(
    presentationCoordinator.currentFrame,
    resolvedTargetFrame,
    {
      animateTransition: true,
      reducedMotion: prefersReducedMotion()
    });
  const normalized = presentationApi.normalizeFrame(resolvedTargetFrame);
  transitionDispatcher.execute(plan, {
    operation: transitionOperations.LYRICS_FRAME,
    frame: normalized
  });
}

function updateMetrics() {
  if (presentationCoordinator.isTransitioning) {
    metricsUpdatePending = true;
    return;
  }

  metricsUpdatePending = false;
  // Exclude the host-provided descender buffer from row metrics.
  const measuredViewportHeight = viewportEl.clientHeight || 30;
  const minimumHostHeight = Math.max(2, Math.round(26 * layoutScaleFactor));
  const hostHeight = Math.max(minimumHostHeight, measuredViewportHeight - viewportDescenderBufferPx);
  // 先按"没有额外行距"的基准算出当前字号与字号夹取的下限：行距的夹取必须保证每行不低于
  // 字号 ÷ 0.92（字号夹取规则的下限），因此先得到这个下限，再让 resolveRowMetrics 求行高/间隙，
  // 最后才夹字号。行距按字号的百分比换算成像素，随字号等比缩放。
  // First derive the base font size and the lower bound of the font clamp from the no-extra-gap layout: the gap clamp has to
  // keep each row at least font size ÷ 0.92 tall (the floor of the font clamp), so the lower bound is computed first, then
  // resolveRowMetrics derives the row heights and the gap, and only afterwards is the font size clamped. The percentage gap is
  // converted into pixels against this font size, so it scales with the font.
  const baseRowHeightPx = Math.max(1, Math.floor(hostHeight / 2));
  const baseSizeMax = Math.max(11.2 * layoutScaleFactor, baseRowHeightPx * 0.92);
  const baseSize = Math.min(requestedFontSize, baseSizeMax);
  const minimumRowHeightPx = baseSize / 0.92;
  const requestedGapPx = requestedLineGapPercent > 0
    ? (requestedLineGapPercent / 100) * baseSize
    : 0;
  const rowMetrics = window.taskbarLyricsSpacing.resolveRowMetrics(
    hostHeight,
    requestedGapPx,
    minimumRowHeightPx
  );
  rowHeightPx = rowMetrics.rowHeightPx;
  rowGapPx = rowMetrics.rowGapPx;
  linePitchPx = rowMetrics.linePitchPx;
  const currentSizeMax = Math.max(11.2 * layoutScaleFactor, rowHeightPx * 0.92);
  currentSize = Math.min(requestedFontSize, currentSizeMax);
  const nextSize = Math.max(9 * layoutScaleFactor, currentSize * 0.92);
  root.style.setProperty("--row-height", `${rowHeightPx}px`);
  root.style.setProperty("--row-gap", `${rowGapPx}px`);
  root.style.setProperty("--line-pitch", `${linePitchPx}px`);
  root.style.setProperty("--current-size", `${currentSize.toFixed(2)}px`);
  root.style.setProperty("--next-size", `${nextSize.toFixed(2)}px`);
  setTrackOffset(0);
  refreshLineHorizontalScroll(currentLineEl);
}

function finalizeTransition(
  promotedCurrent,
  upcomingNext,
  progress,
  promotedLineIndex = -1,
  wordScanProgress = null,
  targetFrame = null) {
  const incomingEndOpacity = Number.parseFloat(window.getComputedStyle(incomingLineEl).opacity || "0.72");
  trackSwitchSearchTransitionActive = false;

  // Freeze transitions while swapping layers to avoid visible "grow then shrink" rebound.
  trackEl.classList.add("no-anim");
  stopTransitionOpacityAnimation();
  currentLineEl.style.opacity = String(leavingLineOpacity);
  nextLineEl.style.opacity = String(currentLineRestOpacity);
  void trackEl.offsetHeight;
  setCurrentLine(promotedCurrent);
  setWordScanProgress(wordScanProgress, false);
  setSecondaryLine(upcomingNext);
  setLineWordScanProgress(nextLineEl, null);
  setIncomingLine("");
  trackEl.classList.remove("animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  trackEl.classList.remove("word-scan-transition");
  transitionPromotedLine = "";
  transitionPromotedLineIndex = -1;
  transitionWordScanProgress = null;
  transitionUsesTranslationPair = false;
  setTrackOffset(0);
  // Reset inline opacity channels while transitions are disabled; otherwise a brief flash can appear.
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  secondaryOpacity = Number.isFinite(incomingEndOpacity) ? incomingEndOpacity : 0.72;
  incomingLineEl.style.opacity = "";
  nextLineEl.style.fontSize = "";
  nextLineEl.style.removeProperty("transform");
  nextLineEl.style.removeProperty("--promotion-scale");
  updateSecondaryOpacity(progress);
  void trackEl.offsetHeight;
  trackEl.classList.remove("no-anim");
  presentationCoordinator.setCurrentFrame(targetFrame || createPresentationFrame({
    current: promotedCurrent,
    next: upcomingNext,
    progress,
    currentLineIndex: promotedLineIndex,
    wordScanProgress
  }));
  lastLineProgress = clamp01(progress);
  if (Number.isInteger(promotedLineIndex) && promotedLineIndex >= 0) {
    lastCurrentLineIndex = promotedLineIndex;
  }
  if (metricsUpdatePending) {
    updateMetrics();
  }

  const pending = presentationCoordinator.takeLatest();
  if (pending) {
    applyPendingPresentation(pending);
  }
}

function startTransition(
  newCurrent,
  newNext,
  progress,
  currentLineIndex = -1,
  wordScanProgress = null,
  currentTranslation = "",
  nextTranslation = "",
  translationMode = false,
  targetFrame = null) {
  const resolvedTargetFrame = targetFrame || createPresentationFrame({
    current: newCurrent,
    next: newNext,
    progress,
    currentLineIndex,
    currentTranslation,
    nextTranslation,
    translationMode
  });
  if (presentationCoordinator.isTransitioning) {
    presentationCoordinator.queueLatest(resolvedTargetFrame);
    return;
  }

  setTranslationMode(Boolean(translationMode));
  if (translationMode) {
    startTranslationPairTransition(
      newCurrent,
      currentTranslation,
      progress,
      currentLineIndex,
      wordScanProgress,
      resolvedTargetFrame);
    return;
  }

  startStandardTransition(newCurrent, newNext, progress, currentLineIndex, wordScanProgress, resolvedTargetFrame);
}

function finalizeTranslationPairTransition(
  promotedCurrent,
  promotedTranslation,
  progress,
  promotedLineIndex,
  wordScanProgress,
  targetFrame = null) {
  trackEl.classList.add("no-anim");
  incomingTranslationPairEl.classList.add("no-anim");
  stopTransitionOpacityAnimation();
  setCurrentLine(promotedCurrent);
  setWordScanProgress(wordScanProgress, false);
  setSecondaryLine(promotedTranslation);
  setLineWordScanProgress(nextLineEl, null);
  trackEl.classList.remove("translation-pair-animating", "animating", "word-scan-transition");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  transitionPromotedLine = "";
  transitionPromotedLineIndex = -1;
  transitionWordScanProgress = null;
  transitionUsesTranslationPair = false;
  setTrackOffset(0);
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  clearIncomingTranslationPair();
  void trackEl.offsetHeight;
  trackEl.classList.remove("no-anim");
  presentationCoordinator.setCurrentFrame(targetFrame || createPresentationFrame({
    scene: presentationApi.SCENES.LYRICS,
    layout: presentationApi.LAYOUTS.TRANSLATION_PAIR,
    current: promotedCurrent,
    currentTranslation: promotedTranslation,
    progress,
    currentLineIndex: promotedLineIndex,
    wordScanProgress,
    translationMode: true
  }));
  lastLineProgress = clamp01(progress);
  if (Number.isInteger(promotedLineIndex) && promotedLineIndex >= 0) {
    lastCurrentLineIndex = promotedLineIndex;
  }
  if (metricsUpdatePending) {
    updateMetrics();
  }

  const pending = presentationCoordinator.takeLatest();
  if (pending) {
    applyPendingPresentation(pending);
  }
}

function startTranslationPairTransition(
  newCurrent,
  currentTranslation,
  progress,
  currentLineIndex,
  wordScanProgress,
  targetFrame = null) {
  transitionUsesTranslationPair = true;
  const promoted = toDisplayLine(newCurrent, SEARCHING_TEXT);
  const promotedTranslation = toDisplayLine(currentTranslation, " ");
  transitionPromotedLine = promoted;
  transitionPromotedLineIndex = currentLineIndex;
  transitionWordScanProgress = wordScanProgress;
  stopTransitionOpacityAnimation();

  const context = presentationCoordinator.beginTransition(
    {
      kind: presentationApi.TRANSITIONS.TRANSLATION_PAIR_ROLL,
      durationMs: translationPairTransitionDurationMs
    },
    {
      complete: () => finalizeTranslationPairTransition(
        promoted,
        promotedTranslation,
        progress,
        currentLineIndex,
        transitionWordScanProgress,
        targetFrame)
    });
  trackEl.classList.add("no-anim");
  trackEl.classList.remove("animating", "translation-pair-animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  setTrackOffset(0);
  clearIncomingTranslationPair();
  incomingTranslationPairEl.classList.add("preparing", "no-anim");
  setIncomingTranslationPair(promoted, promotedTranslation, wordScanProgress);
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  void incomingTranslationPairEl.offsetHeight;
  trackEl.classList.remove("no-anim");
  incomingTranslationPairEl.classList.remove("no-anim");

  let hasFinished = false;
  const finish = () => {
    if (hasFinished) {
      return;
    }

    hasFinished = true;
    context.finish();
  };
  const onTransitionEnd = (event) => {
    if (!event || event.target !== trackEl || event.propertyName !== "transform") {
      return;
    }

    finish();
  };

  context.listenTransitionEnd(trackEl, onTransitionEnd);
  context.requestFrame(() => {
    if (!context.isCurrent()) {
      return;
    }

    incomingTranslationPairEl.classList.remove("preparing");
    incomingTranslationPairEl.classList.add("entering");
    trackEl.classList.add("translation-pair-animating", "animating");
    context.requestFrame(() => {
      if (!context.isCurrent()) {
        return;
      }

      setTrackOffset(2);
      if (prefersReducedMotion()) {
        context.requestFrame(finish);
      }
    });
  });
  context.scheduleFallback(finish, translationPairTransitionDurationMs + 120);
}

function startStandardTransition(
  newCurrent,
  newNext,
  progress,
  currentLineIndex = -1,
  wordScanProgress = null,
  targetFrame = null) {
  transitionUsesTranslationPair = false;
  const promoted = toDisplayLine(newCurrent, SEARCHING_TEXT);
  const upcoming = toDisplayLine(newNext, " ");
  transitionPromotedLine = promoted;
  transitionPromotedLineIndex = currentLineIndex;
  transitionWordScanProgress = wordScanProgress;
  transitionBaseNextOpacity = secondaryOpacity;
  const nextFontSize = Number.parseFloat(window.getComputedStyle(nextLineEl).fontSize || "12");
  const currentFontSize = Number.parseFloat(window.getComputedStyle(currentLineEl).fontSize || "13");
  const promotionStartScale = currentFontSize > 0
    ? clamp01(nextFontSize / currentFontSize)
    : 1;
  transitionStartTime = 0;
  stopTransitionOpacityAnimation();

  const context = presentationCoordinator.beginTransition(
    {
      kind: presentationApi.TRANSITIONS.SINGLE_ROLL,
      durationMs: transitionDurationMs
    },
    {
      complete: () => finalizeTransition(
        promoted,
        upcoming,
        progress,
        currentLineIndex,
        transitionWordScanProgress,
        targetFrame)
    });

  // Render with final font metrics from the start, then animate only the visual scale.
  trackEl.classList.add("no-anim");
  trackEl.classList.remove("animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  setTrackOffset(0);
  setLineText(nextLineEl, nextLineTextEl, nextLineScanTextEl, promoted);
  setIncomingLine(upcoming);
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  nextLineEl.style.fontSize = `${currentFontSize.toFixed(3)}px`;
  nextLineEl.style.setProperty("--promotion-scale", promotionStartScale.toFixed(6));
  nextLineEl.style.transform = "translateY(0px) scale(var(--promotion-scale))";
  updateTransitionWordScanProgress(wordScanProgress, false);
  incomingLineEl.style.opacity = secondaryOpacity.toFixed(3);
  void trackEl.offsetHeight;
  trackEl.classList.remove("no-anim");

  const onTransitionEnd = (event) => {
    if (!event || event.target !== trackEl || event.propertyName !== "transform") {
      return;
    }

    context.finish();
  };

  context.listenTransitionEnd(trackEl, onTransitionEnd);
  context.requestFrame(() => {
    if (!context.isCurrent()) {
      return;
    }

    transitionStartTime = window.performance.now();
    context.requestFrame(runTransitionOpacityAnimation);
    currentLineEl.classList.add("leaving");
    nextLineEl.classList.add("promoting");
    nextLineEl.style.setProperty("--promotion-scale", "1");
    nextLineEl.style.removeProperty("transform");
    trackEl.classList.add("animating");
    context.requestFrame(() => {
      if (context.isCurrent()) {
        setTrackOffset(1);
      }
    });
  });
  context.scheduleFallback(context.finish, transitionDurationMs + 120);
}

function applySpectrumFrame(targetFrame, isPlaying) {
  cancelActiveTransition();
  spectrumExitState = null;
  clearSpectrumExitStyles();
  setTranslationMode(false);
  clearSpectrumTransitionStyles();
  setWordScanProgress(null, false);
  setSecondaryLine(" ");
  setIncomingLine("");
  lastCurrentLineIndex = -1;
  lastLineProgress = 0;
  setDisplayMode(true);
  if (isPlaying === false) {
    setAudioDrivenSpectrum(spectrumSilence);
  }
  presentationCoordinator.setCurrentFrame(targetFrame);
}

function cancelSpectrumPresentationIfActive() {
  if (!isSpectrumEntryRollPlan(presentationCoordinator.activeTransition?.plan)) {
    return;
  }

  cancelActiveTransition();
  clearSpectrumTransitionStyles();
  setDisplayMode(false);
}

function presentSpectrumFrame(targetFrame, isPlaying, animateTransition) {
  if (trackSwitchSearchTransitionActive && presentationCoordinator.isTransitioning) {
    presentationCoordinator.queueLatest(targetFrame, {
      presentation: "spectrum",
      isPlaying,
      animateTransition
    });
    return;
  }

  if (!isSpectrumMode) {
    clearSpectrumBars();
  }
  const plan = presentationPlanner.plan(
    presentationCoordinator.currentFrame,
    targetFrame,
    {
      animateTransition,
      reducedMotion: prefersReducedMotion()
    });
  transitionDispatcher.execute(plan, {
    operation: transitionOperations.SPECTRUM_ENTRY,
    frame: targetFrame,
    isPlaying,
    animateTransition
  });
}

function updateResponsiveLayout() {
  updateMetrics();
  updateSpectrumGeometry();
}

updateResponsiveLayout();
setTranslationMode(false);
setCurrentLine(displayedCurrent);
setWordScanProgress(null);
setSecondaryLine(displayedNext);
setIncomingLine("");
updateSecondaryOpacity(0);
presentationCoordinator.setCurrentFrame(createPresentationFrame({
  current: displayedCurrent,
  next: displayedNext,
  progress: 0,
  currentLineIndex: -1,
  trackId: "",
  isPureMusic: false,
  isPlaying: false,
  translationMode: false
}));

if (typeof ResizeObserver !== "undefined") {
  new ResizeObserver(updateResponsiveLayout).observe(layoutEl);
} else {
  window.addEventListener("resize", updateResponsiveLayout);
}

if (document.fonts?.ready) {
  document.fonts.ready.then(updateResponsiveLayout).catch(() => {});
}

const lyricsApi = {
  setLyrics(
    current,
    next,
    progress,
    currentLineIndex,
    trackId,
    isPureMusic,
    isPlaying,
    wordScanProgress,
    currentTranslation,
    nextTranslation,
    translationMode,
    animateTransition = true,
    scene = null) {
    const wasPlaying = isPlaybackPlaying;
    const nextIsPlaying = Boolean(isPlaying);
    const safeCurrent = toDisplayLine(current, SEARCHING_TEXT);
    const safeNext = toDisplayLine(next, " ");
    const safeCurrentTranslation = toDisplayLine(currentTranslation, " ");
    const safeNextTranslation = toDisplayLine(nextTranslation, " ");
    const useTranslationPair = Boolean(translationMode);
    const p = clamp01(progress);
    const lineIndex = Number(currentLineIndex);
    const normalizedTrackId = normalizeTrackId(trackId);
    updateWordScanFreezeState(
      wasPlaying,
      nextIsPlaying,
      normalizedTrackId,
      lineIndex,
      wordScanProgress);
    isPlaybackPlaying = nextIsPlaying;
    const targetFrame = createPresentationFrame({
      scene,
      current: safeCurrent,
      next: safeNext,
      progress: p,
      currentLineIndex: lineIndex,
      trackId: normalizedTrackId,
      isPureMusic: Boolean(isPureMusic),
      isPlaying,
      wordScanProgress,
      currentTranslation: safeCurrentTranslation,
      nextTranslation: safeNextTranslation,
      translationMode: useTranslationPair
    });
    const shouldShowSpectrum = targetFrame.scene === presentationApi.SCENES.SPECTRUM;

    if (shouldShowSpectrum) {
      if (normalizedTrackId.length > 0) {
        lastTrackId = normalizedTrackId;
      }
      presentSpectrumFrame(targetFrame, isPlaying, animateTransition);
      return;
    }

    if (shouldKeepStableContentForSameTrackSearch(targetFrame)) {
      return;
    }

    if (presentationCoordinator.currentFrame.scene === presentationApi.SCENES.SPECTRUM ||
        isSpectrumExitTransitionActive()) {
      const plan = presentationPlanner.plan(
        presentationCoordinator.currentFrame,
        targetFrame,
        {
          animateTransition,
          reducedMotion: prefersReducedMotion()
        });
      transitionDispatcher.execute(plan, {
        operation: transitionOperations.SPECTRUM_EXIT,
        frame: targetFrame,
        isPlaying,
        animateTransition
      });
      return;
    }

    cancelSpectrumPresentationIfActive();
    setDisplayMode(false);
    clearSpectrumBars();

    if (targetFrame.scene === presentationApi.SCENES.NO_PLAYBACK &&
        presentationCoordinator.isTransitioning) {
      cancelActiveTransition();
    }

    if (animateTransition === false) {
      const plan = presentationPlanner.plan(
        presentationCoordinator.currentFrame,
        targetFrame,
        { animateTransition: false });
      transitionDispatcher.execute(plan, {
        operation: transitionOperations.LYRICS_FRAME,
        frame: targetFrame
      });
      return;
    }

    if (normalizedTrackId.length > 0 && normalizedTrackId !== lastTrackId) {
      resetForTrackSwitch(
        safeCurrent,
        safeNext,
        p,
        lineIndex,
        normalizedTrackId,
        wordScanProgress,
        safeCurrentTranslation,
        safeNextTranslation,
        useTranslationPair,
        targetFrame);
      return;
    }

    if (normalizedTrackId.length > 0) {
      lastTrackId = normalizedTrackId;
    }

    applyFrame(
      safeCurrent,
      safeNext,
      p,
      lineIndex,
      wordScanProgress,
      safeCurrentTranslation,
      safeNextTranslation,
      useTranslationPair,
      targetFrame);
  },

  setSpectrum(values) {
    if (!isSpectrumMode) {
      return;
    }

    if (Array.isArray(values) && values.length > 0) {
      ensureSpectrumBarCount(values.length);
    }
    setAudioDrivenSpectrum(values);
  },

  setSpectrumTuning(payload) {
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
  },

  setCover(dataUri, fallbackText, fallbackColor, diagnosticTrackId) {
    const uri = (dataUri ?? "").toString().trim();
    const text = toDisplayLine(fallbackText, "N").slice(0, 1).toUpperCase();
    const trackId = (diagnosticTrackId ?? "").toString();
    const generation = ++coverGeneration;
    clearCoverUpdateTimer();

    if (uri.length > 0 && uri === currentCoverUri) {
      setCoverLoadingState(false);
      return;
    }

    setCoverLoadingState(true);

    if (uri.length > 0) {
      const preloader = new Image();
      preloader.onload = () => {
        if (generation !== coverGeneration) {
          return;
        }

        crossfadeToCoverImage(uri, generation, () => setCoverLoadingState(false));
      };
      preloader.onerror = () => {
        if (generation !== coverGeneration) {
          return;
        }

        const mimeSeparatorIndex = uri.indexOf(";");
        const mime = uri.startsWith("data:") && mimeSeparatorIndex > 5
          ? uri.slice(5, mimeSeparatorIndex)
          : "";
        try {
          window.taskbarLyricsBridge.post("coverDecodeError", {
            trackId,
            mime,
            uriLength: uri.length,
            generation
          });
        } catch {
          // Diagnostics must not interrupt the fallback transition.
        }

        scheduleFallbackCoverUpdate(text, fallbackColor, () => {
          if (coverFallbackEl) {
            coverFallbackEl.style.display = "flex";
            coverFallbackEl.style.opacity = "1";
          }
          clearImageElement(activeCoverImageEl);
          clearImageElement(standbyCoverImageEl);
          currentCoverUri = "";
          setCoverLoadingState(false);
        });
      };
      window.setTimeout(() => {
        if (generation !== coverGeneration) {
          return;
        }

        preloader.src = uri;
      }, coverSwapDelayMs);
      return;
    }

    scheduleFallbackCoverUpdate(text, fallbackColor, () => {
      if (coverFallbackEl) {
        coverFallbackEl.style.display = "flex";
        coverFallbackEl.style.opacity = "1";
      }
      clearImageElement(activeCoverImageEl);
      clearImageElement(standbyCoverImageEl);
      currentCoverUri = "";
      setCoverLoadingState(false);
    });
  },

  applyStyle(payload) {
    if (!payload || typeof payload !== "object") {
      return;
    }

    // Map Han code points first; otherwise a Latin font that includes Han glyphs masks the Chinese selection.
    // 汉字单独映射，西文字体即使也有汉字字形，也不能遮住用户选择的中文字体。
    scriptFontStyleEl.textContent = `
      @font-face {
        font-family: "AF Media Bar CJK";
        src: local(${quoteCssFont(payload.cjkFontFamily)});
        unicode-range: U+2E80-2FFF, U+3000-303F, U+31C0-31EF, U+3400-4DBF, U+4E00-9FFF, U+F900-FAFF, U+20000-2FA1F;
      }
      @font-face {
        font-family: "AF Media Bar Latin";
        src: local(${quoteCssFont(payload.latinFontFamily)});
        unicode-range: U+0-10FFFF;
      }`;
    root.style.setProperty("--font-family", `"AF Media Bar CJK", "AF Media Bar Latin", ${payload.fontFamily || '"Segoe UI Variable Text", "Microsoft YaHei UI", sans-serif'}`);
    applyLyricsTextAlignment(payload.textAlignment);
    const layoutScalePercent = Number(payload.layoutScalePercent);
    if (Number.isFinite(layoutScalePercent) && layoutScalePercent > 0) {
      layoutScaleFactor = layoutScalePercent / 100;
    }
    requestedFontSize = Number(payload.fontSize) || 13;
    root.style.setProperty("--font-size", `${requestedFontSize}px`);
    root.style.setProperty("--font-weight", window.taskbarLyricsState.normalizeWeight(payload.fontWeight));
    // 字距用 em 下发：每一行按自己的字号解析，因此当前行、第二行与翻译行会等比缩放。
    // Character spacing is sent in em: every line resolves it against its own font size, so the current line, the second line,
    // and the translation row all scale proportionally.
    root.style.setProperty(
      "--letter-spacing",
      window.taskbarLyricsSpacing.letterSpacingEm(Number(payload.characterSpacingPercent))
    );
    const lineGapPercent = Number(payload.lineGapPercent);
    requestedLineGapPercent =
      Number.isFinite(lineGapPercent) && lineGapPercent > 0 ? lineGapPercent : 0;
    root.classList.toggle("cover-hidden", payload.showCover === false);

    const coverSize = Number(payload.coverSize);
    if (Number.isFinite(coverSize) && coverSize > 0) {
      root.style.setProperty("--cover-size", `${coverSize}px`);
    }
    const coverGap = Number(payload.coverGap);
    if (Number.isFinite(coverGap) && coverGap >= 0) {
      root.style.setProperty("--cover-gap", `${coverGap}px`);
    }
    const coverCornerRadius = Number(payload.coverCornerRadius);
    if (Number.isFinite(coverCornerRadius) && coverCornerRadius >= 0) {
      root.style.setProperty("--cover-radius", `${coverCornerRadius}px`);
    }

    const descenderBuffer = Number(payload.viewportDescenderBuffer);
    if (Number.isFinite(descenderBuffer) && descenderBuffer >= 0) {
      viewportDescenderBufferPx = descenderBuffer;
    }

    const pixelMetrics = {
      layoutHorizontalPadding: "--layout-padding-inline",
      lyricsPaneTopPadding: "--lyrics-pane-padding-top",
      lyricsPaneRightPadding: "--lyrics-pane-padding-right",
      lyricsPaneLeftPadding: "--lyrics-pane-padding-left",
      primaryOffsetY: "--primary-offset-y",
      secondaryOffsetY: "--secondary-offset-y",
      lineTextBottomPadding: "--line-text-padding-bottom",
      surfaceRadius: "--surface-radius",
      layerTransitionOffset: "--layer-transition-offset",
      coverFallbackFontSize: "--cover-fallback-font-size",
      spectrumWidth: "--spectrum-width",
      spectrumHeight: "--spectrum-height",
      spectrumGap: "--spectrum-gap",
      spectrumBarWidth: "--spectrum-bar-width",
      spectrumBarHeight: "--spectrum-bar-height",
      spectrumLowHeight: "--spectrum-low-height",
      spectrumHighHeight: "--spectrum-high-height",
      spectrumMiddleHeight: "--spectrum-middle-height"
    };
    Object.entries(pixelMetrics).forEach(([payloadKey, cssVariable]) => {
      const value = Number(payload[payloadKey]);
      if (Number.isFinite(value) && value >= 0) {
        root.style.setProperty(cssVariable, `${value}px`);
      }
    });
    const spectrumBarWidth = Number(payload.spectrumBarWidth);
    if (Number.isFinite(spectrumBarWidth) && spectrumBarWidth > 0) {
      requestedSpectrumBarWidthPx = spectrumBarWidth;
    }
    const spectrumGap = Number(payload.spectrumGap);
    if (Number.isFinite(spectrumGap) && spectrumGap >= 0) {
      requestedSpectrumGapPx = spectrumGap;
    }
    updateResponsiveLayout();

    if (payload.primaryColor && CSS.supports("color", payload.primaryColor)) {
      root.style.setProperty("--primary", payload.primaryColor);
    }

    if (payload.secondaryColor && CSS.supports("color", payload.secondaryColor)) {
      root.style.setProperty("--secondary", payload.secondaryColor);
    }

    if (payload.translationColor && CSS.supports("color", payload.translationColor)) {
      root.style.setProperty("--translation", payload.translationColor);
    }

    if (payload.wordScanOverlayColor && CSS.supports("color", payload.wordScanOverlayColor)) {
      root.style.setProperty("--word-scan-overlay", payload.wordScanOverlayColor);
    }

    if (payload.wordScanBaseColor && CSS.supports("color", payload.wordScanBaseColor)) {
      root.style.setProperty("--word-scan-base", payload.wordScanBaseColor);
    }

    if (payload.surfaceColor && CSS.supports("background-color", payload.surfaceColor)) {
      root.style.setProperty("--surface-color", payload.surfaceColor);
    }

    if (payload.surfaceShadow && CSS.supports("box-shadow", payload.surfaceShadow)) {
      root.style.setProperty("--surface-shadow", payload.surfaceShadow);
    }

    if (payload.textShadow && CSS.supports("text-shadow", payload.textShadow)) {
      root.style.setProperty("--text-shadow", payload.textShadow);
    }
  }
};

window.taskbarLyrics = {
  receive(message) {
    if (message?.version !== 1 || !message.type) return;
    const payload = message.payload;
    switch (message.type) {
      case "lyrics":
        lyricsApi.setLyrics(
          payload?.current,
          payload?.next,
          payload?.progress,
          payload?.currentLineIndex,
          payload?.trackId,
          payload?.isPureMusic,
          payload?.isPlaying,
          payload?.wordScanProgress,
          payload?.currentTranslation,
          payload?.nextTranslation,
          payload?.translationMode,
          payload?.animateTransition,
          payload?.scene);
        break;
      case "cover":
        lyricsApi.setCover(payload?.dataUri, payload?.fallbackText, payload?.fallbackColor, payload?.trackId);
        break;
      case "spectrum":
        lyricsApi.setSpectrum(payload);
        break;
      case "spectrumTuning":
        lyricsApi.setSpectrumTuning(payload);
        break;
      case "style":
        lyricsApi.applyStyle(payload);
        break;
    }
  }
};
