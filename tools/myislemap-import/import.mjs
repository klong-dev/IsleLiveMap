import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, writeFileSync } from "node:fs";
import { basename, dirname, join, resolve } from "node:path";
import vm from "node:vm";

const root = resolve(import.meta.dirname, "../..");
const rawDir = join(root, "tools/myislemap-import/snapshot/raw");
const sourceIconDir = join(root, "src/TheIsleOverlay.App/Assets/MapLayers/Icons/source");
const pngIconDir = join(root, "src/TheIsleOverlay.App/Assets/MapLayers/Icons/png");
const outputPath = join(root, "src/TheIsleOverlay.App/Assets/GatewayMapLayers.json");
const renderDir = join(root, "tools/myislemap-import/snapshot/render");

const sources = Object.freeze({
  mapData: "https://myislemap.com/map-data.js?v=53",
  aiSpawn: "https://myislemap.com/map-ai-spawn-zones.js?v=2",
  roads: "https://myislemap.com/map-roads.js?v=2",
  water: "https://myislemap.com/map-water.js?v=2"
});
const allowedOrigin = "https://myislemap.com";

for (const directory of [rawDir, sourceIconDir, pngIconDir, renderDir]) mkdirSync(directory, { recursive: true });

async function download(url) {
  const parsed = new URL(url);
  if (parsed.origin !== allowedOrigin) throw new Error(`Blocked source origin: ${parsed.origin}`);
  const response = await fetch(parsed, { redirect: "error" });
  if (!response.ok) throw new Error(`${url} returned HTTP ${response.status}`);
  return Buffer.from(await response.arrayBuffer());
}

function hash(bytes) {
  return createHash("sha256").update(bytes).digest("hex").toUpperCase();
}

function evaluate(source, filename) {
  const context = { window: {} };
  vm.createContext(context);
  vm.runInContext(source, context, { filename, timeout: 5_000 });
  return context.window;
}

function mapPoint(x, y) {
  return { left: Number((x / 1000).toFixed(8)), top: Number((y / 1003).toFixed(8)) };
}

function worldPoint(gameX, gameY) {
  return {
    left: Number((((gameY / 1000 + 505) / 1112)).toFixed(8)),
    top: Number((((gameX / 1000 + 607) / 1116)).toFixed(8))
  };
}

function polygonPoints(value) {
  return String(value).trim().split(/\s+/).map(pair => {
    const [x, y] = pair.split(",").map(Number);
    return mapPoint(x, y);
  });
}

function circlePoints(cx, cy, radius, segments = 48) {
  return Array.from({ length: segments }, (_, index) => {
    const angle = Math.PI * 2 * index / segments;
    return mapPoint(cx + Math.cos(angle) * radius, cy + Math.sin(angle) * radius);
  });
}

function slug(value) {
  return String(value).normalize("NFD").replace(/[\u0300-\u036f]/g, "")
    .toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "");
}

function chromePath() {
  const candidates = [
    process.env.CHROME_PATH,
    "C:/Program Files/Google/Chrome/Application/chrome.exe",
    "C:/Program Files (x86)/Google/Chrome/Application/chrome.exe",
    "C:/Program Files/Microsoft/Edge/Application/msedge.exe"
  ].filter(Boolean);
  return candidates.find(candidate => {
    return existsSync(candidate);
  });
}

