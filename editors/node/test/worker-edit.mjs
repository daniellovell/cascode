import { createRequire } from "node:module";
import { parentPort, workerData } from "node:worker_threads";

const require = createRequire(import.meta.url);
const cascode = require(workerData.packageRoot);

const session = cascode.createSession("{}");
try {
  const opened = cascode.open(cascode.native, session, {
    documentId: "doc-edit",
    text: workerData.source
  });

  const updated = cascode.applyOps(cascode.native, session, {
    documentId: "doc-edit",
    baseRevision: opened.revision,
    operations: [
      {
        opId: "move-port-1",
        type: "movePort",
        port: "IN",
        x: 1000,
        y: 1000
      }
    ]
  });

  const hasRenderBlock = updated.sourceText.includes("render {");
  parentPort.postMessage({
    kind: "edit",
    revision: updated.document.revision,
    hasRenderBlock
  });
} finally {
  cascode.destroySession(session);
}
