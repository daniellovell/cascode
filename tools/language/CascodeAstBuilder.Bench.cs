using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

internal sealed partial class CascodeAstBuilder
{
    private FillBlock BuildFillBlock(CascodeParser.FillBlockContext ctx)
    {
        var fill = new FillBlock();

        foreach (var stmtCtx in ctx.fillStatement())
        {
            switch (stmtCtx)
            {
                case CascodeParser.FillNetDeclContext netCtx:
                    fill.Nets.Add(
                        new NetDeclaration
                        {
                            Id = netCtx.IDENT().GetText(),
                            Domain = BuildPortType(netCtx.portType()),
                        }
                    );
                    break;

                case CascodeParser.FillSizeDeclContext sizeCtx:
                    fill.Sizes.Add(
                        new SizeDeclaration
                        {
                            Name = sizeCtx.sizeName.Text,
                            Default = BuildSizeExpression(sizeCtx.sizeExpr(), sizeCtx),
                        }
                    );
                    break;

                case CascodeParser.FillDeviceDeclContext deviceCtx:
                    fill.Devices.Add(BuildDevice(deviceCtx.deviceDecl()));
                    break;

                case CascodeParser.FillInstanceStatementContext instanceCtx:
                    fill.Instances.Add(
                        BuildInstance(instanceCtx.fillInstanceDecl().instanceDecl())
                    );
                    break;

                case CascodeParser.FillAttachDeclContext attachCtx:
                    fill.Attaches.Add(BuildAttach(attachCtx));
                    break;

                case CascodeParser.FillConnectDeclContext connectCtx:
                    var pins = connectCtx.pinRef();
                    fill.Connections.Add(
                        new ConnectionStatement
                        {
                            From = BuildPinRef(pins[0]),
                            To = BuildPinRef(pins[1]),
                        }
                    );
                    break;

                default:
                    AddDiagnostic(
                        stmtCtx,
                        DiagnosticSeverity.Error,
                        $"CAS2011: Unsupported fill statement: '{stmtCtx.GetText()}'"
                    );
                    break;
            }
        }

        return fill;
    }

    private IReadOnlyList<BenchBinding> BuildBenchBindings(
        IEnumerable<CascodeParser.BenchBindingContext> bindingContexts
    )
    {
        var bindings = new List<BenchBinding>();

        foreach (var bindingCtx in bindingContexts)
        {
            var binding = new BenchBinding
            {
                BenchName = bindingCtx.benchName.Text,
                BindingName = bindingCtx.bindingName.Text,
            };

            binding.Metrics = ExtractBindingStatements(
                bindingCtx.bindingStatement(),
                binding.Statements
            );

            bindings.Add(binding);
        }

        return bindings;
    }

    private MetricsBlock? ExtractBindingStatements(
        IEnumerable<CascodeParser.BindingStatementContext> statements,
        List<BenchBindingStatement> target
    )
    {
        ProcessBindingStatements(statements, target, out var metrics);
        return metrics;
    }

