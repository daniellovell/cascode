import { createRequire } from "node:module";
import { parentPort, workerData } from "node:worker_threads";

const require = createRequire(import.meta.url);
const cascode = require(workerData.packageRoot);
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

const session = cascode.createSession("{}");
try {
  cascode.invoke(cascode.native, session, "document.open", {
    documentId: "doc-bench",
    text: workerData.source
  });

  const started = cascode.invoke(cascode.native, session, "job.start", { documentId: "doc-bench" });
  let polled = { state: "running", progress: 0 };
  for (let i = 0; i < 30; i++) {
    polled = cascode.invoke(cascode.native, session, "job.poll", { jobId: started.jobId });
    if (polled.state !== "running") {
      break;
    }

    await sleep(75);
  }

  if (polled.state === "running") {
    cascode.invoke(cascode.native, session, "job.cancel", { jobId: started.jobId });
    for (let i = 0; i < 20; i++) {
      polled = cascode.invoke(cascode.native, session, "job.poll", { jobId: started.jobId });
      if (polled.state !== "running") {
        break;
      }

      await sleep(75);
    }
  }

  parentPort.postMessage({
    kind: "bench",
    state: polled.state,
    progress: polled.progress
  });
} finally {
  cascode.destroySession(session);
}
