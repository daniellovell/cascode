using System.Text.Json;
using System.Text.Json.Nodes;
using Cascode.Language;
using Cascode.Language.Validation;
using Cascode.Workspace;

namespace Cascode.Native;

internal static class SchematicApiDispatcher
{
    internal static Func<DateTimeOffset> UtcNowProvider { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Dispatches an API method string to the corresponding handler and returns the handler's JSON response.
    /// </summary>
    /// <param name="session">The session state used to access and modify documents, jobs, and related data.</param>
    /// <param name="method">The API method identifier (for example, "document.open" or "job.poll").</param>
    /// <param name="requestJson">The raw JSON request payload supplied to the handler.</param>
    /// <returns>A JSON string produced by the selected API handler.</returns>
    /// <exception cref="ApiException">Thrown with code "CASAPI-INVALID-REQUEST" when the provided method is unknown.</exception>
    public static string Dispatch(SessionState session, string method, string requestJson)
    {
        return method switch
        {
            "document.open" => DocumentOpen(session, requestJson),
            "document.updateText" => DocumentUpdateText(session, requestJson),
            "document.close" => DocumentClose(session, requestJson),
            "convert.toStructural" => ConvertToStructural(session, requestJson),
            "convert.toCas" => ConvertToCas(session, requestJson),
            "render.schematic" => RenderSchematic(session, requestJson),
            "schematic.applyOperations" => ApplyOperations(session, requestJson),
            "job.start" => JobStart(session, requestJson),
            "job.poll" => JobPoll(session, requestJson),
            "job.cancel" => JobCancel(session, requestJson),
            "erc.run" => RunErc(session, requestJson),
            "emit.run" => RunEmit(session, requestJson),
            "verify.run" => PassThroughStub("cascode.verify/1.0"),
            "pdk.setDir" => PdkSetDir(session, requestJson),
            "pdk.scan" => PdkScan(session, requestJson),
            "pdk.emitPrimitives" => PdkEmitPrimitives(session, requestJson),
            "command.execute" => PassThroughStub("cascode.command/1.0"),
            _ => throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Unknown API method '{method}'."
            ),
        };
    }

    /// <summary>
    /// Opens a new document in the session from the provided API request JSON and returns the rendered document.
    /// </summary>
    /// <param name="session">Session state to store the new DocumentState under the chosen documentId.</param>
    /// <param name="requestJson">API request JSON containing required field "text" and optional "documentId", "circuit", and "mode".</param>
    /// <returns>A JSON string containing the serialized schematic document render.</returns>
    /// <exception cref="ApiException">Thrown when the source text fails to parse or when the requested/selected circuit cannot be found or is invalid.</exception>
    private static string DocumentOpen(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;

        var documentId = root.TryGetString("documentId") ?? "doc_1";
        var sourceText = root.RequireString("text");
        var circuitName = root.TryGetString("circuit");

        var read = CascodeReader.TryParse(sourceText, "<api>");
        EnsureParseSuccess(read);

        var selectedCircuit = SelectCircuit(read.Document!, circuitName);

        var state = new DocumentState
        {
            DocumentId = documentId,
            SourceText = sourceText,
            Document = read.Document!,
            CircuitName = selectedCircuit.Name,
            Revision = 1,
            ChangedEntities = Array.Empty<string>(),
        };

        session.Documents[documentId] = state;

        var mode = ParseRenderMode(root.TryGetString("mode"));
        var render = SchematicDocumentBuilder.Build(state, mode, allowRelaxation: false);
        return ApiJson.SerializeDocument(render);
    }

