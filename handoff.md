# Manual Schematic Mode Handoff

This handoff is for the next agent continuing work related to manual schematic mode across two repositories:

- `cascode-lynx` at `~/Projects/cascode-lynx`
- `prometheus-lynx` at `~/Projects/prometheus-lynx`

The user asked for the full plan to be implemented without editing the attached plan file itself. The work requested by that plan is functionally complete. This document explains:

- what the feature is
- what was already finished
- what files were changed
- what was verified
- what was not fully verified because of local environment issues
- what a follow-on agent should do next, if anything

## 0. 2026-03-15 Closure Update

This handoff is now aligned with the shipped manual-mode contract:

- structured render diagnostics are propagated as structured native diagnostics (`severity`, `code`, `entityRefs`, `geometry`)
- native editor operations now include explicit port-side editing via `setPortSide`
- Designer supports end-to-end manual geometry authoring for:
  - device place + orientation (`moveDevice`, `rotateDevice`, `mirrorDevice`)
  - port place + side (`movePort`, `setPortSide`)
  - net geometry (`setNetSegments`)
- RFC alignment:
  - `docs/rfcs/0006-schematic-document-and-editor-api.md` is now explicitly marked historical/superseded
  - `docs/rfcs/0012-manual-schematic-mode-and-editor-contract.md` is the active contract
  - `docs/examples/manual-mode-canonical.cas` is the canonical manual-mode example

## 1. Feature Goal

The overall goal was to add a true manual schematic mode for Cascode and the Designer integration.

The core product semantics are:

- `render` blocks support `mode auto` and `mode manual`
- `manual` means complete, explicit, fail-fast geometry
- `auto` keeps solver-based placement/routing behavior
- `wp` is removed entirely
- `seg` is the single net geometry primitive
- in `auto`, `seg` acts as routing guidance only
- in `manual`, `seg` is exact geometry
- manual segments may be any straight-line angle
- T-junction semantics are:
  - endpoint landing on interior of another segment connects
  - simple crossing without shared endpoint does not connect
- native/editor diagnostics should preserve structure:
  - severity
  - code
  - entity references
  - geometry
- Designer should eventually support full manual geometry authoring, not just read-only/manual-consume mode

## 2. Final Status

All plan todos are complete.

Completed todo IDs:

- `lang-mode-seg`
- `native-exact-resolver`
- `native-contract-diagnostics`
- `cli-and-cascode-tests`
- `designer-compatibility`
- `designer-manual-authoring`

There is no known remaining product implementation work from the plan itself.

There is still follow-up verification work that was not completed only because the local Prometheus Panther environment is missing dependencies / has unrelated harness issues. Those are documented below.

## 3. High-Level Result

### In `cascode-lynx`

This repository now supports:

- `render { mode manual }`
- `seg` syntax instead of `wp`
- manual render validation with completeness checks
- exact/manual schematic resolution that bypasses SAT/maze-router layout
- structured native diagnostics
- native `setNetSegments` editing
- CLI render support for manual mode
- tests for:
  - round-tripping manual render blocks
  - missing manual render geometry diagnostics
  - arbitrary-angle manual segments
  - T-junction connectivity
  - non-connecting crossings
  - CLI rendering of manual geometry

### In `prometheus-panther`

Designer now supports:

- parsing `renderSource.mode` as `auto | manual`
- preserving structured diagnostics from the native API
- showing richer diagnostic info in the status panel
- not auto-reflowing a manual document after text sync
- render actions labeled in `auto` / `manual` terms
- manual wire editing that issues `setNetSegments` operations instead of old connectivity edits
- exact device move behavior through the existing move op path, with mode-aware feedback
- tests for:
  - model parsing of render mode and diagnostics
  - manual segment edit helper behavior
  - operation inversion for `setNetSegments`
  - wire tool anchor/commit behavior
  - sync controller behavior with the updated render semantics

## 4. Important Semantics the Next Agent Must Preserve

These are not optional. They were explicitly clarified with the user.

### Render mode

- `manual` must be explicit and fail-fast
- do not silently fall back to auto behavior
- do not add compatibility shims for older waypoint-based render semantics

### Net authoring

- `wp` is gone
- `seg` is the canonical net geometry primitive
- in `auto`, `seg` is guidance
- in `manual`, `seg` is exact geometry

### Segment geometry

- straight-line arbitrary angles are allowed in manual mode
- do not reintroduce Manhattan-only assumptions in manual paths

### Connectivity semantics

- endpoint on segment interior => connects
- crossing with no shared endpoint => does not connect
- manual connectivity validation must remain fail-fast