    private void ProcessBindingStatements(
        IEnumerable<CascodeParser.BindingStatementContext> statements,
        List<BenchBindingStatement> target,
        out MetricsBlock? metrics
    )
    {
        metrics = null;
        foreach (var stmt in statements)
        {
            if (stmt.terminalMapping() is not null)
            {
                var t = stmt.terminalMapping();
                target.Add(
                    new BenchTerminalMapping(
                        BenchTerminal: t.IDENT().GetText(),
                        DutPinRef: BuildPinRef(t.pinRef())
                    )
                );
                continue;
            }

            if (stmt.bindingMetricsBlock() is not null)
            {
                metrics = BuildMetricsBlock(stmt.bindingMetricsBlock());
                continue;
            }

            if (stmt.bindingMeasurementsBlock() is not null)
            {
                foreach (var decl in stmt.bindingMeasurementsBlock().bindingMeasurementDecl())
                {
                    var parameters = new List<TypedParameter>();
                    if (decl.typedParamList() is not null)
                    {
                        foreach (var p in decl.typedParamList().typedParam())
                        {
                            parameters.Add(
                                new TypedParameter(
                                    ParseTypedParamType(p.typedParamType()),
                                    p.idPart().GetText()
                                )
                            );
                        }
                    }

                    target.Add(
                        new BenchBindingMeasurementExport(
                            Name: decl.name.Text,
                            Parameters: parameters,
                            Unit: decl.unitType().GetText(),
                            Target: BuildBenchMeasurementRef(decl.benchMeasurementRef())
                        )
                    );
                }
                continue;
            }

            if (stmt.dutConnection() is not null)
            {
                var c = stmt.dutConnection();
                target.Add(
                    new BenchDutConnection(
                        DutPinRef: BuildPinRef(c.pinRef(0)),
                        PinRef: BuildPinRef(c.pinRef(1))
                    )
                );
                continue;
            }

            if (stmt.instanceDecl() is not null)
            {
                target.Add(new BenchBindingInstance(BuildInstance(stmt.instanceDecl())));
            }
        }
    }

    private IReadOnlyList<BenchBindingExtension> BuildBenchExtensions(
        IEnumerable<CascodeParser.BenchExtensionContext> extensionContexts
    )
    {
        var extensions = new List<BenchBindingExtension>();

        foreach (var extCtx in extensionContexts)
        {
            var ext = new BenchBindingExtension { BindingName = extCtx.bindingName.Text };

            ext.Metrics = ExtractBindingStatements(extCtx.bindingStatement(), ext.Statements);

            extensions.Add(ext);
        }

        return extensions;
    }

    private EnvBlock BuildEnvBlock(CascodeParser.EnvSectionContext ctx)
    {
        var env = new EnvBlock();
        foreach (var stmt in ctx.envStatement())
        {
            var key = stmt.IDENT().GetText();
            var value = stmt.envValue().GetText();
            env.Entries[key] = value;
        }

        return env;
    }

    private SynthBlock BuildSynthBlock(CascodeParser.SynthSectionContext ctx)
    {
        var synth = new SynthBlock();
        foreach (var entry in ctx.synthEntry())
        {
            var key = entry.IDENT(0).GetText();
            var value = entry.GetChild(2).GetText();
            synth.Entries[key] = value;
        }

        return synth;
    }

    private FunctionDefinition BuildFunctionDefinition(CascodeParser.FunctionDefContext ctx)
    {
        var def = new FunctionDefinition
        {
            Name = ctx.name.Text,
            ReturnType = ParseReturnType(ctx.returnType()),
        };

        if (ctx.typedParamList() is not null)
        {
            foreach (var p in ctx.typedParamList().typedParam())
            {
                def.Parameters.Add(
                    new TypedParameter(
                        ParseTypedParamType(p.typedParamType()),
                        p.idPart().GetText()
                    )
                );
            }
        }

        foreach (var stmt in ctx.functionBody().statement())
        {
            def.Body.Add(BuildBenchStatement(stmt));
        }

        return def;
    }

    private List<AnalysisDeclaration> BuildAnalysisBlock(CascodeParser.AnalysisBlockContext ctx)
    {
        var analyses = new List<AnalysisDeclaration>();

        foreach (var decl in ctx.analysisDecl())
        {
            var analysis = new AnalysisDeclaration
            {
                Type = ParseAnalysisType(decl.analysisType(0)),
                Name = decl.name.Text,
            };

            var analysisParams = decl.analysisParams();
            if (analysisParams is not null)
            {
                foreach (var p in analysisParams.analysisParam())
                {
                    analysis.Parameters[p.idPart().GetText()] = BuildConditionalExpr(
                        p.conditionalExpr()
                    );
                }
            }

            analyses.Add(analysis);
        }

        return analyses;
    }

