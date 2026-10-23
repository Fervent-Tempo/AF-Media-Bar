// 歌词行和频谱场景过渡；沿用共享呈现协调器及代际。
function setTrackOffset(rowCount) {
  const offset = snapToPhysicalPixel(-linePitchPx * rowCount);
  if (offset === 0) {
    trackEl.style.removeProperty("transform");
    return;
  }

  trackEl.style.transform = `translateY(${offset}px)`;
}

function setIncomingTranslationPair(original, translation, wordScanProgress, scrollProgress) {
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
  setLineScrollProgress(incomingTranslationOriginalLineEl, scrollProgress, false);
}

function clearIncomingTranslationPair() {
  incomingTranslationPairEl.classList.remove("preparing", "entering", "no-anim");
  incomingTranslationPairEl.style.opacity = "";
  incomingTranslationPairEl.style.transform = "";
  setLineWordScanProgress(incomingTranslationOriginalLineEl, null);
  setLineScrollProgress(incomingTranslationOriginalLineEl, null);
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
    target.scrollProgress,
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
    frame.scrollProgress,
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
  setScrollProgress(normalized.scrollProgress, allowWordScanSmoothing);
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
  setScrollProgress(normalized.scrollProgress);
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
  setLineScrollProgress(nextLineEl, null);
  trackEl.classList.remove("word-scan-transition");
  transitionPromotedLine = "";
  transitionPromotedLineIndex = -1;
  transitionWordScanProgress = null;
  transitionScrollProgress = null;
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
  scrollProgress,
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
      searchFrame.scrollProgress,
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
  scrollProgress,
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
      updateTransitionScrollProgress(scrollProgress);
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

function finalizeTransition(
  promotedCurrent,
  upcomingNext,
  progress,
  promotedLineIndex = -1,
  wordScanProgress = null,
  scrollProgress = null,
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
  setScrollProgress(scrollProgress, false);
  setSecondaryLine(upcomingNext);
  setLineWordScanProgress(nextLineEl, null);
  setLineScrollProgress(nextLineEl, null);
  setIncomingLine("");
  trackEl.classList.remove("animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  trackEl.classList.remove("word-scan-transition");
  transitionPromotedLine = "";
  transitionPromotedLineIndex = -1;
  transitionWordScanProgress = null;
  transitionScrollProgress = null;
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
    wordScanProgress,
    scrollProgress
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
  scrollProgress = null,
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
      scrollProgress,
      resolvedTargetFrame);
    return;
  }

  startStandardTransition(newCurrent, newNext, progress, currentLineIndex, wordScanProgress, scrollProgress, resolvedTargetFrame);
}

function finalizeTranslationPairTransition(
  promotedCurrent,
  promotedTranslation,
  progress,
  promotedLineIndex,
  wordScanProgress,
  scrollProgress,
  targetFrame = null) {
  trackEl.classList.add("no-anim");
  incomingTranslationPairEl.classList.add("no-anim");
  stopTransitionOpacityAnimation();
  setCurrentLine(promotedCurrent);
  setWordScanProgress(wordScanProgress, false);
  setScrollProgress(scrollProgress, false);
  setSecondaryLine(promotedTranslation);
  setLineWordScanProgress(nextLineEl, null);
  setLineScrollProgress(nextLineEl, null);
  trackEl.classList.remove("translation-pair-animating", "animating", "word-scan-transition");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  transitionPromotedLine = "";
  transitionPromotedLineIndex = -1;
  transitionWordScanProgress = null;
  transitionScrollProgress = null;
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
    scrollProgress,
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
  scrollProgress,
  targetFrame = null) {
  transitionUsesTranslationPair = true;
  const promoted = toDisplayLine(newCurrent, SEARCHING_TEXT);
  const promotedTranslation = toDisplayLine(currentTranslation, " ");
  transitionPromotedLine = promoted;
  transitionPromotedLineIndex = currentLineIndex;
  transitionWordScanProgress = wordScanProgress;
  transitionScrollProgress = scrollProgress;
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
        transitionScrollProgress,
        targetFrame)
    });
  trackEl.classList.add("no-anim");
  trackEl.classList.remove("animating", "translation-pair-animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  setTrackOffset(0);
  clearIncomingTranslationPair();
  incomingTranslationPairEl.classList.add("preparing", "no-anim");
  setIncomingTranslationPair(promoted, promotedTranslation, wordScanProgress, scrollProgress);
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
    setSingleLineLayout(promotedTranslation);
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
  scrollProgress = null,
  targetFrame = null) {
  transitionUsesTranslationPair = false;
  const promoted = toDisplayLine(newCurrent, SEARCHING_TEXT);
  const upcoming = toDisplayLine(newNext, " ");
  transitionPromotedLine = promoted;
  transitionPromotedLineIndex = currentLineIndex;
  transitionWordScanProgress = wordScanProgress;
  transitionScrollProgress = scrollProgress;
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
        transitionScrollProgress,
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
  updateTransitionScrollProgress(scrollProgress, false);
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
    setSingleLineLayout(upcoming);
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
  setScrollProgress(null);
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