### Diagnostics

- preserve structured diagnostics end-to-end
- do not flatten them back to message-only strings in the Designer model

### Compatibility policy

- no legacy shims
- no silent fallbacks
- no backwards-compat readers for old shapes

## 5. `cascode-lynx` Work Already Completed

### Key language changes

Files changed:

- `tools/language/Cascode.g4`
- `tools/language/CascodeDocument.Render.cs`
- `tools/language/CascodeAstBuilder.Render.cs`
- `tools/language/CascodeWriter.Render.cs`
- `tools/language/CascodeParserFacade.cs`
- `tools/language/Validation/RenderBlockValidator.cs`
- generated ANTLR files under `tools/language/Generated/`

What changed:

- added `mode manual|auto` in render blocks
- removed `wp`
- added `seg <pointExpr> <pointExpr>`
- added `RenderLayoutMode`
- replaced waypoint storage with segment storage
- updated parsing/writing/validation logic
- manual validation now requires explicit device placement, port placement + side, and net segments

### Exact manual resolver

Files changed:

- `tools/render/Layout/ExactSchematicResolver.cs` (new)
- `tools/render/Routing/RenderRouteConstraints.cs`
- `tools/render/Routing/MazeRouter.cs`
- `tools/native/Cascode.Native/SchematicConstraintResolver.cs`

What changed:

- introduced exact/manual resolver path that bypasses SAT placement and maze routing
- exact resolver computes placements, terminal positions, net segments, junctions, and overlap diagnostics
- auto mode uses `seg` as router guide points rather than exact geometry

### Native contracts and dispatcher

Files changed:

- `tools/native/Cascode.Native/SchematicContracts.cs`
- `tools/native/Cascode.Native/SchematicApiDispatcher.cs`
- `tools/native/Cascode.Native/SchematicDocumentBuilder.cs`
- `tools/native/Cascode.Native/SchematicOperationApplier.cs`
- `tools/native/Cascode.Native/SchematicLayoutProjection.cs`
- `tools/native/Cascode.Native/JsonElementExtensions.cs`
- `editors/node/src/index.d.ts`

What changed:

- render mode contract changed to `RespectDocument` / `Auto` / `Manual` internally, with user-facing `auto|manual`
- added structured diagnostic fields
- replaced waypoint operations with `setNetSegments`
- layout projection now handles non-axis-aligned segments correctly

### CLI and tests

Files changed:

- `tools/cli/Commands/RenderCommandModule.cs`
- `tests/unit/tools/language/Cascode.Language.Tests/RenderBlockTests.cs`
- `tests/unit/tools/render/Cascode.Render.Tests/RenderConstraintTests.cs`
- `tests/unit/tools/render/Cascode.Render.Tests/ExactSchematicResolverTests.cs` (new)
- `tests/unit/tools/native/Cascode.Native.Tests/SchematicApiDispatcherTests.cs`
- `tests/integration/cli/Cascode.Cli.IntegrationTests/RenderIntegrationTests.cs`

What changed:

- CLI render now dispatches to exact resolver for manual render mode
- tests cover parsing/writing/validation/manual geometry/native operation behavior

## 6. `prometheus-panther` Work Already Completed

### Model / parsing / diagnostics compatibility

Files changed:

- `ide/designer/src/features/cascode-schematic/types.ts`
- `ide/designer/src/features/cascode-schematic/model/types.ts`
- `ide/designer/src/features/cascode-schematic/model/parse.ts`
- `ide/designer/src/features/cascode-schematic/state/store.ts`
- `ide/designer/src/features/cascode-schematic/hooks/useSchematicSync.ts`
- `ide/designer/src/features/cascode-schematic/state/sync-controller.ts`
- `ide/designer/src/features/cascode-schematic/background/cascode-editor.ts`
- `ide/designer/src/features/cascode-schematic/panels/CascodeStatusPanel.tsx`
- `ide/designer/src/features/cascode-schematic/CascodeSchematicView.tsx`
- `ide/designer/src/features/cascode-schematic/panels/Toolbar.tsx`

What changed:

- `SchematicModel.diagnostics` is now `CascodeDiagnostic[]`, not a count
- `SchematicModel.renderSource` now stores:
  - `hasRenderBlock`
  - `mode`
- `SchematicModel.terminalNets` was added to map terminal tokens to net names
- document summary now includes `renderMode`
- sync path now stores model diagnostics in the feature store
- sync controller no longer auto-calls a separate reflow render after `document.updateText`
- background API boundary now uses `auto | manual`
- status panel now shows severity/code/entity refs
- toolbar render actions now reflect `auto` and `manual`

