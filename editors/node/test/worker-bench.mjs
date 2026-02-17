import { createRequire } from "node:module";
import { parentPort, workerData } from "node:worker_threads";

const require = createRequire(import.meta.url);
const cascode = require(workerData.packageRoot);

const session = cascode.createSession("{}");
try {
  cascode.open(cascode.native, session, {
    documentId: "doc-bench",
    text: workerData.source
  });

  const started = cascode.jobStart(cascode.native, session, { documentId: "doc-bench" });
  let polled = { state: "running", progress: 0 };
  for (let i = 0; i < 30; i++) {
    polled = cascode.jobPoll(cascode.native, session, { jobId: started.jobId });
    if (polled.state !== "running") {
      break;
    }
  }

  if (polled.state === "running") {
    cascode.jobCancel(cascode.native, session, { jobId: started.jobId });
    polled = cascode.jobPoll(cascode.native, session, { jobId: started.jobId });
  }

  parentPort.postMessage({
    kind: "bench",
    state: polled.state,
    progress: polled.progress
  });
} finally {
  cascode.destroySession(session);
}
