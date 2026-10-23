// 逐字进度、暂停恢复及长行横向滚动。
function normalizeWordScanProgress(value) {
  if (value === null || value === undefined || value === "") {
    return null;
  }

  const parsed = Number(value);
  return Number.isFinite(parsed) ? clamp01(parsed) : null;
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

function readInterpolatedScrollProgress(lineElement, targetProgress) {
  if (!lineElement?.classList.contains("line-scroll-smoothing") ||
      typeof window.getComputedStyle !== "function") {
    return null;
  }

  const inlineProgress = Number.parseFloat(
    lineElement.style.getPropertyValue("--line-scroll-progress")) / 100;
  if (!Number.isFinite(inlineProgress) || targetProgress === null ||
      targetProgress < inlineProgress - 0.02 ||
      targetProgress - inlineProgress > 0.35) {
    return null;
  }

  const computedValue = window.getComputedStyle(lineElement)
    .getPropertyValue("--line-scroll-progress");
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
      progress: readInterpolatedScrollProgress(activeLine, normalizedProgress) ?? normalizedProgress,
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
    lineElement.classList.remove("line-scroll-smoothing");
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
  // 滚动偏移以行上独立存放的滚动进度为准：擦亮关闭时 --word-scan-progress 不存在，
  // 但滚动必须照常重算，否则 resize 之后长行会停在不随演唱进度移动的位置。
  // The scroll offset is recomputed from the line's dedicated scroll progress: with the highlight off there is no
  // --word-scan-progress, yet the scroll must still be recomputed, or a long line would stop following the song.
  const progress = Number.parseFloat(
    lineElement.style.getPropertyValue("--line-scroll-progress")) / 100;
  updateLineHorizontalScroll(lineElement, Number.isFinite(progress) ? clamp01(progress) : null);
}

// 擦亮层：只写高亮视觉（类名与裁剪变量），不再驱动横向滚动。
function applyLineWordScanVisual(lineElement, normalized, allowSmoothing) {
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
    return;
  }

  lineElement.style.setProperty("--word-scan-progress", `${(normalized * 100).toFixed(3)}%`);
}

function setLineWordScanProgress(lineElement, progress, allowSmoothing = true) {
  applyLineWordScanVisual(
    lineElement,
    resolveWordScanProgress(lineElement, normalizeWordScanProgress(progress)),
    allowSmoothing);
}

// 横向滚动层：由独立透传的时间轴进度驱动，与擦亮是否开启无关；进度同时写到行上的
// --line-scroll-progress，供布局变化后重算偏移时读取。
function setLineScrollProgress(lineElement, progress, allowSmoothing = true) {
  const normalized = resolveWordScanProgress(lineElement, normalizeWordScanProgress(progress));
  const previousProgress = Number.parseFloat(
    lineElement.style.getPropertyValue("--line-scroll-progress")) / 100;
  const isContinuousProgress = Number.isFinite(previousProgress) &&
    normalized !== null &&
    normalized >= previousProgress - 0.02 &&
    normalized - previousProgress <= 0.35;
  lineElement.classList.toggle(
    "line-scroll-smoothing",
    normalized !== null && normalized > 0 && normalized < 1 &&
      isPlaybackPlaying && !prefersReducedMotion() && allowSmoothing && isContinuousProgress);
  if (normalized === null) {
    lineElement.style.removeProperty("--line-scroll-progress");
  } else {
    lineElement.style.setProperty("--line-scroll-progress", `${(normalized * 100).toFixed(3)}%`);
  }

  updateLineHorizontalScroll(lineElement, normalized);
}

function setWordScanProgress(progress, allowSmoothing = true) {
  setLineWordScanProgress(currentLineEl, progress, allowSmoothing);
}

function setScrollProgress(progress, allowSmoothing = true) {
  setLineScrollProgress(currentLineEl, progress, allowSmoothing);
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

function updateTransitionScrollProgress(progress, allowSmoothing = true) {
  transitionScrollProgress = progress;
  const targetLine = transitionUsesTranslationPair
    ? incomingTranslationOriginalLineEl
    : nextLineEl;
  setLineScrollProgress(targetLine, progress, allowSmoothing);
}