    /// <summary>
    /// Updates an open document's source text, increments its revision, rebuilds the schematic render, and returns the updated document representation.
    /// </summary>
    /// <param name="session">The current session state containing documents and jobs.</param>
    /// <param name="requestJson">A JSON request containing "documentId", "text", optional "baseRevision", and optional "circuit".</param>
    /// <returns>A JSON string conforming to "cascode.document.update/1.0" containing the updated document node and the new sourceText.</returns>
    private static string DocumentUpdateText(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;

        var state = GetDocumentState(session, root.RequireString("documentId"));
        var baseRevision = TryGetInt(root, "baseRevision");
        EnsureRevision(state, baseRevision);

        var sourceText = root.RequireString("text");
        var read = CascodeReader.TryParse(sourceText, "<api>");
        EnsureParseSuccess(read);

        var selectedCircuit = SelectCircuit(
            read.Document!,
            root.TryGetString("circuit") ?? state.CircuitName
        );

        state.SourceText = sourceText;
        state.Document = read.Document!;
        state.CircuitName = selectedCircuit.Name;
        state.Revision++;
        state.ChangedEntities = Array.Empty<string>();

        var render = SchematicDocumentBuilder.Build(
            state,
            RenderSchematicMode.RespectDocument,
            allowRelaxation: false
        );
        var response = new JsonObject
        {
            ["schema"] = "cascode.document.update/1.0",
            ["document"] = ApiJson.SerializeDocumentNode(render),
            ["sourceText"] = state.SourceText,
        };

        return response.ToJsonString(ApiJson.Options);
    }

    /// <summary>
    /// Closes and removes the specified document from the session state.
    /// </summary>
    /// <param name="requestJson">A JSON request containing a required string field "documentId".</param>
    /// <returns>A JSON string confirming the document close with schema "cascode.document.close/1.0" and "ok" = true.</returns>
    private static string DocumentClose(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var documentId = doc.RootElement.RequireString("documentId");
        session.Documents.Remove(documentId);

        return new JsonObject
        {
            ["schema"] = "cascode.document.close/1.0",
            ["ok"] = true,
        }.ToJsonString(ApiJson.Options);
    }

    /// <summary>
    /// Produce a structural representation response for the specified document.
    /// </summary>
    /// <param name="session">Session state containing the document to convert.</param>
    /// <param name="requestJson">Request JSON which must include the `documentId` field.</param>
    /// <returns>A JSON string with schema "cascode.structural/1.0" containing `documentId`, `revision`, and the serialized structural node.</returns>
    private static string ConvertToStructural(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var state = GetDocumentState(session, doc.RootElement.RequireString("documentId"));

        var render = SchematicDocumentBuilder.Build(
            state,
            RenderSchematicMode.RespectDocument,
            allowRelaxation: false
        );
        var response = new JsonObject
        {
            ["schema"] = "cascode.structural/1.0",
            ["documentId"] = state.DocumentId,
            ["revision"] = state.Revision,
            ["structural"] = ApiJson.SerializeStructuralNode(render.Structural),
        };

        return response.ToJsonString(ApiJson.Options);
    }

    /// <summary>
    /// Produces the cascode source representation for a document.
    /// </summary>
    /// <param name="requestJson">A JSON payload that must include a "documentId" field identifying the document to convert.</param>
    /// <returns>A JSON string with schema "cascode.source/1.0" containing the documentId, the document's revision, and the current sourceText.</returns>
    private static string ConvertToCas(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var state = GetDocumentState(session, doc.RootElement.RequireString("documentId"));

        return new JsonObject
        {
            ["schema"] = "cascode.source/1.0",
            ["documentId"] = state.DocumentId,
            ["revision"] = state.Revision,
            ["sourceText"] = state.SourceText,
        }.ToJsonString(ApiJson.Options);
    }

