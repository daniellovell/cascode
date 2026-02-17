import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

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

async function ensureExists(targetPath, label) {
  const stat = await fs.stat(targetPath).catch(() => null);
  if (!stat) {
    throw new Error(`Missing ${label}: ${targetPath}`);
  }
}

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

async function run() {
  await ensureExists(templateDir, "template directory");
  await ensureExists(addonSrc, "built addon");
  await ensureExists(nativeSrcDir, "published native runtime directory");
  await ensureExists(expectedLibPath, "expected runtime shared library");

  const resolvedRepoRoot = path.resolve(repoRoot);
  const resolvedOutDir = path.resolve(outDir);
  const outDirRoot = path.parse(resolvedOutDir).root;
  const outDirIsDescendantOfRepoRoot = resolvedOutDir.startsWith(
    `${resolvedRepoRoot}${path.sep}`
  );

  if (
    resolvedOutDir === outDirRoot ||
    resolvedOutDir === resolvedRepoRoot ||
    !outDirIsDescendantOfRepoRoot
  ) {
    throw new Error(
      `Unsafe delete target in run(): outDir="${resolvedOutDir}" must be a descendant of repoRoot="${resolvedRepoRoot}" and must not be filesystem root or repoRoot.`
    );
  }

  await fs.rm(resolvedOutDir, { recursive: true, force: true });
  await fs.mkdir(resolvedOutDir, { recursive: true });
  await fs.cp(templateDir, resolvedOutDir, { recursive: true });

  const pkgPath = path.join(resolvedOutDir, "package.json");
  const pkg = JSON.parse(await fs.readFile(pkgPath, "utf8"));
  pkg.version = version;
  await fs.writeFile(pkgPath, `${JSON.stringify(pkg, null, 2)}\n`, "utf8");

  const prebuildDir = path.join(resolvedOutDir, "prebuilds");
  await fs.mkdir(prebuildDir, { recursive: true });
  await fs.copyFile(addonSrc, path.join(prebuildDir, "cascode_native_addon.node"));

  const nativeOutDir = path.join(resolvedOutDir, "native", rid);
  await fs.mkdir(path.dirname(nativeOutDir), { recursive: true });
  await copyRuntimeFiles(nativeSrcDir, nativeOutDir);

  console.log(`Staged ${packageName}@${version} at ${resolvedOutDir}`);
}

await run();
