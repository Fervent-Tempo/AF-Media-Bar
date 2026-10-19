// 封面加载和切换状态由本模块闭包独占。
const artworkPresentation = (() => {
const coverSwapDelayMs = 180;
const coverSwitchMinVisibleMs = 420;
let coverUpdateTimer = 0;
let coverStateTimer = 0;
let coverSwitchStartedAt = 0;
let activeCoverImageEl = coverImageEl;
let standbyCoverImageEl = coverImageNextEl;
let currentCoverUri = "";
let coverGeneration = 0;

// 封面双层切换、加载计时器和失败回退。
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

function setCover(dataUri, fallbackText, fallbackColor, diagnosticTrackId) {
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
  }


return { setCover, setCoverLoadingState };
})();
const { setCoverLoadingState } = artworkPresentation;