    /// <summary>
    /// Renders a document's schematic according to the provided request and returns the API response as JSON.
    /// </summary>
    /// <param name="session">The current session state containing documents and jobs.</param>
    /// <param name="requestJson">A JSON request that must include "documentId" and may include "mode", "allowConstraintRelaxation", and "persist".</param>
    /// <returns>A JSON string conforming to the "cascode.render/1.0" schema containing the rendered document node and the document's sourceText.</returns>
    /// <remarks>
    /// If the request specifies mode = auto or manual and persist = true, the selected circuit's render mode is rewritten in source and the document revision is incremented.
    /// </remarks>
    private static string RenderSchematic(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;

        var state = GetDocumentState(session, root.RequireString("documentId"));
        var mode = ParseRenderMode(root.TryGetString("mode"));
        var allowRelaxation = TryGetBool(root, "allowConstraintRelaxation") ?? false;
        var persist = TryGetBool(root, "persist") ?? false;

        if (mode != RenderSchematicMode.RespectDocument && persist)
        {
            var updated = CopyCircuitWithRender(
                FindCircuit(state),
                BuildPersistedRender(FindCircuit(state).Render, mode)
            );
            state.Document = ReplaceCircuit(state.Document, updated);
            state.SourceText = SerializeSource(state.Document);
            state.Revision++;
        }

        var render = SchematicDocumentBuilder.Build(state, mode, allowRelaxation);
        var response = new JsonObject
        {
            ["schema"] = "cascode.render/1.0",
            ["document"] = ApiJson.SerializeDocumentNode(render),
            ["sourceText"] = state.SourceText,
        };

        return response.ToJsonString(ApiJson.Options);
    }

    /// <summary>
    /// Applies a list of schematic operations to the named document, updates its state and source, and returns the new render.
    /// </summary>
    /// <param name="requestJson">JSON request containing "documentId", optional "baseRevision", and an "operations" array of schematic operations to apply.</param>
    /// <returns>A JSON string with schema "cascode.apply/1.0" containing the updated document render ("document") and the updated source text ("sourceText").</returns>
    private static string ApplyOperations(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;

        var state = GetDocumentState(session, root.RequireString("documentId"));
        EnsureRevision(state, TryGetInt(root, "baseRevision"));

        var changed = new HashSet<string>(StringComparer.Ordinal);
        var operations = root.TryGetProperty("operations", out var operationsEl)
            ? operationsEl.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();

        foreach (var operation in operations)
        {
            SchematicOperationApplier.Apply(state, operation, changed);
        }

        state.Revision++;
        state.ChangedEntities = changed.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        var render = SchematicDocumentBuilder.Build(
            state,
            RenderSchematicMode.RespectDocument,
            allowRelaxation: false
        );
        state.SourceText = SerializeSource(state.Document);

        var response = new JsonObject
        {
            ["schema"] = "cascode.apply/1.0",
            ["document"] = ApiJson.SerializeDocumentNode(render),
            ["sourceText"] = state.SourceText,
        };

        return response.ToJsonString(ApiJson.Options);
    }

    /// <summary>
    /// Starts a new background job and records it in the session state.
    /// </summary>
    /// <param name="session">Session state where the job entry will be stored.</param>
    /// <param name="requestJson">Request payload (ignored by this operation).</param>
    /// <returns>A JSON string with schema "cascode.job/1.0" containing the new jobId and state "running".</returns>
    private static string JobStart(SessionState session, string requestJson)
    {
        _ = requestJson;
        var id = $"job_{Guid.NewGuid():N}";
        session.Jobs[id] = new BenchJob
        {
            JobId = id,
            StartedAt = UtcNowProvider(),
            ProgressPercent = 0,
        };

        return new JsonObject
        {
            ["schema"] = "cascode.job/1.0",
            ["jobId"] = id,
            ["state"] = "running",
        }.ToJsonString(ApiJson.Options);
    }

    /// <summary>
    /// Polls a background job's progress by jobId, updates its progress and state based on elapsed time, and returns a job status payload.
    /// </summary>
    /// <param name="requestJson">JSON request containing the required field `jobId` (string).</param>
    /// <returns>A JSON string conforming to `cascode.job.poll/1.0` with fields: `jobId`, `state` (lowercase), and `progress` (0–100).</returns>
    /// <exception cref="ApiException">Thrown with code "CASAPI-INVALID-REQUEST" when the specified jobId does not exist.</exception>
    private static string JobPoll(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var jobId = doc.RootElement.RequireString("jobId");
        if (!session.Jobs.TryGetValue(jobId, out var job))
        {
            throw new ApiException("CASAPI-INVALID-REQUEST", $"Unknown job '{jobId}'.");
        }

        if (job.State == JobState.Running)
        {
            var elapsedMs = (UtcNowProvider() - job.StartedAt).TotalMilliseconds;
            job.ProgressPercent = Math.Clamp((int)(elapsedMs / 25), 0, 100);
            if (job.ProgressPercent >= 100)
            {
                job.State = JobState.Completed;
            }
        }

        return new JsonObject
        {
            ["schema"] = "cascode.job.poll/1.0",
            ["jobId"] = jobId,
            ["state"] = job.State.ToString().ToLowerInvariant(),
            ["progress"] = job.ProgressPercent,
        }.ToJsonString(ApiJson.Options);
    }

