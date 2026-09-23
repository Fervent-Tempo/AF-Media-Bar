(() => {
  window.taskbarLyricsState = {
    normalizeWeight(weight) {
      const raw = String(weight || "").trim().toLowerCase();
      const numeric = Number(raw);
      if (Number.isFinite(numeric) && numeric > 0) {
        return String(Math.min(900, Math.max(100, Math.round(numeric / 100) * 100)));
      }
      switch (raw) {
        case "light": return "300";
        case "medium": return "500";
        case "semibold": return "600";
        case "bold": return "700";
        default: return "500";
      }
    }
  };
})();