function convertSvgToPng(sourcePath, pngPath, key) {
  // ffmpeg builds on Windows frequently omit the SVG demuxer. Keep the
  // conversion deterministic by preferring ffmpeg when it supports SVG and
  // falling back to the installed Chromium renderer. The fallback only reads
  // the audited local SVG and never fetches remote content.
  try {
    execFileSync("ffmpeg", ["-hide_banner", "-loglevel", "error", "-y", "-i", sourcePath,
      "-vf", "scale=64:64:force_original_aspect_ratio=decrease:flags=lanczos", pngPath],
      { stdio: "ignore" });
    return;
  } catch {
    const browser = chromePath();
    if (!browser) {
      throw new Error(`Unable to rasterize ${key}. Install ffmpeg with SVG support or set CHROME_PATH.`);
    }
    const htmlPath = join(renderDir, `${key}.html`);
    const fileUrl = `file:///${sourcePath.replaceAll("\\", "/").replaceAll(" ", "%20")}`;
    writeFileSync(htmlPath,
      `<!doctype html><meta charset="utf-8"><style>html,body{margin:0;width:64px;height:64px;background:transparent;overflow:hidden}img{display:block;width:64px;height:64px;object-fit:contain}</style><img src="${fileUrl}">`);
    execFileSync(browser, ["--headless", "--disable-gpu", "--no-sandbox", "--hide-scrollbars",
      "--window-size=64,64", `--screenshot=${pngPath}`, `file:///${htmlPath.replaceAll("\\", "/").replaceAll(" ", "%20")}`],
      { stdio: "ignore", windowsHide: true });
  }
}

const downloaded = {};
const provenanceSources = [];
for (const [key, url] of Object.entries(sources)) {
  const bytes = await download(url);
  downloaded[key] = bytes.toString("utf8").replace(/^\uFEFF/, "");
  writeFileSync(join(rawDir, `${key}.js`), bytes);
  provenanceSources.push({ key, url, sha256: hash(bytes), bytes: bytes.length });
}

const mapWindow = evaluate(downloaded.mapData, "map-data.js");
const aiWindow = evaluate(downloaded.aiSpawn, "map-ai-spawn-zones.js");
const roadWindow = evaluate(downloaded.roads, "map-roads.js");
const waterWindow = evaluate(downloaded.water, "map-water.js");
const overlays = mapWindow.MAP_OVERLAYS;

const zones = [];
for (const kind of ["migration", "patrol", "sanctuary"]) {
  overlays[kind].zones.forEach((zone, index) => zones.push({
    id: `${kind}-${slug(zone.gameLabel || zone.label)}-${index}`,
    name: zone.label,
    gameLabel: zone.gameLabel ?? null,
    kind,
    points: zone.type === "circle"
      ? circlePoints(zone.cx, zone.cy, zone.r)
      : polygonPoints(zone.points)
  }));
}

const aiSpawnZones = aiWindow.MAP_AI_SPAWN_ZONES.map((zone, index) => ({
  id: `ai-zone-${index}-${slug(zone.label)}`,
  name: zone.label,
  speciesKeys: [...new Set((zone.configs ?? []).map(config => slug(config.name)))],
  configs: (zone.configs ?? []).map(config => ({
    name: config.name,
    maximum: config.max ?? null,
    minimumDistance: config.minimumDistance ?? null,
    respawnSeconds: config.respawnTime ?? null
  })),
  points: (zone.points?.length >= 3 ? zone.points : [zone.location])
    .filter(Boolean)
    .map(point => worldPoint(Number(point.y), Number(point.x)))
}));

const routes = roadWindow.MAP_ROADS.map((route, index) => ({
  id: `route-${index}-${slug(route.label)}`,
  name: route.label,
  kind: route.type || "road",
  points: route.points.map(point => worldPoint(Number(point.x), Number(point.y)))
}));

const waterLabels = waterWindow.MAP_WATER_LABELS.map((item, index) => ({
  id: `water-${index}-${slug(item.label.replace(/<[^>]+>/g, " "))}`,
  name: item.label.replace(/<br\s*\/?>/gi, " ").replace(/<[^>]+>/g, "").trim(),
  point: worldPoint(Number(item.x), Number(item.y))
}));