    private List<MeasurementDefinition> BuildMeasurementsBlock(
        CascodeParser.MeasurementsBlockContext ctx
    )
    {
        var measurements = new List<MeasurementDefinition>();

        foreach (var decl in ctx.measurementDecl())
        {
            measurements.Add(BuildMeasurementDefinition(decl));
        }

        return measurements;
    }

    private MeasurementDefinition BuildMeasurementDefinition(
        CascodeParser.MeasurementDeclContext decl
    )
    {
        var measurement = new MeasurementDefinition
        {
            Name = decl.name.GetText(),
            IsOverride = decl.OVERRIDE_KW() is not null,
            Unit = decl.unitType().GetText(),
        };

        if (decl.typedParamList() is not null)
        {
            foreach (var p in decl.typedParamList().typedParam())
            {
                measurement.Parameters.Add(
                    new TypedParameter(
                        ParseTypedParamType(p.typedParamType()),
                        p.idPart().GetText()
                    )
                );
            }
        }

        foreach (var stmt in decl.measurementBody().statement())
        {
            measurement.Body.Add(BuildBenchStatement(stmt));
        }

        return measurement;
    }

    private BenchStatement BuildBenchStatement(CascodeParser.StatementContext ctx)
    {
        if (ctx.variableDecl() is not null)
        {
            var v = ctx.variableDecl();
            return new BenchVarDecl(
                Type: ParseTypedParamType(v.typedParamType()),
                Name: v.IDENT().GetText(),
                Expr: BuildMeasurementExpr(v.measurementExpr())
            );
        }

        if (ctx.ifStatement() is not null)
        {
            var i = ctx.ifStatement();
            var thenBody = i.statement().Select(BuildBenchStatement).ToList();

            // The grammar flattens then/else statements; the first block is always then, optional second is else.
            // We rely on token structure: IF <cond> { then* } (ELSE { else* })?
            // Generated contexts expose nested statement lists as a single array; we rebuild using child ranges.
            //
            // Instead of fragile child-walking, split via braces by using the parse tree children.
            // The builder below is conservative: if an else block exists, it is the last brace-delimited block.
            IReadOnlyList<BenchStatement>? elseBody = null;
            if (i.ELSE_KW() is not null)
            {
                // Parse-tree structure: IF cond { thenStmts... } ELSE { elseStmts... }
                // In the generated context, statement() includes both then and else statements.
                // We re-parse by scanning children for brace blocks.
                var blocks = SplitBraceBlocks(i.children);
                if (blocks.Count == 2)
                {
                    thenBody = blocks[0].Select(BuildBenchStatement).ToList();
                    elseBody = blocks[1].Select(BuildBenchStatement).ToList();
                }
            }

            return new BenchIf(BuildBoolExpr(i.boolExpr()), thenBody, elseBody);
        }

        if (ctx.returnStatement() is not null)
        {
            var r = ctx.returnStatement();
            return new BenchReturn(BuildMeasurementExpr(r.measurementExpr()));
        }

        throw new InvalidOperationException($"Unhandled bench statement: {ctx.GetText()}");
    }

    private static List<List<CascodeParser.StatementContext>> SplitBraceBlocks(
        IList<Antlr4.Runtime.Tree.IParseTree> children
    )
    {
        // We want to pull out the lists of `statement` contexts that are directly contained in each
        // brace-delimited block. This avoids relying on `statement()` flattening behavior.
        var blocks = new List<List<CascodeParser.StatementContext>>();
        List<CascodeParser.StatementContext>? current = null;
        foreach (var child in children)
        {
            if (child is Antlr4.Runtime.Tree.ITerminalNode t)
            {
                var sym = t.Symbol.Type;
                if (sym == CascodeParser.LBRACE)
                {
                    current = new List<CascodeParser.StatementContext>();
                }
                else if (sym == CascodeParser.RBRACE)
                {
                    if (current is not null)
                    {
                        blocks.Add(current);
                        current = null;
                    }
                }
                continue;
            }

            if (current is not null && child is CascodeParser.StatementContext s)
            {
                current.Add(s);
            }
        }

        return blocks;
    }

