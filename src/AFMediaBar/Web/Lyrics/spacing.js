/**
 * Web 歌词间距的纯计算：字距换算与双行行高的解析。
 *
 * 排版本身完全交给 CSS：字距就是行元素的 `letter-spacing`（用 em，随字号缩放），行距改变的是两行行框与
 * 两者之间的间隙。这里只回答"给定宿主高度与请求值，行高与间隙取多少才不会裁切文字"，保持无 DOM、无副作用，
 * 便于脱离界面单独验证。
 * Pure math for the web lyrics spacing: character-spacing conversion and two-line row metrics.
 *
 * Layout itself stays with CSS: character spacing is the line's `letter-spacing` (in em, so it scales with the font size),
 * and the line gap changes the two row boxes and the gap between them. This module only answers "given the host height and
 * the requested values, which row heights and gap never clip the text", with no DOM and no side effects so it can be
 * verified on its own.
 */
;(function (global) {
  const MIN_ROW_HEIGHT_PX = 1

  function clamp(value, minimum, maximum) {
    if (!Number.isFinite(value)) {
      return minimum
    }
    return Math.min(maximum, Math.max(minimum, value))
  }

  /**
   * 字距百分比 → CSS letter-spacing 值（em）。
   * Character-spacing percentage -> CSS letter-spacing value in em.
   */
  function letterSpacingEm(percent) {
    const normalized = clamp(Number(percent), 0, 100)
    return `${(normalized / 100).toFixed(4)}em`
  }

  /**
   * 解析双行行高与行距。
   *
   * - requestedGapPx <= 0：与升级前的公式逐位等价（行高取宿主一半的整数，行距是取整余量），默认外观不变。
   * - requestedGapPx > 0：额外间距加倍后作为行间距（两行各自向内收缩一半），因此两行的中心距恰好增加
   *   requestedGapPx；行高允许是小数（Chromium 亚像素排版），滑杆每一档都连续可见；夹取上限由
   *   minRowHeightPx（字号 ÷ 0.92，即字号夹取规则的下限）决定，行高不会低于它——文字既不会被裁切，
   *   字号也不会因为调大行距而被压缩。
   * Resolves the two-line row heights and gap.
   *
   * - requestedGapPx <= 0: bit-for-bit the formula used before this feature (row height is half the host, floored; the gap
   *   is the rounding remainder), so the default look is unchanged.
   * - requestedGapPx > 0: the extra separation is doubled into the row gap (each row shrinks by half of it), so the two text
   *   lines move apart by exactly requestedGapPx; row heights may be fractional (Chromium lays out sub-pixel), so every
   *   slider step stays visible. The clamp keeps every row at least minRowHeightPx (font size ÷ 0.92, the floor of the font
   *   clamp) tall, so neither is text clipped nor is the font shrunk by increasing the spacing.
   */
  function resolveRowMetrics(hostHeightPx, requestedGapPx, minRowHeightPx) {
    const host = Math.max(MIN_ROW_HEIGHT_PX, Number(hostHeightPx) || 0)
    const minRow = clamp(Number(minRowHeightPx), MIN_ROW_HEIGHT_PX, host / 2)
    // 升级前公式的取整余量（0 或 1 像素）是"零间距"基准；额外间距叠加在它之上，因此 0 与旧观感逐位一致，
    // 从 0 到第一档也连续。
    // The rounding remainder of the previous formula (zero or one pixel) is the zero-gap baseline; the extra separation is
    // added on top of it, so zero stays bit-identical to the old look and the step from zero is continuous.
    const baseExtra = Math.max(0, host - 2 * Math.floor(host / 2))
    const maxExtra = Math.max(baseExtra, host - 2 * minRow)
    const gap = clamp(Number(requestedGapPx), 0, Number.MAX_SAFE_INTEGER)
    const extra = gap <= 0 ? baseExtra : Math.min(baseExtra + 2 * gap, maxExtra)
    const rowHeightPx = Math.max(MIN_ROW_HEIGHT_PX, (host - extra) / 2)
    const rowGapPx = extra
    return {
      rowHeightPx,
      rowGapPx,
      linePitchPx: rowHeightPx + rowGapPx
    }
  }

  global.taskbarLyricsSpacing = { letterSpacingEm, resolveRowMetrics }
})(typeof window !== 'undefined' ? window : globalThis);
