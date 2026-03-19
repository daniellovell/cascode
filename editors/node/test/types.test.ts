import {
  createSession,
  destroySession,
  call,
  stdlibPath
} from "../src/index.js";

const session: number = createSession("{}");
const path: string = stdlibPath;
void path;

const opened = JSON.parse(
  call(session, "document.open", JSON.stringify({ documentId: "doc", text: "VERSION 4.0\n" }))
) as { revision: number };
void opened;

destroySession(session);
