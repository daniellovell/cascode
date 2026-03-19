import {
  apiVersion,
  createSession,
  destroySession,
  invoke,
  lastErrorJson,
  native,
  schemaVersion
} from "../src/index.js";

const session: number = createSession("{}");
const version: string = apiVersion();
const schema: string = schemaVersion();
const err: string | null = lastErrorJson(session);
void err;

const opened = invoke(native, session, "document.open", { documentId: "doc", text: "VERSION 4.0\n" }) as {
  revision: number;
};
const applied = invoke(native, session, "schematic.applyOperations", {
  documentId: "doc",
  baseRevision: opened.revision,
  operations: [{ opId: "op1", type: "movePort", port: "IN", x: 1, y: 1 }]
});
void opened;
void applied;
void version;
void schema;

destroySession(session);