    private MeasurementExpr BuildConditionalExpr(CascodeParser.ConditionalExprContext ctx)
    {
        if (ctx.ifExpr() is null)
        {
            return BuildMeasurementExpr(ctx.measurementExpr());
        }

        return BuildIfExpr(ctx.ifExpr());
    }

    private MeasurementExpr BuildIfExpr(CascodeParser.IfExprContext ctx)
    {
        return new MeasurementConditional(
            Condition: BuildBoolExpr(ctx.boolExpr()),
            ThenExpr: BuildMeasurementExpr(ctx.measurementExpr(0)),
            ElseExpr: BuildMeasurementExpr(ctx.measurementExpr(1))
        );
    }

    private BoolExpr BuildBoolExpr(CascodeParser.BoolExprContext ctx)
    {
        if (ctx.scopedAccess() is not null)
        {
            return new BoolExists(BuildScopedValueRef(ctx.scopedAccess()));
        }

        if (ctx.pathAccess() is not null)
        {
            return new BoolTruthy(new MeasurementPath(ctx.pathAccess().GetText()));
        }

        var op = ParseComparisonOp(ctx.COMPARISON_OP().GetText());
        return new BoolCompare(
            op,
            BuildMeasurementExpr(ctx.measurementExpr(0)),
            BuildMeasurementExpr(ctx.measurementExpr(1))
        );
    }

    private MeasurementExpr BuildMeasurementExpr(CascodeParser.MeasurementExprContext ctx)
    {
        if (ctx.measurementExpr() is null)
        {
            return BuildMulMeasurementExpr(ctx.mulMeasurementExpr());
        }

        var op = ctx.PLUS() is not null ? "+" : "-";
        return new MeasurementBinary(
            op,
            BuildMeasurementExpr(ctx.measurementExpr()),
            BuildMulMeasurementExpr(ctx.mulMeasurementExpr())
        );
    }

    private MeasurementExpr BuildMulMeasurementExpr(CascodeParser.MulMeasurementExprContext ctx)
    {
        if (ctx.mulMeasurementExpr() is null)
        {
            return BuildUnaryMeasurementExpr(ctx.unaryMeasurementExpr());
        }

        var op = ctx.STAR() is not null ? "*" : "/";
        return new MeasurementBinary(
            op,
            BuildMulMeasurementExpr(ctx.mulMeasurementExpr()),
            BuildUnaryMeasurementExpr(ctx.unaryMeasurementExpr())
        );
    }

    private MeasurementExpr BuildUnaryMeasurementExpr(CascodeParser.UnaryMeasurementExprContext ctx)
    {
        if (ctx.MINUS() is null)
        {
            return BuildMeasurementPostfix(ctx.measurementPostfix());
        }

        return new MeasurementUnary("-", BuildUnaryMeasurementExpr(ctx.unaryMeasurementExpr()));
    }

    private MeasurementExpr BuildMeasurementPostfix(CascodeParser.MeasurementPostfixContext ctx)
    {
        var expr = BuildMeasurementPrimary(ctx.measurementPrimary());
        foreach (var suffix in ctx.methodCallSuffix())
        {
            var args = new List<MeasurementCallArg>();
            if (suffix.measurementArgList() is not null)
            {
                foreach (var arg in suffix.measurementArgList().measurementArg())
                {
                    if (arg.idPart() is not null)
                    {
                        args.Add(
                            new MeasurementCallArg(
                                arg.idPart().GetText(),
                                BuildMeasurementExpr(arg.measurementExpr())
                            )
                        );
                    }
                    else
                    {
                        args.Add(
                            new MeasurementCallArg(
                                null,
                                BuildMeasurementExpr(arg.measurementExpr())
                            )
                        );
                    }
                }
            }

            expr = new MeasurementMethodCall(expr, suffix.idPart().GetText(), args);
        }

        return expr;
    }