### Manual geometry authoring in Designer

Files changed:

- `ide/designer/src/features/cascode-schematic/model/manual-net-edit.ts` (new)
- `ide/designer/src/features/cascode-schematic/model/operations.ts`
- `ide/designer/src/features/cascode-schematic/interaction/tools/wire-tool.ts`
- `ide/designer/src/features/cascode-schematic/renderer/wire-preview-renderer.ts`
- `ide/designer/src/features/cascode-schematic/renderer/hooks/useSchematicTools.ts`
- `ide/designer/src/features/cascode-schematic/renderer/SchematicCanvas.tsx`
- `ide/designer/src/features/cascode-schematic/hooks/useSchematicOperations.ts`

What changed:

- added helper functions to build exact manual net segment edits
- manual wire edits now operate on `setNetSegments`
- manual add only succeeds when both anchors resolve to the same net
- manual delete removes the segment whose span contains the selected points
- wire tool callback contract changed from endpoint-token connectivity to full wire anchors
- wire preview is now straight-line, matching manual segment semantics
- `setNetSegments` is now included in operation inversion logic

### Designer tests changed

Files changed:

- `ide/designer/tests/features/cascode-schematic/model-parse.test.ts`
- `ide/designer/tests/features/cascode-schematic/sync-controller.test.ts`
- `ide/designer/tests/features/cascode-schematic/cascode-feature-store.test.ts`
- `ide/designer/tests/features/cascode-schematic/clipboard.test.ts`
- `ide/designer/tests/features/cascode-schematic/model-bounds.test.ts`
- `ide/designer/tests/features/cascode-schematic/select-tool.test.ts`
- `ide/designer/tests/features/cascode-schematic/spatial-index.test.ts`
- `ide/designer/tests/features/cascode-schematic/useSchematicViewport.test.ts`
- `ide/designer/tests/features/cascode-schematic/useSchematicKeyboard.test.ts`
- `ide/designer/tests/features/cascode-schematic/wire-tool.test.ts`
- `ide/designer/tests/features/cascode-schematic/operations.test.ts`
- `ide/designer/tests/features/cascode-schematic/manual-net-edit.test.ts` (new)
- `ide/designer/tests/features/cascode-schematic/schematic-store.test.ts`
- `ide/designer/tests/features/cascode-schematic/cas-file-compatibility.test.ts`
- `ide/designer/tests/features/cascode-schematic/terminal-alignment.test.ts`
- `ide/designer/tests/features/cascode-schematic/route-continuity.test.ts`
- `ide/designer/tests/ui/cascode-boundaries.test.ts`

What changed:

- old render mode names were updated to `auto|manual`
- tests that manually construct `SchematicModel` were updated for:
  - `diagnostics: []`
  - `renderSource`
- added targeted tests for manual net edit behavior
- wire tool tests now validate anchor-based commit behavior

## 7. Verification Already Performed

### Verified in `prometheus-panther`

This focused test command passed:

```sh
npm run test -- \
  tests/features/cascode-schematic/model-parse.test.ts \
  tests/features/cascode-schematic/manual-net-edit.test.ts \
  tests/features/cascode-schematic/operations.test.ts \
  tests/features/cascode-schematic/wire-tool.test.ts \
  tests/features/cascode-schematic/sync-controller.test.ts
```

Result:

- `5` test files passed
- `62` tests passed

### Verified earlier in `cascode-lynx`

Before switching to Designer work, the Cascode-side implementation and associated tests were completed and passing during the earlier phase of the session. That included:

- language tests
- render tests
- native API tests
- CLI integration test coverage for manual mode

The detailed work was summarized in conversation state and implemented before the Designer handoff phase started.

## 8. Verification That Was Not Cleanly Possible Here

These are environment-level issues, not known regressions from this feature work.

### Prometheus Panther full `typecheck`

Running:

```sh
npm run typecheck
```

produced many unrelated workspace/dependency errors outside the schematic feature area, including missing modules and unrelated Reactron/native/runtime typing issues such as:

- `@vscode/ripgrep`
- `pixi.js`
- `openai`
- `adm-zip`
- `mqtt`
- `framer-motion`
- Reactron runtime exported symbol mismatches

There was also one temporary local failure from the new parser helper during development, but that was fixed. The remaining `typecheck` failures were pre-existing or outside the schematic change surface.

### Prometheus Panther broader test suite / harness tests

Some broader tests could not be trusted in this environment because of unrelated harness/build problems:

- Reactron harness startup failures
- missing module resolution during harness build
- unrelated duplicate top-level generated main-function declarations in the harness bundle

