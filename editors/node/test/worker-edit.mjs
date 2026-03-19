import { createRequire } from "node:module";
import { parentPort, workerData } from "node:worker_threads";

const require = createRequire(import.meta.url);
const cascode = require(workerData.packageRoot);

const session = cascode.createSession("{}");
try {
  const opened = JSON.parse(
    cascode.call(session, "document.open", JSON.stringify({
      documentId: "doc-edit",
      text: workerData.source
    }))
  );

  const rendered = JSON.parse(
    cascode.call(session, "render.schematic", JSON.stringify({
      documentId: "doc-edit",
      mode: "manual",
      persist: true
    }))
  );

  const hasRenderBlock = rendered.sourceText.includes("render {");
  parentPort.postMessage({
    kind: "edit",
    revision: rendered.document.revision,
    hasRenderBlock
  });
} finally {
  cascode.destroySession(session);
}