    /// <summary>
    /// Cancels a running job identified in the request JSON within the provided session.
    /// </summary>
    /// <param name="session">The session state containing jobs.</param>
    /// <param name="requestJson">A JSON string containing a required `jobId` field identifying the job to cancel.</param>
    /// <returns>A JSON string with schema "cascode.job.cancel/1.0" containing the `jobId` and `"ok": true`.</returns>
    private static string JobCancel(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var jobId = doc.RootElement.RequireString("jobId");
        if (session.Jobs.TryGetValue(jobId, out var job) && job.State == JobState.Running)
        {
            job.State = JobState.Cancelled;
        }

        return new JsonObject
        {
            ["schema"] = "cascode.job.cancel/1.0",
            ["jobId"] = jobId,
            ["ok"] = true,
        }.ToJsonString(ApiJson.Options);
    }

    /// <summary>
    /// Runs ERC on the specified document, linking includes using session search roots.
    /// </summary>
    private static string RunErc(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;
        var documentId = root.TryGetString("documentId") ?? "doc_1";
        var requirePdk = TryGetBool(root, "requirePdk") ?? false;

        var state = GetDocumentState(session, documentId);
        var searchRoots = session.GetSearchRoots();

        CascodeDocument linked;
        if (searchRoots.Count > 0 && state.Document.Includes.Count > 0)
        {
            var tmpDir = Path.Combine(
                Path.GetTempPath(),
                "cascode-erc",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(tmpDir);
            var tmpFile = Path.Combine(tmpDir, "entry.cas");
            File.WriteAllText(tmpFile, state.SourceText);
            var linkResult = CascodeLinker.LinkFile(
                tmpFile,
                tmpDir,
                searchRoots,
                CascodeLinkOptions.Default,
                null
            );

            if (!linkResult.Success || string.IsNullOrWhiteSpace(linkResult.LinkedCasPath))
            {
                var diagnosticsArray = new JsonArray(
                    linkResult.Diagnostics.Select(d => (JsonNode?)d.Message).ToArray()
                );
                return new JsonObject
                {
                    ["schema"] = "cascode.erc/1.0",
                    ["ok"] = false,
                    ["diagnostics"] = diagnosticsArray,
                }.ToJsonString(ApiJson.Options);
            }

            var linkedText = File.ReadAllText(linkResult.LinkedCasPath);
            var linkedRead = CascodeReader.TryParse(linkedText, linkResult.LinkedCasPath);
            linked =
                linkedRead.Success && linkedRead.Document is not null
                    ? linkedRead.Document
                    : state.Document;
        }
        else
        {
            linked = state.Document;
        }

        var combinedResult = ElectricalRuleChecker.Check(linked, requirePdk);

        var errors = new JsonArray();
        foreach (var error in combinedResult.GetErrors())
            errors.Add((JsonNode?)error.ToString());
        var warnings = new JsonArray();
        foreach (var warning in combinedResult.GetWarnings())
            warnings.Add((JsonNode?)warning.ToString());

        return new JsonObject
        {
            ["schema"] = "cascode.erc/1.0",
            ["ok"] = !combinedResult.HasErrors,
            ["errorCount"] = combinedResult.ErrorCount,
            ["warningCount"] = combinedResult.WarningCount,
            ["errors"] = errors,
            ["warnings"] = warnings,
        }.ToJsonString(ApiJson.Options);
    }

    /// <summary>
    /// Runs SPICE emit on the specified document, linking includes using session search roots.
    /// </summary>
    private static string RunEmit(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;
        var documentId = root.TryGetString("documentId") ?? "doc_1";

        var state = GetDocumentState(session, documentId);
        var searchRoots = session.GetSearchRoots();

        CascodeDocument linked;
        if (searchRoots.Count > 0 && state.Document.Includes.Count > 0)
        {
            var tmpDir = Path.Combine(
                Path.GetTempPath(),
                "cascode-emit",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(tmpDir);
            var tmpFile = Path.Combine(tmpDir, "entry.cas");
            File.WriteAllText(tmpFile, state.SourceText);
            var linkResult = CascodeLinker.LinkFile(
                tmpFile,
                tmpDir,
                searchRoots,
                CascodeLinkOptions.Default,
                null
            );

            if (!linkResult.Success || string.IsNullOrWhiteSpace(linkResult.LinkedCasPath))
            {
                var diagnosticsArray = new JsonArray(
                    linkResult.Diagnostics.Select(d => (JsonNode?)d.Message).ToArray()
                );
                return new JsonObject
                {
                    ["schema"] = "cascode.emit/1.0",
                    ["ok"] = false,
                    ["diagnostics"] = diagnosticsArray,
                }.ToJsonString(ApiJson.Options);
            }

            var linkedText = File.ReadAllText(linkResult.LinkedCasPath);
            var linkedRead = CascodeReader.TryParse(linkedText, linkResult.LinkedCasPath);
            linked =
                linkedRead.Success && linkedRead.Document is not null
                    ? linkedRead.Document
                    : state.Document;
        }
        else
        {
            linked = state.Document;
        }

        var outputDir = Path.Combine(
            Path.GetTempPath(),
            "cascode-emit-out",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(outputDir);

        var emitResult = SpiceEmitter.ValidateAndEmit(linked, outputDir);

        if (!emitResult.Success)
        {
            var validationErrors = new JsonArray();
            foreach (var error in emitResult.Validation.GetErrors())
                validationErrors.Add((JsonNode?)error.ToString());

            return new JsonObject
            {
                ["schema"] = "cascode.emit/1.0",
                ["ok"] = false,
                ["errors"] = validationErrors,
            }.ToJsonString(ApiJson.Options);
        }

        var files = new JsonArray();
        foreach (var f in emitResult.Emit.DesignPaths)
            files.Add((JsonNode?)f);
        foreach (var f in emitResult.Emit.TestbenchPaths)
            files.Add((JsonNode?)f);

        var netlist =
            emitResult.Emit.DesignPaths.Count > 0
                ? File.ReadAllText(emitResult.Emit.DesignPaths[0])
                : null;

        return new JsonObject
        {
            ["schema"] = "cascode.emit/1.0",
            ["ok"] = true,
            ["files"] = files,
            ["netlist"] = netlist,
        }.ToJsonString(ApiJson.Options);
    }

    private static string PdkSetDir(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var pdkRoot = doc.RootElement.RequireString("pdkRoot");
        session.PdkRoot = pdkRoot;

        return new JsonObject
        {
            ["schema"] = "cascode.pdk.setDir/1.0",
            ["ok"] = true,
            ["pdkRoot"] = pdkRoot,
        }.ToJsonString(ApiJson.Options);
    }

    private static string PdkScan(SessionState session, string requestJson)
    {
        _ = requestJson;
        if (string.IsNullOrWhiteSpace(session.PdkRoot))
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                "No PDK root configured. Call pdk.setDir first."
            );
        }

        var scanService = new PdkScanService();
        var result = scanService.ScanAndPersist(session.PdkRoot);

        return new JsonObject
        {
            ["schema"] = "cascode.pdk.scan/1.0",
            ["ok"] = true,
            ["pdkRoot"] = session.PdkRoot,
            ["libraryCount"] = result.WorkspaceScan.Libraries.Count,
            ["modelCount"] = result.WorkspaceScan.Models.Count,
            ["modelDeckCount"] = result.WorkspaceScan.ModelDecks.Count,
            ["dbPath"] = result.DatabasePath,
        }.ToJsonString(ApiJson.Options);
    }

    private static string PdkEmitPrimitives(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;
        var includeFixed = TryGetBool(root, "includeFixed") ?? false;

        if (string.IsNullOrWhiteSpace(session.PdkRoot))
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                "No PDK root configured. Call pdk.setDir first."
            );
        }

