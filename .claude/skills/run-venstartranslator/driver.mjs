// Launches VenstarTranslator with throwaway state, drives the web UI in
// headless Chromium, writes screenshots, and tears everything down.
//
// Usage:  node .claude/skills/run-venstartranslator/driver.mjs [--keep]
//   --keep   leave the app + stub running after the smoke (prints how to stop)
//
// State/logs/screenshots: /tmp/venstartranslator-run/  (wiped on each run)
// Exits 0 on full pass, 1 on any failed check, 2 on a driver crash.

import { chromium } from "playwright";
import { spawn, execSync } from "node:child_process";
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import http from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";

const skillDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(skillDir, "../../..");
const appDir = path.join(repoRoot, "VenstarTranslator");

const STATE = "/tmp/venstartranslator-run";
const SHOTS = path.join(STATE, "shots");
const APP_PORT = 18080; // avoid the app's default 8080
const STUB_PORT = 19090;
const BASE = `http://127.0.0.1:${APP_PORT}`;
const KEEP = process.argv.includes("--keep");

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const fails = [];
const check = (label, ok, detail) => {
  console.log(`${ok ? "PASS" : "FAIL"}  ${label}${detail ? `  [${detail}]` : ""}`);
  if (!ok) fails.push(label);
};

rmSync(STATE, { recursive: true, force: true });
mkdirSync(SHOTS, { recursive: true });
writeFileSync(path.join(STATE, "sensors.json"), "[]");

// Stub data source: any JSON endpoint with a temperature works as a sensor URL.
const stub = http.createServer((_req, res) => {
  res.setHeader("Content-Type", "application/json");
  res.end(JSON.stringify({ temperature: 72.5 }));
});
await new Promise((r) => stub.listen(STUB_PORT, "127.0.0.1", r));

// App launch — isolated sensors file, SQLite DBs, and port so a developer's
// local state is never touched. The app rewrites its sensors file on startup,
// so never point SensorFilePath at a config you care about.
const app = spawn("dotnet", ["run", "--no-launch-profile"], {
  cwd: appDir,
  env: {
    ...process.env,
    SensorFilePath: path.join(STATE, "sensors.json"),
    ConnectionStrings__Hangfire: path.join(STATE, "hangfire.db"),
    ConnectionStrings__DataCache: `Data Source=${path.join(STATE, "datacache.db")}`,
    Kestrel__Endpoints__Http__Url: `http://*:${APP_PORT}`,
    ASPNETCORE_ENVIRONMENT: "Production",
  },
  stdio: ["ignore", "pipe", "pipe"],
});
const log = [];
app.stdout.on("data", (d) => log.push(d));
app.stderr.on("data", (d) => log.push(d));
const dumpLog = () => writeFileSync(path.join(STATE, "app.log"), log.join(""));

async function stopAll() {
  dumpLog();
  app.kill("SIGTERM"); // dotnet run forwards SIGTERM to the app
  for (let i = 0; i < 10; i++) {
    await sleep(500);
    try { await fetch(`${BASE}/api/version`, { signal: AbortSignal.timeout(500) }); }
    catch { break; }
  }
  try { execSync("pkill -f VenstarTranslator.dll", { stdio: "ignore" }); } catch { /* already gone */ }
  stub.close();
}

let browser;
try {
  // dotnet run builds first; allow a slow cold start
  let up = false;
  for (let i = 0; i < 60 && !up; i++) {
    await sleep(2000);
    try { up = (await fetch(`${BASE}/api/version`, { signal: AbortSignal.timeout(1500) })).ok; } catch { /* not yet */ }
  }
  if (!up) throw new Error(`app never came up on :${APP_PORT} — see ${STATE}/app.log`);
  console.log("app up:", await (await fetch(`${BASE}/api/version`)).text());

  const create = await fetch(`${BASE}/api/sensors`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      Name: "Test Sensor", Enabled: true, Purpose: "Remote", Scale: "F",
      URL: `http://127.0.0.1:${STUB_PORT}/temp.json`, JSONPath: "$.temperature",
    }),
  });
  check("create test sensor", create.ok, String(create.status));

  browser = await chromium.launch({ args: ["--no-sandbox"] });
  const page = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
  const consoleErrors = [];
  page.on("console", (m) => { if (m.type() === "error") consoleErrors.push(m.text()); });
  page.on("pageerror", (e) => consoleErrors.push(String(e)));

  // Sensor table
  await page.goto(`${BASE}/index.html`);
  await page.waitForSelector("text=Test Sensor", { timeout: 15000 });
  await sleep(800); // style.css fadeInUp (0.6 s) — screenshot mid-animation looks broken
  await page.screenshot({ path: `${SHOTS}/1-index.png`, fullPage: true });
  check("index shows sensor table", true);

  // Protobuf Listener: capture the app's own pairing broadcast (UDP self-receive)
  await page.goto(`${BASE}/protobuf.html`);
  await page.click("#btnStart");
  await page.waitForFunction(() => document.getElementById("status").textContent.includes("Listening on"));
  const pair = await fetch(`${BASE}/api/sensors/0/pair`); // GET, not POST
  check("pair broadcast accepted", pair.ok, String(pair.status));
  await sleep(1000); // let the 5 datagrams land in the capture buffer
  await page.click("#btnPoll");
  await page.waitForSelector("tr.pkt", { timeout: 10000 });
  const rows = await page.locator("tr.pkt").count();
  check("listener self-received the burst", rows >= 5, `${rows} rows`);
  await sleep(800); // fresh-row fade (0.5 s)
  await page.screenshot({ path: `${SHOTS}/2-listener.png`, fullPage: true });
  await page.click("#btnStop");

  check("no browser console errors", consoleErrors.length === 0, consoleErrors.join(" | ").slice(0, 300));
} catch (e) {
  console.error("DRIVER CRASH:", e);
  fails.push("driver crash");
} finally {
  if (browser) await browser.close();
  if (KEEP && !fails.length) {
    dumpLog();
    console.log(`\n--keep: app running at ${BASE}, stub data source on :${STUB_PORT}.`);
    console.log("This process stays alive to serve the stub — Ctrl-C (or kill it) stops everything.");
    const bail = async () => { await stopAll(); process.exit(0); };
    process.on("SIGINT", bail);
    process.on("SIGTERM", bail);
    await new Promise(() => {}); // park until signalled
  } else {
    await stopAll();
  }
}

console.log(fails.length ? `RESULT: ${fails.length} FAILURE(S)` : "RESULT: ALL PASS");
console.log(`screenshots: ${SHOTS}   app log: ${STATE}/app.log`);
process.exit(fails.length ? 1 : 0);
