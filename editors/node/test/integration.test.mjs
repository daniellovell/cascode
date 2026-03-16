import assert from "node:assert/strict";
import path from "node:path";
import { Worker } from "node:worker_threads";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const packageRoot = path.resolve(__dirname, "..");

const sampleSource = `VERSION 5.0

primitive NMOS_Level1(size primSize) implements NMOS {
  device "nmos_level1"
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
    NMOS M1 = new NMOS_Level1(Unit) {
      .D--OUT
      .G--IN
      .S--n1
      .B--GND
    }
    NMOS M2 = new NMOS_Level1(Unit) {
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
    let settled = false;
    const settle = (onDone) => {
      if (settled) {
        return;
      }

      settled = true;
      void worker.terminate().catch(() => {}).finally(onDone);
    };

    worker.once("message", (message) => {
      settle(() => resolve(message));
    });
    worker.once("error", (error) => {
      settle(() => reject(error));
    });
    worker.once("exit", (code) => {
      if (settled) {
        return;
      }

      if (code !== 0) {
        settle(() => reject(new Error(`Worker '${filename}' exited with code ${code}.`)));
        return;
      }

      settle(() => reject(new Error(`Worker '${filename}' exited without sending a message.`)));
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
