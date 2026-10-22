// 同一内嵌脚本的 DOM 引用及布局上下文；必须先于各呈现模块初始化。
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

let displayedCurrent = currentLineTextEl?.textContent || "";
let displayedNext = nextLineTextEl?.textContent || "";
let requestedFontSize = 13;
let compactPresentation = false;
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
// 过渡期间推广行的横向滚动进度：与擦亮进度分开存放，关掉擦亮后滚动照常工作。
let transitionScrollProgress = null;
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
const horizontalScrollAnchorRatio = 0.65;
const SEARCHING_TEXT = "\u6b63\u5728\u68c0\u7d22\u6b4c\u8bcd...";
const LEGACY_SEARCHING_TEXT = "\u6b63\u5728\u5339\u914d\u6b4c\u8bcd...";
let trackSwitchSearchTransitionActive = false;
let spectrumExitState = null;
let isSpectrumMode = false;

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