        if (string.IsNullOrWhiteSpace(session.WorkspaceRoot))
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                "No workspaceRoot configured for this session. Pass workspaceRoot in createSession options."
            );
        }

        var pdkName = Path.GetFileName(Path.GetFullPath(session.PdkRoot));
        if (string.IsNullOrWhiteSpace(pdkName))
        {
            pdkName = "pdk";
        }

        var dbPath = WorkspacePaths.GetDatabasePath(session.PdkRoot);
        var libraryDir = PdkPrimitiveLibraryLayout.GetLibraryDirectory(
            session.WorkspaceRoot,
            pdkName
        );
        var result = PdkEmitPrimitivesService.Emit(
            new PdkEmitPrimitivesService.EmitArgs(
                PdkName: pdkName,
                DbPath: dbPath,
                OutputDirectory: libraryDir,
                IncludeFixed: includeFixed
            )
        );

        var files = new JsonArray();
        foreach (var f in PdkPrimitiveLibraryLayout.GetExpectedCategoryPaths(libraryDir))
        {
            if (File.Exists(f))
            {
                files.Add((JsonNode?)f);
            }
        }

        return new JsonObject
        {
            ["schema"] = "cascode.pdk.emitPrimitives/1.0",
            ["ok"] = result.Succeeded,
            ["pdkRoot"] = session.PdkRoot,
            ["workspaceRoot"] = session.WorkspaceRoot,
            ["pdkName"] = pdkName,
            ["outputDirectory"] = libraryDir,
            ["primitivesWritten"] = result.PrimitivesWritten,
            ["message"] = result.Message,
            ["files"] = files,
        }.ToJsonString(ApiJson.Options);
    }

    /// <summary>
    /// Creates a minimal API response JSON object with the provided schema and an OK flag.
    /// </summary>
    /// <param name="schema">The schema identifier to include in the response (e.g. "cascode.job/1.0").</param>
    /// <returns>A JSON string containing the given schema and an `ok` field set to `true`.</returns>
    private static string PassThroughStub(string schema)
    {
        return new JsonObject { ["schema"] = schema, ["ok"] = true }.ToJsonString(ApiJson.Options);
    }

    /// <summary>
    /// Selects a circuit from the provided document, preferring the named circuit when one is given.
    /// </summary>
    /// <param name="document">The Cascode document to search for circuits.</param>
    /// <param name="requested">Optional name of the preferred circuit; if null or not found, a suitable EL/ML circuit is chosen.</param>
    /// <returns>The selected <see cref="Circuit"/>.</returns>
    /// <exception cref="ApiException">Thrown when no non-inline EL or ML circuit is available in the document.</exception>
    private static Circuit SelectCircuit(CascodeDocument document, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var byName = document.Circuits.FirstOrDefault(c => c.Name == requested);
            if (byName is not null)
            {
                return byName;
            }
        }

        var selected = document.Circuits.FirstOrDefault(c =>
            !c.Inline && c.Level is CascodeLevel.EL or CascodeLevel.ML
        );
        if (selected is null)
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                "No non-inline EL/ML circuit available."
            );
        }

        return selected;
    }

    /// <summary>
    /// Selects the circuit in the provided document whose name equals the state's CircuitName.
    /// </summary>
    /// <param name="state">The document state containing the document and the target CircuitName.</param>
    /// <returns>The circuit from the document that matches the state's CircuitName.</returns>
    /// <exception cref="ApiException">Thrown with code "CASAPI-INVALID-REQUEST" if no circuit with the specified name exists in the document.</exception>
    private static Circuit FindCircuit(DocumentState state)
    {
        var circuit = state.Document.Circuits.FirstOrDefault(c => c.Name == state.CircuitName);
        if (circuit is null)
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Circuit '{state.CircuitName}' was not found in document '{state.DocumentId}'."
            );
        }

        return circuit;
    }

    /// <summary>
    /// Creates a copy of the given CascodeDocument with the circuit that has the same name as <paramref name="updatedCircuit"/> replaced by <paramref name="updatedCircuit"/>.
    /// </summary>
    /// <param name="document">The original document to copy.</param>
    /// <param name="updatedCircuit">The circuit whose name identifies which circuit in the document will be replaced.</param>
    /// <returns>A new CascodeDocument identical to <paramref name="document"/> except its Circuits list contains <paramref name="updatedCircuit"/> in place of the circuit with the same name.</returns>
    private static CascodeDocument ReplaceCircuit(CascodeDocument document, Circuit updatedCircuit)
    {
        return new CascodeDocument
        {
            VersionMajor = document.VersionMajor,
            VersionMinor = document.VersionMinor,
            Includes = document.Includes,
            FileLibrary = document.FileLibrary,
            Functions = document.Functions,
            BundleTypes = document.BundleTypes,
            Traits = document.Traits,
            BenchDefinitions = document.BenchDefinitions,
            Primitives = document.Primitives,
            Circuits = document
                .Circuits.Select(c => c.Name == updatedCircuit.Name ? updatedCircuit : c)
                .ToList(),
        };
    }

    /// <summary>
    /// Creates a copy of the given circuit with its Render property replaced by the supplied render block.
    /// </summary>
    /// <param name="source">The original circuit to copy.</param>
    /// <param name="render">The render block to set on the copied circuit, or null to omit any render.</param>
    /// <returns>A new Circuit whose fields match <paramref name="source"/> except that <see cref="Circuit.Render"/> is set to <paramref name="render"/>.</returns>
    private static Circuit CopyCircuitWithRender(Circuit source, RenderBlock? render)
    {
        return new Circuit
        {
            Name = source.Name,
            Traits = source.Traits,
            Level = source.Level,
            Inline = source.Inline,
            Package = source.Package,
            Parameters = source.Parameters,
            Sizes = source.Sizes,
            Supplies = source.Supplies,
            Grounds = source.Grounds,
            Ports = source.Ports,
            Slot = source.Slot,
            Fill = source.Fill,
            Constraints = source.Constraints,
            Harness = source.Harness,
            Env = source.Env,
            Render = render,
            BenchBindings = source.BenchBindings,
            BenchBindingExtensions = source.BenchBindingExtensions,
            Synth = source.Synth,
            Provenance = source.Provenance,
        };
    }

    /// <summary>
    /// Retrieves the DocumentState associated with the given document identifier from the session.
    /// </summary>
    /// <param name="session">The session containing documents.</param>
    /// <param name="documentId">The identifier of the document to retrieve.</param>
    /// <returns>The DocumentState for the specified document identifier.</returns>
    /// <exception cref="ApiException">Thrown with code "CASAPI-INVALID-REQUEST" if no document with the given identifier exists in the session.</exception>
    private static DocumentState GetDocumentState(SessionState session, string documentId)
    {
        if (!session.Documents.TryGetValue(documentId, out var state))
        {
            throw new ApiException("CASAPI-INVALID-REQUEST", $"Unknown document '{documentId}'.");
        }

        return state;
    }

    /// <summary>
    /// Validates a Cascode parse result and throws an ApiException containing diagnostic details when parsing failed.
    /// </summary>
    /// <param name="read">The Cascode read result containing Success and Diagnostics information.</param>
    /// <exception cref="ApiException">Thrown with code "CASAPI-PARSE-FAILED" and a JSON payload of line, column, and diagnostics when <paramref name="read"/> indicates a parse failure.</exception>
    private static void EnsureParseSuccess(CascodeReadResult read)
    {
        if (read.Success)
        {
            return;
        }

        var first = read.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        throw new ApiException(
            "CASAPI-PARSE-FAILED",
            first?.Message ?? "Failed to parse Cascode source.",
            new JsonObject
            {
                ["line"] = first?.Line,
                ["column"] = first?.Column,
                ["diagnostics"] = new JsonArray(
                    read.Diagnostics.Select(d => (JsonNode?)d.Message).ToArray()
                ),
            }
        );
    }

    /// <summary>
    /// Validates that the provided base revision matches the document's current revision.
    /// </summary>
    /// <param name="state">The document state whose revision is validated.</param>
    /// <param name="baseRevision">The expected base revision; if null the check is skipped.</param>
    /// <exception cref="ApiException">Thrown when <paramref name="baseRevision"/> is not null and does not equal <paramref name="state"/>.Revision. The exception uses code "CASAPI-REVISION-CONFLICT" and includes JSON details with `currentRevision` and `changedEntities`.</exception>
    private static void EnsureRevision(DocumentState state, int? baseRevision)
    {
        if (baseRevision is null || baseRevision.Value == state.Revision)
        {
            return;
        }

        throw new ApiException(
            "CASAPI-REVISION-CONFLICT",
            "Base revision does not match current document revision.",
            new JsonObject
            {
                ["currentRevision"] = state.Revision,
                ["changedEntities"] = new JsonArray(
                    state.ChangedEntities.Select(name => (JsonNode?)name).ToArray()
                ),
            }
        );
    }

    /// <summary>
    /// Serializes a CascodeDocument to its source text representation.
    /// </summary>
    /// <returns>The document serialized as Cascode source text.</returns>
    private static string SerializeSource(CascodeDocument document)
    {
        using var writer = new StringWriter();
        CascodeWriter.Write(document, writer);
        return writer.ToString();
    }

    /// <summary>
    /// Parses a render mode string into a <see cref="RenderSchematicMode"/> value.
    /// </summary>
    /// <param name="raw">Mode string to parse (case-insensitive); may be null.</param>
    /// <returns>`manual`, `auto`, or `RespectDocument` when the request omits a mode override.</returns>
    private static RenderSchematicMode ParseRenderMode(string? raw)
    {
        return raw?.ToLowerInvariant() switch
        {
            "manual" => RenderSchematicMode.Manual,
            "auto" => RenderSchematicMode.Auto,
            _ => RenderSchematicMode.RespectDocument,
        };
    }

    private static RenderBlock? BuildPersistedRender(RenderBlock? render, RenderSchematicMode mode)
    {
        return mode switch
        {
            RenderSchematicMode.Manual when render is null => new RenderBlock
            {
                Mode = RenderLayoutMode.Manual,
            },
            RenderSchematicMode.Manual when render is not null => new RenderBlock
            {
                Mode = RenderLayoutMode.Manual,
                Entities = render.Entities.ToList(),
            },
            RenderSchematicMode.Auto when render is not null => new RenderBlock
            {
                Mode = RenderLayoutMode.Auto,
                Entities = render.Entities.ToList(),
            },
            RenderSchematicMode.Auto => null,
            _ => render,
        };
    }

    /// <summary>
    /// Retrieves the named property from a JSON element and returns its 32-bit integer value when present and representable.
    /// </summary>
    /// <param name="element">The JSON element to read the property from.</param>
    /// <param name="name">The property name to look up.</param>
    /// <returns>The property's `int` value if the property exists and can be represented as a 32-bit integer, `null` otherwise.</returns>
    private static int? TryGetInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var child) && child.TryGetInt32(out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Retrieves a boolean property by name from a JSON element, distinguishing explicit true/false from missing or non-boolean values.
    /// </summary>
    /// <param name="element">The JSON element to read the property from (typically an object).</param>
    /// <param name="name">The property name to look up.</param>
    /// <returns>`true` if the property exists and is JSON true, `false` if it exists and is JSON false, `null` if the property is missing or not a JSON boolean.</returns>
    private static bool? TryGetBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.True
                ? true
            : element.TryGetProperty(name, out child) && child.ValueKind == JsonValueKind.False
                ? false
            : null;
    }
}
