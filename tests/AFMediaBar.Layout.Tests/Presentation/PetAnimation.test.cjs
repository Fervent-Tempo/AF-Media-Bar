// 执行真实内嵌脚本，验证暂停、旧消息与局部/全局反馈，不断言精确动画帧或像素。
const { test } = require("node:test");
const assert = require("node:assert/strict");
const vm = require("node:vm");
const fs = require("node:fs");
const path = require("node:path");
const source = name => fs.readFileSync(path.join(__dirname, "../../../src/AFMediaBar/Web/Pet/", name), "utf8");
const script = ["geometry.js", "motion.js", "effects.js", "pet.js"].map(source).join("\n");

function page() {
  let time = 0, serial = 0, receiver;
  const frames = new Map(), timers = new Map(), nodes = new Map();
  const make = () => ({ attributes: {}, style: {}, setAttribute(key, value) { this.attributes[key] = value; }, getAttribute(key) { return this.attributes[key]; }, children: [] });
  for (const match of source("index.html").matchAll(/id="([^"]+)"/g)) nodes.set(match[1], make());
  for (let n = 0; n < 16; n++) nodes.set("particle-" + n, make());
  const contour = fs.readFileSync(path.join(__dirname, "../../../src/AFMediaBar/Components/MediaBar/PetVisualDefinition.cs"), "utf8").match(/BodyPath = "([^"]+)"/)[1];
  nodes.get("body").setAttribute("d", contour);
  const classes = new Set();
  const document = {
    hidden: false,
    documentElement: { ...make(), classList: { toggle(key, enabled) { if (enabled) classes.add(key); else classes.delete(key); } } },
    getElementById: key => nodes.get(key),
    addEventListener() {}
  };
  const context = {
    document, performance: { now: () => time },
    window: { addEventListener() {}, matchMedia: () => ({ matches: false, addEventListener() {} }),
      chrome: { webview: { addEventListener: (_, handler) => { receiver = handler; }, postMessage() {} } } },
    requestAnimationFrame: callback => { const id = ++serial; frames.set(id, callback); return id; },
    cancelAnimationFrame: id => frames.delete(id),
    setTimeout: callback => { const id = ++serial; timers.set(id, callback); return id; },
    clearTimeout: id => timers.delete(id)
  };
  vm.runInNewContext(script, context);
  let state = { gazeX: 0, gazeY: 0, hovered: false, blinkSequence: 0, pressSequence: 0,
    bodyColor: "#FFFFFF", eyeColor: "#1C1C1C", highContrast: false, idleAnimationsEnabled: true, motion: 0, active: true, animation: "auto" };
  return {
    nodes, classes, frames, timers, document, contour, engine: context.window.PetMotion, geometry: context.window.PetGeometry,
    receive(change = {}, initial = false) { state = { ...state, ...change }; receiver({ data: { type: "state", id: ++serial, initial, state } }); },
    advance(milliseconds) {
      const end = time + milliseconds;
      while (time < end) {
        time = Math.min(end, time + 16);
        const callbacks = [...frames.values()]; frames.clear();
        callbacks.forEach(callback => callback(time));
      }
    }
  };
}

test("global blink does not squash the body; local press does", () => {
  const p = page();
  p.receive({ idleAnimationsEnabled: false }, true);
  p.receive({ blinkSequence: 1 });
  p.advance(80);
  assert.match(p.nodes.get("lids").attributes.transform, /scale\(1 0\.08\)/);
  assert.match(p.nodes.get("pose").attributes.transform, /scale\(1 1\)/);
  p.receive({ pressSequence: 1 });
  p.advance(80);
  assert.doesNotMatch(p.nodes.get("breath").attributes.transform, /scale\(1 1\)/);
});

test("pause stops work and resumed initial state never replays old clicks", () => {
  const p = page();
  p.receive({}, true);
  assert.equal(p.timers.size, 1);
  p.receive({ active: false, blinkSequence: 50, pressSequence: 50 });
  assert.equal(p.frames.size, 0);
  assert.equal(p.timers.size, 0);
  assert.equal(p.classes.has("active"), false);
  p.receive({ active: true, idleAnimationsEnabled: false }, true);
  p.advance(32);
  assert.match(p.nodes.get("lids").attributes.transform, /scale\(1 1\)/);
  assert.match(p.nodes.get("pose").attributes.transform, /scale\(1 1\)/);
  p.receive({ idleAnimationsEnabled: false });
  assert.equal(p.timers.size, 0);
});