    /// <summary>
    /// Builds a MeasurementExpr node from a measurement primary parse context.
    /// </summary>
    /// <returns>A MeasurementExpr that represents the primary measurement expression described by the context.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the context contains an unsupported measurement primary; the exception message contains the offending text.</exception>
    private MeasurementExpr BuildMeasurementPrimary(CascodeParser.MeasurementPrimaryContext ctx)
    {
        if (ctx.ifExpr() is not null)
        {
            return BuildIfExpr(ctx.ifExpr());
        }

        if (ctx.measurementExpr() is not null)
        {
            return BuildMeasurementExpr(ctx.measurementExpr());
        }

        if (ctx.benchMeasurementRef() is not null)
        {
            return BuildBenchMeasurementRef(ctx.benchMeasurementRef());
        }

        if (ctx.measurementFunctionCall() is not null)
        {
            var call = ctx.measurementFunctionCall();
            var args = new List<MeasurementCallArg>();
            if (call.measurementArgList() is not null)
            {
                foreach (var arg in call.measurementArgList().measurementArg())
                {
                    if (arg.idPart() is not null)
                    {
                        args.Add(
                            new MeasurementCallArg(
                                arg.idPart().GetText(),
                                BuildMeasurementExpr(arg.measurementExpr())
                            )
                        );
                    }
                    else
                    {
                        args.Add(
                            new MeasurementCallArg(
                                null,
                                BuildMeasurementExpr(arg.measurementExpr())
                            )
                        );
                    }
                }
            }

            return new MeasurementCall(call.idPart().GetText(), args);
        }

        if (ctx.scopedAccess() is not null)
        {
            return new MeasurementScopedAccess(BuildScopedValueRef(ctx.scopedAccess()));
        }

        if (ctx.dutAccess() is not null)
        {
            return new MeasurementDutAccess(BuildPinRef(ctx.dutAccess().pinRef()));
        }

        if (ctx.pathAccess() is not null)
        {
            return new MeasurementPath(ctx.pathAccess().GetText());
        }

        if (ctx.NUMBER() is not null)
        {
            return new MeasurementNumber(ctx.NUMBER().GetText());
        }

        if (ctx.QUANTITY() is not null)
        {
            return new MeasurementQuantity(ctx.QUANTITY().GetText());
        }

        throw new InvalidOperationException($"Unsupported measurement primary: {ctx.GetText()}");
    }

    private MeasurementBenchMeasurementRef BuildBenchMeasurementRef(
        CascodeParser.BenchMeasurementRefContext r
    )
    {
        var args = new List<BenchMeasurementRefArg>();
        if (r.measurementArgList() is not null)
        {
            foreach (var arg in r.measurementArgList().measurementArg())
            {
                var name = arg.idPart() is null ? null : arg.idPart().GetText();
                var text = arg.measurementExpr().GetText();
                args.Add(
                    new BenchMeasurementRefArg(
                        name,
                        text,
                        BuildMeasurementExpr(arg.measurementExpr())
                    )
                );
            }
        }

        return new MeasurementBenchMeasurementRef(r.IDENT().GetText(), r.idPart().GetText(), args);
    }

    private ScopedValueRef BuildScopedValueRef(CascodeParser.ScopedAccessContext ctx)
    {
        if (ctx.ENV_KW() is not null)
        {
            return new ScopedValueRef(MeasurementScope.Env, ctx.IDENT().GetText());
        }

        if (ctx.CONSTRAINTS_KW() is not null)
        {
            return new ScopedValueRef(MeasurementScope.Constraints, ctx.IDENT().GetText());
        }

        return new ScopedValueRef(MeasurementScope.Harness, BuildPinRef(ctx.pinRef()));
    }

    private static ComparisonOp ParseComparisonOp(string raw) =>
        raw switch
        {
            ">=" => ComparisonOp.Gte,
            "<=" => ComparisonOp.Lte,
            ">" => ComparisonOp.Gt,
            "<" => ComparisonOp.Lt,
            "==" => ComparisonOp.Eq,
            _ => throw new InvalidOperationException($"Unknown comparison operator: {raw}"),
        };