Examples seen:

- `tests/ui/cascode-boundaries.test.ts` failed due harness startup/build issues
- some broader Designer tests failed because importing unrelated workspace subsystems required missing packages like `@vscode/ripgrep`

### What this means

Do not interpret the above as evidence that the manual schematic mode work is broken.

The focused feature tests listed above are green.

If the next agent has a fully bootstrapped `prometheus-panther` environment, they should rerun the broader checks.

## 9. Known Caveats / Potential Follow-Up Cleanup

These are not known blockers to the requested plan, but the next agent should be aware of them.

### Endpoint-selection hook path is probably no longer important

The old endpoint-selection flow in Designer was part of the connectivity-first model. The wire tool now commits via anchor geometry directly.

The `useEndpointSelection()` hook and some related UI plumbing still exist, but they are no longer central to wire authoring. A future cleanup pass may be able to remove or simplify that code if the user wants cleanup. Do not do this casually without checking the canvas/UI interactions first.

### Manual segment delete behavior is intentionally simple

The current helper removes whichever existing segment contains both selected points. It does not split segments or perform partial trimming. This matches the narrow need for targeted exact segment editing without inventing more semantics than requested.

If the user later wants:

- segment splitting
- insertion at interior points
- multi-segment drag editing
- device drag causing segment rewrites

that is follow-on feature work, not unfinished work from this plan.

### Manual wire edits are same-net only

This is intentional.

In manual mode, the Designer helper currently refuses to create geometry that implicitly changes connectivity across nets. That keeps the editor from inventing topology changes while we are still in exact-geometry mode.

## 10. Unrelated Files to Ignore

While working in `prometheus-panther`, unrelated untracked files were discovered. The user explicitly instructed that they should be ignored.

Ignore these:

- `preprocessor_ref_model_wiring_plan.md`
- `prl/prl-control/test-results/`
- `x/dlovell/cascode-agent/`

Do not touch them unless the user later asks you to.

## 11. Current Git State

There are uncommitted changes in both repositories corresponding to this feature work.

### `cascode-lynx`

Relevant modified/new files include:

- language render files
- native schematic API files
- render resolver files
- CLI render command
- tests
- `tools/render/Layout/ExactSchematicResolver.cs`
- `tests/unit/tools/render/Cascode.Render.Tests/ExactSchematicResolverTests.cs`

There is also an untracked `.cursor/` directory and `docs/issues/` visible in the workspace status. Do not assume they are part of this feature unless the user says so.

### `prometheus-panther`

Relevant modified/new files are the schematic/designer files listed above.

No commit was created.

No PR was created.

## 12. Recommended Next Actions for the Next Agent

If the user asks for continuation rather than a new feature, the next sensible steps are:

1. Do not re-implement the plan. It is already done.
2. If the environment is properly bootstrapped, run broader verification in `prometheus-panther`:
   - `npm run test`
   - `npm run typecheck`
3. If any failures remain, separate them into:
   - actual regressions from this feature
   - unrelated workspace/environment issues
4. If the user wants cleanup, consider a small follow-up pass to remove now-obsolete endpoint-selection plumbing in Designer, but only after confirming it is truly unused.
5. If the user wants source-control work, then:
   - inspect git diffs in both repos
   - make sure unrelated files are excluded
   - commit only when explicitly asked

## 13. Exact Commands Worth Re-Running

### In `cascode-lynx`

If needed:

```sh
dotnet build tools/cli/Cascode.Cli.csproj
dotnet test Cascode.sln --configuration Release
```

If any C# files were changed again:

```sh
dotnet csharpier format .
```

### In `prometheus-panther/ide/designer`

Focused feature regression command:

```sh
npm run test -- \
  tests/features/cascode-schematic/model-parse.test.ts \
  tests/features/cascode-schematic/manual-net-edit.test.ts \
  tests/features/cascode-schematic/operations.test.ts \
  tests/features/cascode-schematic/wire-tool.test.ts \
  tests/features/cascode-schematic/sync-controller.test.ts
```

Broader checks if environment is healthy:

```sh
npm run typecheck
npm run test
```

## 14. Final Bottom Line

The requested manual schematic mode plan has been implemented end-to-end across the Cascode core/native stack and the Designer integration.

If the next agent is asked “what remains?”, the correct answer is:

- no remaining core implementation from the plan
- only broader environment-dependent verification and possible optional cleanup

Do not restart design work from scratch. Do not add shims. Do not reintroduce waypoint logic. Preserve the exact/manual semantics already implemented.
