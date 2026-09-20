import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || "playwright");
const base = "http://127.0.0.1:5188";
const artifacts = new URL("../../artifacts/workshop/", import.meta.url);
await mkdir(artifacts, { recursive: true });
let output = "";
const server = spawn("dotnet", ["CloudCommerce.Demo.dll"], {
  cwd: fileURLToPath(new URL("../../artifacts/demo-host/", import.meta.url)),
  env: { ...process.env, ASPNETCORE_ENVIRONMENT: "Production", ASPNETCORE_URLS: base },
  stdio: ["ignore", "pipe", "pipe"], windowsHide: true
});
server.stdout.on("data", data => output += data);
server.stderr.on("data", data => output += data);
let browser;
try {
  let ready = false;
  for (let attempt = 0; attempt < 100; attempt++) {
    try { if ((await fetch(base)).ok) { ready = true; break; } } catch {}
    if (server.exitCode !== null) throw new Error(output);
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  assert(ready, "Workshop host did not start: " + output);
  browser = await chromium.launch({ headless: true, channel: process.env.PLAYWRIGHT_CHANNEL });
  const context = await browser.newContext({ viewport: { width: 1600, height: 1000 }, permissions: ["clipboard-read", "clipboard-write"] });
  await context.route("**/*", route => new URL(route.request().url()).hostname === "127.0.0.1" ? route.continue() : route.abort());
  const page = await context.newPage();
  page.setDefaultTimeout(10000);
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });
  const selectTab = async name => {
    await page.getByRole("tab", { name, exact: true }).click();
    await page.locator("#workshop-panel-" + name.toLowerCase() + ":visible").waitFor();
  };
  const go = async path => {
    await page.goto(base + path);
    await page.waitForFunction(() => window.Blazor != null);
    // This round trip also verifies interactive server hydration.
    await selectTab("Instructions");
    await selectTab("View");
  };
  await go("/");
  const routes = [...new Set(await page.locator(".demo-nav a").evaluateAll(links => links.map(link => new URL(link.href).pathname)))];
  assert.equal(routes.length, 30);
  for (const route of routes) {
    await go(route);
    assert.equal(await page.getByRole("tab").count(), 3, route);
    await selectTab("Code");
    assert((await page.locator(".demo-source").textContent()).trim().length > 20, route);
    await selectTab("Instructions");
    assert((await page.locator(".demo-instructions ol li").count()) >= 3, route);
    await selectTab("View");
  }
  console.log("All 30 routes: View, Code and Instructions passed.");

  await go("/components/product-card");
  await page.getByRole("button", { name: "Add Cloud Essentials Kit to cart" }).click();
  await page.getByText("OnAdd: cloud-essentials", { exact: true }).waitFor();
  await selectTab("Code");
  await page.getByRole("button", { name: "Copy code", exact: true }).click();
  await page.getByText("Code copied.", { exact: true }).waitFor();
  assert.equal((await page.evaluate(() => navigator.clipboard.readText())).replaceAll("\r\n", "\n"), (await page.locator(".demo-source").textContent()).replaceAll("\r\n", "\n"));
  const downloadWait = page.waitForEvent("download");
  await page.getByRole("button", { name: "Download file" }).click();
  assert.equal((await downloadWait).suggestedFilename(), "ProductCard.razor");
  await selectTab("View");
  await page.getByText("OnAdd: cloud-essentials", { exact: true }).waitFor();
  await page.getByRole("tab", { name: "View", exact: true }).focus();
  await page.keyboard.press("ArrowRight");
  await page.locator("#workshop-panel-code:visible").waitFor();
  assert.equal(await page.locator("#workshop-tab-code").getAttribute("aria-selected"), "true");
  await page.goto(base + "/booking?tab=instructions");
  await page.locator("#workshop-panel-instructions:visible").waitFor();

  await go("/payments/sandbox");
  await page.getByLabel("Authorize before capture").check();
  await page.getByRole("button", { name: /^Pay / }).click();
  await page.getByRole("button", { name: "Capture", exact: true }).waitFor();
  await selectTab("Code");
  await selectTab("View");
  await page.getByRole("button", { name: "Capture", exact: true }).click();
  await page.getByRole("button", { name: "Refund payment" }).click();
  await page.getByText("Refunded · Demo", { exact: true }).waitFor();
  await page.getByRole("button", { name: "Refresh status from provider" }).click();
  await page.getByText("Status refreshed.", { exact: true }).waitFor();
  await page.evaluate(() => window.scrollTo({ top: 0, behavior: "instant" }));
  await page.screenshot({ path: new URL("payments.png", artifacts).pathname.replace(/^\/([A-Za-z]:)/, "$1"), fullPage: false });

  await go("/payments/stripe");
  await page.getByPlaceholder("sk_test_…").fill("sk_live_DO_NOT_SEND");
  await page.getByRole("button", { name: "Connect Stripe" }).click();
  await page.getByText("Live keys are not accepted. Use provider-issued test credentials.", { exact: true }).waitFor();
  assert(await page.getByRole("button", { name: /^Pay / }).isDisabled());
  await page.getByRole("button", { name: "Clear keys" }).click();
  assert(await page.getByRole("button", { name: /^Pay / }).isDisabled());

  await go("/booking");
  const dates = page.getByRole("group", { name: "Available dates" }).getByRole("button");
  await dates.nth(1).click();
  await page.waitForFunction(() => document.querySelectorAll(".commerce-booking-dates button")[1].getAttribute("aria-pressed") === "true");
  assert.equal(await page.locator(".commerce-booking-day").count(), 1);
  await page.getByLabel("Resource capacity").fill("1");
  await page.getByRole("button", { name: "Apply scenario" }).click();
  await page.getByRole("button", { name: "Reserve this time" }).click();
  await page.getByRole("button", { name: "Confirm reservation" }).click();
  await page.getByText("Reservation confirmed without requiring a payment integration.", { exact: true }).waitFor();
  await page.locator(".commerce-booking-times button").first().click();
  await page.getByRole("button", { name: "Reschedule to selected slot" }).click();
  await page.getByText("Reservation moved and old capacity released atomically.", { exact: true }).waitFor();
  await page.getByRole("button", { name: "Cancel reservation" }).click();
  await page.getByText("Reservation cancelled and capacity returned.", { exact: true }).waitFor();

  await go("/storefront");
  await page.getByRole("button", { name: "Add Cloud Essentials Kit to cart" }).click();
  await page.getByRole("button", { name: "Add Architecture Studio to cart" }).click();
  await page.getByRole("button", { name: "Review cart" }).click();
  await page.getByRole("button", { name: "MONKEY20", exact: true }).click();
  await page.getByRole("button", { name: "Continue to checkout" }).click();
  await page.getByRole("button", { name: "Pay 156.66 USD", exact: true }).waitFor();
  await page.getByLabel("Delivery and inventory").uncheck();
  await page.getByRole("button", { name: "Pay 141.12 USD", exact: true }).waitFor();
  await page.getByLabel("Delivery and inventory").check();
  await page.getByRole("button", { name: "Pay 156.66 USD", exact: true }).waitFor();
  await page.evaluate(() => window.scrollTo({ top: 0, behavior: "instant" }));
  await page.screenshot({ path: new URL("storefront.png", artifacts).pathname.replace(/^\/([A-Za-z]:)/, "$1"), fullPage: false });
  await page.getByRole("button", { name: "Pay 156.66 USD", exact: true }).click();
  await page.getByRole("heading", { name: "Everything moved as one." }).waitFor();
  await page.getByRole("button", { name: "Copy", exact: true }).click();
  await page.getByText("Copied.", { exact: true }).waitFor();

  await go("/logistics");
  for (const name of ["Create shipping label", "Hand to carrier", "Move to final mile", "Confirm delivery"])
    await page.getByRole("button", { name: new RegExp(name) }).click();
  await page.getByRole("button", { name: /Restart journey/ }).waitFor();
  await page.getByRole("button", { name: "Reset shipment", exact: true }).click();
  await page.getByRole("button", { name: /Create shipping label/ }).waitFor();

  await page.setViewportSize({ width: 390, height: 844 });
  await go("/components/cart-view");
  await page.getByRole("button", { name: "Toggle demo navigation" }).click();
  await page.getByRole("searchbox", { name: "Find a demo" }).fill("Booking");
  await page.locator(".demo-nav").getByRole("link", { name: "Booking", exact: true }).click();
  await page.getByRole("heading", { name: /Capacity, availability/ }).waitFor();
  await page.evaluate(() => window.scrollTo({ top: 0, behavior: "instant" }));
  await page.screenshot({ path: new URL("mobile.png", artifacts).pathname.replace(/^\/([A-Za-z]:)/, "$1"), fullPage: false });
  assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), "Mobile page overflows horizontally");
  assert.equal(errors.length, 0, errors.join("\n"));
  console.log("Clipboard, download, tab persistence, keyboard, payment lifecycle, invalid keys, booking, checkout, logistics and mobile navigation passed.");
} finally {
  if (browser) await browser.close();
  await writeFile(new URL("server.log", artifacts), output);
  if (server.pid && process.platform === "win32") spawnSync("taskkill", ["/pid", String(server.pid), "/t", "/f"], { windowsHide: true });
  else server.kill("SIGTERM");
}
