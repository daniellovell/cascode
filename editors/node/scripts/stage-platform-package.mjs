import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

/**
 * Retrieve the value for a command-line argument provided in the form `--name=value`.
 * @param {string} name - Argument name without leading dashes.
 * @returns {string} The value after the `=` for the matching argument, or an empty string if not found.
 */
function readArg(name) {
  const prefix = `--${name}=`;
  const value = process.argv.find((arg) => arg.startsWith(prefix));
  return value ? value.slice(prefix.length) : "";
}

const packageName = readArg("package");
const rid = readArg("rid");
const libName = readArg("lib");
const rawVersion = readArg("version");
const version = rawVersion.startsWith("v") ? rawVersion.slice(1) : rawVersion;
const outDirArg = readArg("out");

if (!packageName || !rid || !libName || !version || !outDirArg) {
  throw new Error(
    "Missing required args. Expected --package=... --rid=... --lib=... --version=... --out=..."
  );
}

const scriptFile = fileURLToPath(import.meta.url);
const nodeRoot = path.resolve(path.dirname(scriptFile), "..");
const repoRoot = path.resolve(nodeRoot, "..", "..");
const packageShort = packageName.replace("@cascode/", "");
const templateDir = path.join(nodeRoot, "platform-packages", packageShort);
const outDir = path.resolve(outDirArg);

const addonSrc = path.join(nodeRoot, "build", "Release", "cascode_native_addon.node");
const nativeSrcDir = path.join(repoRoot, "build", "native", rid);
const expectedLibPath = path.join(nativeSrcDir, libName);

/**
 * Ensure the given filesystem path exists.
 *
 * @param {string} targetPath - Path to check for existence.
 * @param {string} label - Human-readable label used in the error message if the path is missing.
 * @throws {Error} If the path does not exist.
 */
async function ensureExists(targetPath, label) {
  const stat = await fs.stat(targetPath).catch(() => null);
  if (!stat) {
    throw new Error(`Missing ${label}: ${targetPath}`);
  }
}

/**
 * Recursively copies files and directories from sourceDir into targetDir, excluding files that end with `.pdb` (case-insensitive).
 * @param {string} sourceDir - Path to the source directory to copy from.
 * @param {string} targetDir - Path to the destination directory to copy into; created recursively if it does not exist.
 */
async function copyRuntimeFiles(sourceDir, targetDir) {
  await fs.mkdir(targetDir, { recursive: true });
  const entries = await fs.readdir(sourceDir, { withFileTypes: true });
  for (const entry of entries) {
    const sourcePath = path.join(sourceDir, entry.name);
    const targetPath = path.join(targetDir, entry.name);

    if (entry.isDirectory()) {
      await copyRuntimeFiles(sourcePath, targetPath);
      continue;
    }

    const lowerName = entry.name.toLowerCase();
    if (lowerName.endsWith(".pdb")) continue;
    await fs.copyFile(sourcePath, targetPath);
  }
}

/**
 * Prepare and stage a platform package into the configured output directory.
 *
 * Validates required resources, recreates the output directory, copies the package
 * template, updates the package.json version, installs the built native addon into
 * a prebuilds folder, copies native runtime files for the target RID, and logs the
 * final staged location.
 */
async function run() {
  await ensureExists(templateDir, "template directory");
  await ensureExists(addonSrc, "built addon");
  await ensureExists(nativeSrcDir, "published native runtime directory");
  await ensureExists(expectedLibPath, "expected runtime shared library");

  await fs.rm(outDir, { recursive: true, force: true });
  await fs.mkdir(outDir, { recursive: true });
  await fs.cp(templateDir, outDir, { recursive: true });

  const pkgPath = path.join(outDir, "package.json");
  const pkg = JSON.parse(await fs.readFile(pkgPath, "utf8"));
  pkg.version = version;
  await fs.writeFile(pkgPath, `${JSON.stringify(pkg, null, 2)}\n`, "utf8");

  const prebuildDir = path.join(outDir, "prebuilds");
  await fs.mkdir(prebuildDir, { recursive: true });
  await fs.copyFile(addonSrc, path.join(prebuildDir, "cascode_native_addon.node"));

  const nativeOutDir = path.join(outDir, "native", rid);
  await fs.mkdir(path.dirname(nativeOutDir), { recursive: true });
  await copyRuntimeFiles(nativeSrcDir, nativeOutDir);

  console.log(`Staged ${packageName}@${version} at ${outDir}`);
}

await run();