import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptFile = fileURLToPath(import.meta.url);
const nodeRoot = path.resolve(path.dirname(scriptFile), "..");
const repoRoot = path.resolve(nodeRoot, "..", "..");

const stdlibSrc = path.join(repoRoot, "lib", "std");
const stdlibDst = path.join(nodeRoot, "lib", "std");

async function pathExists(p) {
  return fs.stat(p).then(
    () => true,
    () => false
  );
}

async function ensureStdlibPresent() {
  const ok = await pathExists(stdlibSrc);
  if (!ok) {
    throw new Error(`[cascode-native] stdlib source directory not found: ${stdlibSrc}`);
  }
}

async function run() {
  await ensureStdlibPresent();
  await fs.mkdir(path.dirname(stdlibDst), { recursive: true });
  await fs.rm(stdlibDst, { recursive: true, force: true });
  await fs.cp(stdlibSrc, stdlibDst, { recursive: true });
  console.log(`[cascode-native] Staged stdlib into package at: ${stdlibDst}`);
}

await run();

