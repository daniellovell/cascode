"use strict";

const path = require("path");

const addon = require(path.join(
  __dirname,
  "..",
  "build",
  "Release",
  "cascode_native_addon.node"
));

function createSession(optionsJson = "{}") {
  return addon.createSession(optionsJson);
}

function destroySession(session) {
  addon.destroySession(session);
}

function call(session, method, requestJson) {
  return addon.call(session, method, requestJson);
}

function lastErrorJson(session) {
  return addon.lastErrorJson(session);
}

function apiVersion() {
  return addon.apiVersion();
}

function schemaVersion() {
  return addon.schemaVersion();
}

function parseCall(native, session, method, request) {
  return JSON.parse(native.call(session, method, JSON.stringify(request)));
}

function open(native, session, req) {
  return parseCall(native, session, "document.open", req);
}

function updateText(native, session, req) {
  return parseCall(native, session, "document.updateText", req);
}

function close(native, session, req) {
  parseCall(native, session, "document.close", req);
}

function render(native, session, req) {
  return parseCall(native, session, "render.schematic", req);
}

function applyOps(native, session, req) {
  return parseCall(native, session, "schematic.applyOperations", req);
}

function erc(native, session, req) {
  return parseCall(native, session, "erc.run", req);
}

function emit(native, session, req) {
  return parseCall(native, session, "emit.run", req);
}

function verify(native, session, req) {
  return parseCall(native, session, "verify.run", req);
}

function jobStart(native, session, req) {
  return parseCall(native, session, "job.start", req);
}

function jobPoll(native, session, req) {
  return parseCall(native, session, "job.poll", req);
}

function jobCancel(native, session, req) {
  return parseCall(native, session, "job.cancel", req);
}

const native = {
  createSession,
  destroySession,
  call,
  lastErrorJson,
  apiVersion,
  schemaVersion
};

module.exports = {
  native,
  createSession,
  destroySession,
  call,
  lastErrorJson,
  apiVersion,
  schemaVersion,
  open,
  updateText,
  close,
  render,
  applyOps,
  erc,
  emit,
  verify,
  jobStart,
  jobPoll,
  jobCancel
};
