// 接收宿主协议并协调布局、样式与呈现模块；模块按 LyricsWebViewRenderer 中的顺序内嵌。
let hasAppliedColorStyle = false;

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

function toDisplayLine(line, fallback = " ") {
  const text = (line ?? "").toString();
  // 格式清理后的显式空行保留空白，只有缺失文本才使用兜底文案。
  return text.length > 0 ? text.trim() || " " : fallback;
}

function resolveTranslationDisplay(line) {
  const text = (line ?? "").toString().trim();
  return {
    text: text.length > 0 ? text : " ",
    isPlaceholder: text.length === 0
  };
}

function setSingleLineLayout(secondary) {
  layoutEl.classList.toggle("single-line", (secondary ?? "").toString().trim().length === 0);
}

function snapToPhysicalPixel(value) {
  const devicePixelRatio = Number(window.devicePixelRatio) || 1;
  return Math.round(value * devicePixelRatio) / devicePixelRatio;
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
  setSingleLineLayout(safe);
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
  scrollProgress,
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
    scrollProgress,
    currentTranslation,
    nextTranslation,
    translationMode
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
  const hostHeight = compactPresentation ? Math.max(1, measuredViewportHeight - viewportDescenderBufferPx) :
    Math.max(minimumHostHeight, measuredViewportHeight - viewportDescenderBufferPx);
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
  const rowMetrics = compactPresentation
    ? { rowHeightPx: hostHeight, rowGapPx: 0, linePitchPx: hostHeight }
    : window.taskbarLyricsSpacing.resolveRowMetrics(hostHeight, requestedGapPx, minimumRowHeightPx);
  rowHeightPx = rowMetrics.rowHeightPx;
  rowGapPx = rowMetrics.rowGapPx;
  linePitchPx = rowMetrics.linePitchPx;
  const currentSizeMax = compactPresentation ? rowHeightPx * 0.92 :
    Math.max(11.2 * layoutScaleFactor, rowHeightPx * 0.92);
  currentSize = Math.min(requestedFontSize, currentSizeMax);
  const nextSize = Math.max(9 * layoutScaleFactor, currentSize * 0.92);
  root.style.setProperty("--row-height", `${rowHeightPx}px`);
  root.style.setProperty("--row-gap", `${rowGapPx}px`);
  root.style.setProperty("--line-pitch", `${linePitchPx}px`);
  root.style.setProperty("--current-size", `${currentSize.toFixed(2)}px`);
  root.style.setProperty("--next-size", `${nextSize.toFixed(2)}px`);
  // Keep row boxes intact for rolling; independent translation centers a lone row without changing its roll distance.
  const rootStyle = window.getComputedStyle(root);
  const paneTop = Number.parseFloat(rootStyle.getPropertyValue("--lyrics-pane-padding-top")) || 0;
  const primaryOffset = Number.parseFloat(rootStyle.getPropertyValue("--primary-offset-y")) || 0;
  const singleLineOffset = snapToPhysicalPixel((layoutEl.clientHeight - rowHeightPx) / 2 - paneTop - primaryOffset);
  root.style.setProperty("--single-line-offset", `${singleLineOffset}px`);
  setTrackOffset(0);
  refreshLineHorizontalScroll(currentLineEl);
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
    scrollProgress,
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
    // 冻结与恢复追赶以滚动进度为准：它不受擦亮开关影响，关掉擦亮后暂停、恢复的
    // 滚动轨迹与开启时完全一致。
    // Freeze and resume catch-up track the scroll progress: it is independent of the highlight switch, so the
    // scroll trajectory while paused and resuming stays identical with the highlight off.
    updateWordScanFreezeState(
      wasPlaying,
      nextIsPlaying,
      normalizedTrackId,
      lineIndex,
      scrollProgress);
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
      scrollProgress,
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
        scrollProgress,
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
      scrollProgress,
      safeCurrentTranslation,
      safeNextTranslation,
      useTranslationPair,
      targetFrame);
  },

  setSpectrum: spectrumPresentation.setSpectrum,

  setSpectrumTuning: spectrumPresentation.setSpectrumTuning,

  setCover: artworkPresentation.setCover,

  applyStyle(payload) {
    if (!payload || typeof payload !== "object") {
      return;
    }

    const colorDuration = Number(payload.colorTransitionMilliseconds);
    const transitionDuration = hasAppliedColorStyle && Number.isFinite(colorDuration)
      ? Math.max(0, Math.min(220, colorDuration)) : 0;
    root.style.setProperty("--color-transition-duration", `${transitionDuration}ms`);
    hasAppliedColorStyle = true;

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
    compactPresentation = payload.compact === true;
    root.style.setProperty("--lyrics-pane-padding-top", payload.compact ? "0px" : "3px");
    root.style.setProperty("--line-text-padding-bottom", payload.compact ? "0px" : "2px");
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
        if (window.taskbarVerticalText.present(payload)) break;
        lyricsApi.setLyrics(
          payload?.current,
          payload?.next,
          payload?.progress,
          payload?.currentLineIndex,
          payload?.trackId,
          payload?.isPureMusic,
          payload?.isPlaying,
          payload?.wordScanProgress,
          payload?.scrollProgress,
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
        window.taskbarVerticalText.applyStyle(payload);
        break;
    }
  }
};