const resourceCategories = { animals: "animals", herbs: "plants", earth: "earth" };
const fallbackIcons = { plants: "assets/icons/leaf.svg" };
const resources = [];
const iconReferences = new Map();
for (const [sourceCategory, category] of Object.entries(resourceCategories)) {
  overlays[sourceCategory].forEach((item, index) => {
    const iconUrl = item.emoji || fallbackIcons[category] || "";
    const iconKey = basename(iconUrl, ".svg") || item.key;
    if (iconUrl) iconReferences.set(iconKey, iconUrl);
    resources.push({
      id: `${category}-${index}-${slug(item.key)}-${Number(item.x).toFixed(2)}-${Number(item.y).toFixed(2)}`,
      category,
      key: item.key,
      name: item.name,
      group: item.group ?? null,
      iconKey,
      point: mapPoint(Number(item.x), Number(item.y)),
      source: item.source ?? null,
      updated: item.updated ?? null
    });
  });
}

const icons = [];
for (const [key, relativeUrl] of [...iconReferences.entries()].sort(([left], [right]) => left.localeCompare(right))) {
  const url = new URL(relativeUrl, `${allowedOrigin}/`).href;
  const bytes = await download(url);
  const svg = bytes.toString("utf8");
  if (/<script\b|\bon\w+\s*=|(?:href|src)\s*=\s*["'](?:https?:|\/\/|data:)/i.test(svg)) {
    throw new Error(`Unsafe SVG content: ${url}`);
  }
  const sourcePath = join(sourceIconDir, `${key}.svg`);
  const pngPath = join(pngIconDir, `${key}.png`);
  writeFileSync(sourcePath, bytes);
  convertSvgToPng(sourcePath, pngPath, key);
  icons.push({
    key,
    sourceUrl: url,
    sourceSha256: hash(bytes),
    sourceAsset: `Assets/MapLayers/Icons/source/${key}.svg`,
    runtimeAsset: `Assets/MapLayers/Icons/png/${key}.png`
  });
}

const catalog = {
  schemaVersion: 2,
  mapId: "gateway",
  coordinateSpace: "normalized-gateway",
  provenance: {
    provider: "myislemap.com",
    snapshotMode: "build-time-offline",
    retrievedAt: new Date().toISOString(),
    sources: provenanceSources,
    counts: {
      migrationZones: zones.filter(zone => zone.kind === "migration").length,
      patrolZones: zones.filter(zone => zone.kind === "patrol").length,
      sanctuaryZones: zones.filter(zone => zone.kind === "sanctuary").length,
      aiSpawnZones: aiSpawnZones.length,
      routes: routes.length,
      routePoints: routes.reduce((count, route) => count + route.points.length, 0),
      waterLabels: waterLabels.length,
      animalResources: resources.filter(item => item.category === "animals").length,
      plantResources: resources.filter(item => item.category === "plants").length,
      earthResources: resources.filter(item => item.category === "earth").length,
      icons: icons.length
    }
  },
  defaults: {
    migration: true,
    patrol: true,
    sanctuary: true,
    aiSpawnZones: false,
    roads: true,
    water: false,
    animals: false,
    plants: false,
    earth: false,
    selectedResourceKeys: Object.fromEntries(Object.values(resourceCategories)
      .map(category => [category, [...new Set(resources.filter(item => item.category === category).map(item => item.key))].sort()]))
  },
  zones,
  aiSpawnZones,
  routes,
  waterLabels,
  resources,
  icons
};

const expected = catalog.provenance.counts;
const required = {
  migrationZones: 12, patrolZones: 61, sanctuaryZones: 7, aiSpawnZones: 52,
  routes: 32, routePoints: 876, waterLabels: 28,
  animalResources: 430, plantResources: 245, earthResources: 278
};
for (const [key, value] of Object.entries(required)) {
  if (expected[key] !== value) throw new Error(`Unexpected ${key}: ${expected[key]} (expected ${value})`);
}

writeFileSync(outputPath, `${JSON.stringify(catalog, null, 2)}\n`);
console.log(`Wrote ${outputPath}`);
console.log(JSON.stringify(catalog.provenance.counts, null, 2));
