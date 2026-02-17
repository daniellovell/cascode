import assert from "node:assert/strict";
import path from "node:path";
import { Worker } from "node:worker_threads";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const packageRoot = path.resolve(__dirname, "..");

const sampleSource = `VERSION 3.2

primitive NMOS Level1_NMOS(size primSize) {
  device "level1_nmos"
  params {
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }
}

circuit Amp {
  level EL
  input IN : analog
  output OUT : analog
  ground GND
  fill {
    net n1 : analog
    size Unit = size(W=1u, L=180n, M=1)
    NMOS M1 = new Level1_NMOS(Unit) {
      .D--OUT
      .G--IN
      .S--n1
      .B--GND
    }
    NMOS M2 = new Level1_NMOS(Unit) {
      .D--OUT
      .G--n1
      .S--GND
      .B--GND
    }
  }
}
`;

function runWorker(filename, workerData, env) {
  return new Promise((resolve, reject) => {
    const worker = new Worker(filename, { workerData, env });
    worker.once("message", resolve);
    worker.once("error", reject);
    worker.once("exit", (code) => {
      if (code !== 0) {
        reject(new Error(`Worker '${filename}' exited with code ${code}.`));
      }
    });
  });
}

test("dual-worker sessions stay isolated across edit and bench flows", async () => {
  const libraryPath = process.env.CASCODE_NATIVE_LIB;
  assert.ok(
    libraryPath,
    "CASCODE_NATIVE_LIB must point at the published libcascode shared library."
  );
  const libraryDir = path.dirname(libraryPath);
  const librarySearchPathKey = process.platform === "darwin" ? "DYLD_LIBRARY_PATH" : "LD_LIBRARY_PATH";
  const existingLibrarySearchPath = process.env[librarySearchPathKey] ?? "";
  const workerEnv = {
    ...process.env,
    CASCODE_NATIVE_LIB: libraryPath,
    [librarySearchPathKey]: `${libraryDir}${path.delimiter}${existingLibrarySearchPath}`
  };

  const [editResult, benchResult] = await Promise.all([
    runWorker(path.join(__dirname, "worker-edit.mjs"), {
      packageRoot,
      libraryPath,
      source: sampleSource
    }, workerEnv),
    runWorker(path.join(__dirname, "worker-bench.mjs"), {
      packageRoot,
      libraryPath,
      source: sampleSource
    }, workerEnv)
  ]);

  assert.equal(editResult.kind, "edit");
  assert.equal(benchResult.kind, "bench");
  assert.equal(editResult.revision, 2);
  assert.ok(editResult.hasRenderBlock);
  assert.ok(["running", "completed", "cancelled"].includes(benchResult.state));
  assert.ok(benchResult.progress >= 0);
  assert.ok(benchResult.progress <= 100);
});