test("automatic cycling stays within lifecycle and reactions; morphs require selection", () => {
  const p = page();
  p.receive({}, true);
  const seen = new Set();
  const manualOnly = new Set(["orbit", "radar", "progress", "spawning", "humming", "loading", "dictating", "writing",
    "sending", "receiving", "uploading", "notifying", "alerting", "dragging", "bouncing", "powering-down"]);
  for (let n = 0; n < 130; n++) {
    p.advance(1000);
    const id = p.document.documentElement.attributes["data-animation"];
    assert.equal(manualOnly.has(id), false, id);
    seen.add(id);
  }
  assert.equal(seen.size, 23);
  p.receive({ animation: "radar" });
  p.advance(1000);
  assert.equal(p.document.documentElement.attributes["data-animation"], "radar");
  assert.equal(Number(p.nodes.get("character").attributes.opacity), 1);
  assert.notEqual(p.nodes.get("body").getAttribute("d"), p.contour);
  assert.equal(p.nodes.get("fx-radar").style.display, "inline");
});

test("all selectable motions remain finite and a rapid change uses the latest state", () => {
  const p = page();
  p.receive({}, true);
  const catalog = fs.readFileSync(path.join(__dirname, "../../../src/AFMediaBar/Classes/Settings/PetAnimationCatalog.cs"), "utf8");
  const ids = [...catalog.matchAll(/"([a-z-]+)"/g)].map(match => match[1]);
  assert.equal(ids.length, 46);
  for (const animation of ids) {
    p.receive({ animation });
    p.advance(2200);
    if (animation !== "auto") assert.equal(p.document.documentElement.attributes["data-animation"], animation);
    for (const node of p.nodes.values()) for (const value of Object.values(node.attributes))
      assert.doesNotMatch(String(value), /NaN|Infinity/, animation);
  }
  p.receive({ animation: "sleeping" });
  p.receive({ animation: "loading" });
  p.receive({ animation: "happy" });
  p.advance(32);
  assert.equal(p.document.documentElement.attributes["data-animation"], "happy");
  p.receive({ active: false });
  assert.equal(p.frames.size, 0);
  assert.equal(p.timers.size, 0);
});

test("multi-turn targets keep moving forward when the spring lags and the cycle wraps", () => {
  const p = page(), turn = { raw: 0, offset: 0 };
  assert.equal(p.engine.unwrapTurn(turn, 0, 0, true), 0);
  assert.equal(p.engine.unwrapTurn(turn, 720, 180, false), 720);
  assert.equal(p.engine.unwrapTurn(turn, 2160, 1600, false), 2160);
  assert.equal(p.engine.unwrapTurn(turn, 0, 1900, false), 2160);
  assert.equal(p.engine.unwrapTurn(turn, 90, 2000, false), 2250);
  assert.equal(p.engine.unwrapTurn(turn, 0, 2200, true), 2160);
  assert.equal(p.engine.unwrapTurn(turn, 2160, 720, true), 720, "restoring motion must not add historical turns");
});

test("pause preserves effect phase and rapid reselection starts a coherent new cycle", () => {
  const p = page();
  p.receive({ animation: "progress" }, true); p.advance(1800);
  p.receive({ active: false }); p.advance(4000);
  p.receive({ active: true }, true); p.advance(1200);
  assert.ok(Number(p.nodes.get("fx-progress").getAttribute("opacity")) < 0.6, "the body and effect must enter their rest phase together");
  p.receive({ animation: "orbit" }); p.advance(100);
  p.receive({ animation: "progress" }); p.advance(32);
  assert.ok(Number(p.nodes.get("progress-arc").getAttribute("stroke-dasharray").split(" ")[0]) < 10);
});

test("wink has a continuous closing and opening envelope", () => {
  const p = page();
  assert.ok(p.engine.target("playful", 4.5001).winkLid > 0.99);
  assert.ok(p.engine.target("playful", 4.65).winkLid < 0.2);
  assert.ok(p.engine.target("playful", 4.8499).winkLid > 0.99);
});

