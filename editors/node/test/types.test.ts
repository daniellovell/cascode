import {
  apiVersion,
  applyOps,
  createSession,
  destroySession,
  lastErrorJson,
  native,
  open,
  schemaVersion
} from "../src/index.js";

const session: number = createSession("{}");
const version: string = apiVersion();
const schema: string = schemaVersion();
const err: string | null = lastErrorJson(session);
void err;

const opened = open(native, session, { documentId: "doc", text: "VERSION 5.0\n" }) as {
  revision: number;
};
const applied = applyOps(native, session, {
  documentId: "doc",
  baseRevision: opened.revision,
  operations: [{ opId: "op1", type: "movePort", port: "IN", x: 1, y: 1 }]
});
void opened;
void applied;
void version;
void schema;

destroySession(session);
