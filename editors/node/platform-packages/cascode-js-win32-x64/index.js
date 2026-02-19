"use strict";

const path = require("path");

module.exports = {
  addonPath: path.join(__dirname, "prebuilds", "cascode_native_addon.node"),
  libraryPath: path.join(__dirname, "native", "win-x64", "Cascode.Native.dll"),
  libraryDir: path.join(__dirname, "native", "win-x64"),
};

