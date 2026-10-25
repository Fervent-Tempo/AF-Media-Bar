// 从本项目自绘贝塞尔轮廓构造形变环；身体与球面眼睛各自投影，旋转不压扁整个角色。
(() => {
  "use strict";
  const count = 64, cx = 32, cy = 34, tau = Math.PI * 2;
  const pencil = Object.freeze({ halfWidth: 3.5, halfHeight: 13.5, offsetY: -1, tipAnchorY: 13.5, tipEndY: 16.5 });
  const source = document.getElementById("body").getAttribute("d");
  const tokens = source.match(/[MCZ]|-?\d+(?:\.\d+)?/g);
  const samples = [];
  let cursor = 0, x = 0, y = 0;
  while (cursor < tokens.length) {
    const command = tokens[cursor++];
    if (command === "M") { x = Number(tokens[cursor++]); y = Number(tokens[cursor++]); samples.push([x, y]); }
    else if (command === "C") {
      const controls = Array.from({ length: 6 }, () => Number(tokens[cursor++]));
      const fromX = x, fromY = y;
      for (let n = 1; n <= 12; n++) {
        const t = n / 12, u = 1 - t;
        samples.push([u ** 3 * fromX + 3 * u * u * t * controls[0] + 3 * u * t * t * controls[2] + t ** 3 * controls[4],
          u ** 3 * fromY + 3 * u * u * t * controls[1] + 3 * u * t * t * controls[3] + t ** 3 * controls[5]]);
      }
      [x, y] = controls.slice(4);
    } else if (command !== "Z") throw new Error("Unsupported authored pet contour");
  }
  const polar = samples.map(([px, py]) => ({ angle: (Math.atan2(py - cy, px - cx) + tau) % tau, radius: Math.hypot(px - cx, py - cy) }))
    .sort((a, b) => a.angle - b.angle);
  function radiusAt(angle) {
    const a = (angle + tau) % tau;
    let next = polar.findIndex(point => point.angle >= a);
    if (next < 0) next = 0;
    const before = polar[(next + polar.length - 1) % polar.length], after = polar[next];
    let low = before.angle, high = after.angle;
    if (next === 0) { if (a <= high) low -= tau; else high += tau; }
    const amount = high === low ? 0 : (a - low) / (high - low);
    return before.radius + (after.radius - before.radius) * amount;
  }
  const angles = Array.from({ length: count }, (_, n) => -Math.PI / 2 + n * tau / count);
  const radii = angles.map(radiusAt), vectors = angles.map(a => [Math.cos(a), Math.sin(a)]);
  const rest = vectors.map(([dx, dy], n) => [cx + dx * radii[n], cy + dy * radii[n]]);
  const points = rest.map(point => point.slice());
  function path(ring) {
    let result = `M${ring[0][0].toFixed(2)} ${ring[0][1].toFixed(2)}`;
    for (let n = 0; n < count; n++) {
      const a = ring[(n + count - 1) % count], b = ring[n], c = ring[(n + 1) % count], d = ring[(n + 2) % count];
      result += `C${(b[0] + (c[0] - a[0]) / 6).toFixed(2)} ${(b[1] + (c[1] - a[1]) / 6).toFixed(2)} ${(c[0] - (d[0] - b[0]) / 6).toFixed(2)} ${(c[1] - (d[1] - b[1]) / 6).toFixed(2)} ${c[0].toFixed(2)} ${c[1].toFixed(2)}`;
    }
    return result + "Z";
  }
  function update(mix, core, yawDegrees, dt, animate) {
    const yaw = yawDegrees * Math.PI / 180, front = Math.cos(yaw);
    const rate = animate ? 1 - Math.exp(-22 * dt) : 1;
    let moving = false, extent = 0;
    const target = core === "pencil" ? [pencil.halfWidth, pencil.halfHeight, pencil.offsetY] : core === "alert" ? [3.5, 10, -6] : core === "standby" ? [4, 4, 0] : core === "ball" ? [7, 7, 0] : [5.3, 5.3, 0];
    for (let n = 0; n < count; n++) {
      const [dx, dy] = vectors[n];
      const bodyRadius = radii[front >= 0 ? n : (count - n) % count];
      const radius = 26.5 + (bodyRadius - 26.5) * Math.abs(front);
      let smallRadius = 1 / Math.sqrt(dx * dx / (target[0] ** 2) + dy * dy / (target[1] ** 2));
      if (core === "pencil" || core === "alert") {
        const capOffset = target[1] - target[0], side = target[0] / Math.max(0.00001, Math.abs(dx));
        smallRadius = side * Math.abs(dy) <= capOffset ? side
          : capOffset * Math.abs(dy) + Math.sqrt(Math.max(0, target[0] ** 2 - capOffset * capOffset * dx * dx));
      }
      const r = radius + (smallRadius - radius) * mix;
      const goal = [cx + dx * r, cy + dy * r + target[2] * mix];
      for (let k = 0; k < 2; k++) {
        const difference = goal[k] - points[n][k];
        points[n][k] += Math.abs(difference) < 0.002 ? difference : difference * rate;
        moving ||= Math.abs(difference) >= 0.002;
      }
      extent = Math.max(extent, Math.hypot(points[n][0] - cx, points[n][1] - cy));
    }
    const atRest = mix < 0.0001 && Math.abs(Math.sin(yaw / 2)) < 0.001 && !moving;
    return { d: atRest ? source : path(points), moving, radius: extent };
  }
  function eye(x, y, yawDegrees, pitchDegrees) {
    const radius = 26.5, longitude = (x - cx) / radius, latitude = (y - cy) / radius;
    const depth = Math.sqrt(Math.max(0.01, 1 - longitude * longitude - latitude * latitude));
    const yaw = yawDegrees * Math.PI / 180, pitch = pitchDegrees * Math.PI / 180;
    const turnedX = longitude * Math.cos(yaw) + depth * Math.sin(yaw);
    const turnedZ = depth * Math.cos(yaw) - longitude * Math.sin(yaw);
    const turnedY = latitude * Math.cos(pitch) - turnedZ * Math.sin(pitch);
    const facing = latitude * Math.sin(pitch) + turnedZ * Math.cos(pitch);
    return { x: cx + turnedX * radius, y: cy + turnedY * radius,
      width: Math.max(0.025, Math.min(1.2, turnedZ / depth)), opacity: Math.max(0, Math.min(1, facing / 0.3)) };
  }
  function boundary(angle) {
    const position = (((angle + Math.PI / 2) / tau * count) % count + count) % count;
    const n = Math.floor(position), a = points[n], b = points[(n + 1) % count], mix = position - n;
    return [a[0] + (b[0] - a[0]) * mix - cx, a[1] + (b[1] - a[1]) * mix - cy];
  }
  window.PetGeometry = Object.freeze({ update, eye, boundary, pencil });
})();