test("spinning keeps body volume and hides each eye at its own horizon", () => {
  const p = page();
  for (const yaw of [0, 90, 180, 270]) {
    const shape = p.geometry.update(0, "dot", yaw, 0.1, false);
    assert.ok(shape.radius > 25, "the whole body must not collapse into a thin strip");
    assert.doesNotMatch(shape.d, /NaN|Infinity/);
  }
  assert.ok(p.geometry.eye(24, 32, 90, 0).opacity > 0.5);
  assert.equal(p.geometry.eye(40, 32, 90, 0).opacity, 0);
});

test("fixed moods vary their eyes and waking finishes without restarting", () => {
  const p = page();
  p.receive({ animation: "happy" }, true); p.advance(900);
  const happy = p.nodes.get("eye-left").getAttribute("d");
  p.advance(3100);
  assert.notEqual(p.nodes.get("eye-left").getAttribute("d"), happy);
  p.receive({ animation: "waking" }); p.advance(3500);
  const awake = p.nodes.get("eye-left").getAttribute("d");
  p.advance(5400);
  assert.equal(p.nodes.get("eye-left").getAttribute("d"), awake);
});

test("reduced motion disables idle and body movement while preserving click feedback", () => {
  const p = page();
  p.receive({ motion: 1 }, true);
  assert.equal(p.timers.size, 0);
  assert.equal(p.classes.has("full"), false);
  p.receive({ hovered: true, pressSequence: 1, blinkSequence: 1, gazeX: 4 });
  p.advance(80);
  assert.match(p.nodes.get("pose").attributes.transform, /scale\(1 1\)/);
  assert.match(p.nodes.get("gaze").attributes.transform, /translate\(4 0\)/);
  assert.match(p.nodes.get("lids").attributes.transform, /scale\(1 0\.08\)/);
});

// 律动输入必须遵守系统减少动画设置，并能恢复静止。
test("music pulse moves the pet, settles, and respects reduced motion", () => {
  const p = page();
  p.receive({ animation: "idle", idleAnimationsEnabled: false, beat: 1 }, true);
  p.advance(700);
  assert.doesNotMatch(p.nodes.get("pose").attributes.transform, /scale\(1 1\)/);
  p.receive({ beat: 0 });
  p.advance(4000);
  assert.match(p.nodes.get("pose").attributes.transform, /scale\(1 1\)/);
  p.receive({ beat: 1, motion: 2 });
  p.advance(100);
  assert.match(p.nodes.get("pose").attributes.transform, /scale\(1 1\)/);
});

// 同名装扮会被 getElementById 绑定到眼睑层，必须由真实模板验证。
test("outfit exists once outside gaze and eyelids", () => {
  const html = source("index.html");
  const ids = [...html.matchAll(/id="([^"]+)"/g)].map(match => match[1]);
  assert.equal(new Set(ids).size, ids.length);
  assert.match(html, /id="eye-right"[\s\S]*?<\/g><\/g><\/g>\s*<g id="outfit"/);
});


test("music poses never spin, beats nod, and pause lowers only the right ear cup", () => {
  const p = page();
  for (const animation of ["music-quiet", "music-playing", "music-paused"]) {
    p.receive({ animation, outfit: "headphones", headphoneOff: animation === "music-paused", beat: 0 }, true);
    for (let t = 0; t < 20; t += 0.25) {
      const pose = p.engine.target(animation, t);
      assert.equal(pose.spinY, 0);
      assert.ok(Math.abs(pose.turn) <= 1.8);
      assert.equal(pose.morph, 0);
      p.advance(250);
    }
    assert.equal(p.document.documentElement.attributes["data-animation"], animation);
    const ear = p.nodes.get("outfit-ear-right").attributes.transform;
    assert.match(ear, animation === "music-paused" ? /translate\(2 8\)/ : /translate\(0 0\)/);
  }
  p.receive({ animation: "music-playing", headphoneOff: false });
  p.advance(1500);
  const resting = p.nodes.get("pose").attributes.transform;
  p.receive({ beat: 1 }); p.advance(160);
  assert.notEqual(p.nodes.get("pose").attributes.transform, resting);
  p.receive({ motion: 1, headphoneOff: true, animation: "music-paused", beat: 0 });
  p.advance(32);
  assert.match(p.nodes.get("outfit-ear-right").attributes.transform, /translate\(2 8\)/);
  assert.match(p.nodes.get("pose").attributes.transform, /rotate\(0\)/);
});