    private static BenchValueType ParseReturnType(CascodeParser.ReturnTypeContext ctx)
    {
        if (ctx.BOOL_KW() is not null)
        {
            return BenchValueType.Bool;
        }

        return ParsePhysicalType(ctx.physicalType());
    }

    private static BenchValueType ParseTypedParamType(CascodeParser.TypedParamTypeContext ctx)
    {
        if (ctx.physicalType() is not null)
        {
            return ParsePhysicalType(ctx.physicalType());
        }

        if (ctx.analysisType() is not null)
        {
            return ParseAnalysisType(ctx.analysisType());
        }

        // Allow 'stim'/'resp' as a generic terminal value type.
        return ctx.GetText() switch
        {
            "stim" => BenchValueType.Terminal,
            "resp" => BenchValueType.Terminal,
            "port" => BenchValueType.Terminal,
            _ => throw new InvalidOperationException(
                $"Unknown typed parameter type: {ctx.GetText()}"
            ),
        };
    }

    private static BenchValueType ParsePhysicalType(CascodeParser.PhysicalTypeContext ctx)
    {
        return ctx.GetText() switch
        {
            "Frequency" => BenchValueType.Frequency,
            "VoltageRatio" => BenchValueType.VoltageRatio,
            "TransferFunction" => BenchValueType.TransferFunction,
            "GainSpectrum" => BenchValueType.GainSpectrum,
            "ScalarSpectrum" => BenchValueType.ScalarSpectrum,
            "PhaseSpectrum" => BenchValueType.PhaseSpectrum,
            "TimeSpectrum" => BenchValueType.TimeSpectrum,
            "ComplexVoltageSpectrum" => BenchValueType.ComplexVoltageSpectrum,
            "ComplexCurrentSpectrum" => BenchValueType.ComplexCurrentSpectrum,
            "VoltageSpectrum" => BenchValueType.VoltageSpectrum,
            "CurrentSpectrum" => BenchValueType.CurrentSpectrum,
            "NoiseSpectrum" => BenchValueType.NoiseSpectrum,
            "ImpedanceSpectrum" => BenchValueType.ImpedanceSpectrum,
            "VoltageWaveform" => BenchValueType.VoltageWaveform,
            "CurrentWaveform" => BenchValueType.CurrentWaveform,
            "NoiseSpectralDensity" => BenchValueType.NoiseSpectralDensity,
            "IntegratedNoise" => BenchValueType.IntegratedNoise,
            "ElementPin" => BenchValueType.ElementPin,
            "Impedance" => BenchValueType.Impedance,
            "Capacitance" => BenchValueType.Capacitance,
            "Inductance" => BenchValueType.Inductance,
            "Voltage" => BenchValueType.Voltage,
            "Current" => BenchValueType.Current,
            "Time" => BenchValueType.Time,
            "Phase" => BenchValueType.Phase,
            "Scalar" => BenchValueType.Scalar,
            "SParameterMatrix" => BenchValueType.SParameterMatrix,
            _ => throw new InvalidOperationException($"Unknown physical type: {ctx.GetText()}"),
        };
    }

    private static BenchValueType ParseAnalysisType(CascodeParser.AnalysisTypeContext ctx)
    {
        return ctx.GetText() switch
        {
            "ACAnalysis" => BenchValueType.ACAnalysis,
            "DCAnalysis" => BenchValueType.DCAnalysis,
            "TranAnalysis" => BenchValueType.TranAnalysis,
            "NoiseAnalysis" => BenchValueType.NoiseAnalysis,
            "STBAnalysis" => BenchValueType.STBAnalysis,
            "SPAnalysis" => BenchValueType.SPAnalysis,
            _ => throw new InvalidOperationException($"Unknown analysis type: {ctx.GetText()}"),
        };
    }
}
