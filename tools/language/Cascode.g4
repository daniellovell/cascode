grammar Cascode;

@lexer::members {
    // We need to distinguish terminal-prefix '.' (start of a binding on its own line)
    // from '.' used as a path separator. Newlines are otherwise skipped, so without
    // this, a binding like ".VDD--VDD" can incorrectly consume ".IN" from the next
    // line as a continuation of the RHS pinRef (e.g., "VDD.IN").
    private bool _atLineStart = true;

    public override IToken Emit()
    {
        _atLineStart = false;
        return base.Emit();
    }
}

// ============================================================================
// Parser Rules
// ============================================================================

// Document: optional version followed by top-level declarations.
// Empty documents (no version) are allowed for compatibility.
document
    : versionDecl? topLevelDecl* EOF
    ;

topLevelDecl
    : includeDecl
    | filePackageDecl
    | bundleDef
    | interfaceDef
    | benchDef
    | functionDef
    | wrapSpiceDef
    | primitiveDef
    | circuit
    ;

// File-level library/package annotation. This is primarily metadata today but must parse
// because standard library files use it.
filePackageDecl
    : PACKAGE_KW qualifiedName
    ;

includeDecl
    : INCLUDE_KW qualifiedName
    ;

// Version declaration like "VERSION 5.0"
versionDecl
    : VERSION_KW NUMBER
    ;

// ----------------------------------------------------------------------------
// Bundle definitions
// ----------------------------------------------------------------------------

bundleDef
    : BUNDLE_KW name=IDENT LBRACE bundleField* RBRACE
    ;

bundleField
    : IDENT COLON portType
    ;

// ----------------------------------------------------------------------------
// Interface definitions
// ----------------------------------------------------------------------------

interfaceDef
    : INTERFACE_KW name=IDENT LBRACE interfaceMember* RBRACE
    ;

interfaceMember
    : direction portName COLON portType                             # InterfacePort
    | SUPPLY_KW IDENT                                               # InterfaceSupply
    | GROUND_KW IDENT                                               # InterfaceGround
    | CONNECTORS_KW LBRACE connectorDef* RBRACE                      # InterfaceConnectors
    | interfaceBenchesSection                                        # InterfaceBenches
    ;

connectorDef
    : TO_KW IDENT LBRACE connectorMapping* RBRACE
    ;

connectorMapping
    : pinRef WIRE_OP pinRef
    ;

// ----------------------------------------------------------------------------
// Bench definitions
// ----------------------------------------------------------------------------

benchDef
    : ABSTRACT_KW? BENCH_KW name=IDENT benchParamList? (EXTENDS_KW base=IDENT)? LBRACE benchBody RBRACE
    ;

benchParamList
    : LPAREN benchParamDecl (COMMA benchParamDecl)* RPAREN
    ;

benchParamDecl
    : physicalType name=IDENT (EQ measurementExpr)?
    ;

benchBody
    : terminalDecl* benchMember*
    ;

benchMember
    : fillBlock
    | functionDef
    | analysisBlock
    | measurementsBlock
    | measurementDecl
    ;

terminalDecl
    : ABSTRACT_KW? terminalRole IDENT (COLON terminalType)?
    ;

terminalRole
    : STIM_KW
    | RESP_KW
    ;

terminalType
    : IDENT
    | BIAS_KW
    | SUPPLY_KW
    | GROUND_KW
    | ANALOG_KW
    | DIGITAL_KW
    | MIXED_KW
    | CLOCK_KW
    | RF_KW
    ;

// ----------------------------------------------------------------------------
// Primitive definitions
// ----------------------------------------------------------------------------

primitiveDef
    : PRIMITIVE_KW DEVICE_TYPE name=IDENT LPAREN paramList? RPAREN LBRACE primitiveBody RBRACE
    ;

primitiveBody
    : deviceDirective paramsBlock
    ;

deviceDirective
    : DEVICE_KW STRING
    ;

paramsBlock
    : PARAMS_KW LBRACE paramMapping+ RBRACE
    ;

paramMapping
    : IDENT EQ paramExpr
    ;

paramExpr
    : sizeFieldAccess
    | expr
    ;

sizeFieldAccess
    : IDENT DOT IDENT
    ;

// ----------------------------------------------------------------------------
// Circuit definitions
// ----------------------------------------------------------------------------

circuit
    : CIRCUIT_KW name=IDENT paramSignature? implementsClause? LBRACE circuitMember* RBRACE
    ;

paramSignature
    : LPAREN paramList RPAREN
    ;

implementsClause
    : IMPLEMENTS_KW interfaceList
    ;

interfaceList
    : IDENT (COMMA IDENT)*
    ;

circuitMember
    : LEVEL_KW levelValue                                           # LevelDecl
    | INLINE_KW                                                     # InlineDecl
    | PACKAGE_KW qualifiedName                                      # PackageDecl
    | SUPPLY_KW IDENT                                               # SupplyDecl
    | GROUND_KW IDENT                                               # GroundDecl
    | direction portName COLON portType                             # PortDecl
    | SLOT_KW                                                       # BareSlotMember
    | SLOT_KW LBRACE slotBlockStatement* RBRACE                      # SlotBlockMember
    | FILL_KW LBRACE fillStatement* RBRACE                          # FillSection
    | CONSTRAINTS_KW LBRACE constraintSection* RBRACE               # ConstraintsSection
    | HARNESS_KW LBRACE harnessStatement* RBRACE                    # HarnessSection
    | ENV_KW LBRACE envStatement* RBRACE                            # EnvSection
    | RENDER_KW LBRACE renderEntity* RBRACE                         # RenderSection
    | circuitBenchesSection                                         # CircuitBenches
    | SYNTH_KW LBRACE synthEntry* RBRACE                            # SynthSection
    | PROVENANCE_KW LBRACE provenanceEntry* RBRACE                  # ProvenanceSection
    ;

levelValue
    : HL_KW | ML_KW | EL_KW
    ;

direction
    : INPUT_KW
    | OUTPUT_KW
    | IO_KW
    ;

// Port names can have dots (e.g., OUT.P) and optional array indices.
portName
    : IDENT (DOT IDENT)* (LBRACK NUMBER RBRACK)?
    | IDENT (DOT IDENT)* LBRACK STAR RBRACK
    ;

// Port type can be an identifier or certain keywords used as type names.
portType
    : IDENT
    | BIAS_KW
    | SUPPLY_KW
    | GROUND_KW
    | ANALOG_KW
    | DIGITAL_KW
    | MIXED_KW
    | CLOCK_KW
    | RF_KW
    ;

paramList
    : paramDecl (COMMA paramDecl)*
    ;

paramDecl
    : SIZE_KW sizeName=IDENT (EQ sizeExpr)?
    | paramType paramName=IDENT (EQ paramValue)?
    ;

paramType
    : REAL_KW
    | INT_KW
    | BOOL_KW
    ;

paramValue
    : scalarExpr
    ;

// ----------------------------------------------------------------------------
// Slot block content (HL)
// ----------------------------------------------------------------------------

slotBlockStatement
    : NET_KW IDENT COLON portType                                   # SlotNetDecl
    | slotInstanceDecl                                              # SlotInstanceStatement
    | pinRef WIRE_OP pinRef                                         # SlotConnectDecl
    ;

slotInstanceDecl
    : declaredType=slotDeclaredType instanceId=IDENT EQ NEW_KW instanceType=instanceTypeName (LPAREN argList? RPAREN)? bindingBlock?
    ;

slotDeclaredType
    : IDENT
    ;

// ----------------------------------------------------------------------------
// Fill block content
// ----------------------------------------------------------------------------

fillStatement
    : NET_KW IDENT COLON portType                                   # FillNetDecl
    | SIZE_KW sizeName=IDENT EQ sizeExpr                            # FillSizeDecl
    | fillInstanceDecl                                              # FillInstanceStatement
    | someInstanceDecl                                              # FillSomeInstanceStatement
    | deviceDecl                                                    # FillDeviceDecl
    | ATTACH_KW IDENT attachTargetList VIA_KW IDENT COLONCOLON IDENT (AS_KW IDENT)? attachOverrides? # FillAttachDecl
    | pinRef WIRE_OP pinRef                                         # FillConnectDecl
    | repeatStatement                                               # FillRepeat
    | matchStatement                                                # FillMatch
    | pairStatement                                                 # FillPair
    ;

repeatStatement
    : REPEAT_KW IDENT IN_KW LBRACK scalarExpr COLON scalarExpr RBRACK LBRACE fillStatement* RBRACE
    ;

matchStatement
    : MATCH_KW IDENT LBRACE caseStatement+ RBRACE
    ;

caseStatement
    : CASE_KW IDENT COLON LBRACE fillStatement* RBRACE
    ;

pairStatement
    : PAIR_KW IDENT LBRACE fillStatement* RBRACE
    ;

wrapSpiceDef
    : WRAP_KW SPICE_KW TRIPLE_STRING MAP_KW LBRACE wrapMapEntry* RBRACE
    ;

wrapMapEntry
    : IDENT EQ IDENT
    ;

fillBlock
    : FILL_KW LBRACE fillStatement* RBRACE
    ;

fillInstanceDecl
    : instanceDecl
    ;

someInstanceDecl
    : SOME_KW requiredType=IDENT instanceId=IDENT bindingBlock?
    ;

instanceDecl
    : declaredType=IDENT instanceId=IDENT EQ NEW_KW instanceType=instanceTypeName (LPAREN argList? RPAREN)? bindingBlock?
    ;

instanceTypeName
    : IDENT
    | physicalType
    ;

argList
    : arg (COMMA arg)*
    ;

arg
    : argName EQ argValue
    | argValue
    ;

argName
    : IDENT
    | Z_KW
    ;

argValue
    : sizeExpr
    | expr
    | scalarExpr
    ;

deviceDecl
    : DEVICE_TYPE deviceId EQ NEW_KW primitiveName=IDENT LPAREN sizeArg RPAREN bindingBlock
    ;

sizeArg
    : IDENT
    | sizeExpr
    ;

bindingBlock
    : LBRACE bindingList? RBRACE
    ;

bindingList
    : binding (COMMA? binding)*
    ;

binding
    : (DOT | BIND_DOT) pinRef WIRE_OP pinRef
    ;

// Device ID can contain keywords as parts (e.g., load.M where "load" is a keyword).
deviceId
    : idPart (DOT idPart)*
    ;

// Rule for identifiers that may also be keywords.
idPart
    : IDENT
    | INPUT_KW
    | OUTPUT_KW
    | IO_KW
    | LOAD_KW
    | BIAS_KW
    | SUPPLY_KW
    | GROUND_KW
    | SOURCE_KW
    | SWEEP_KW
    | LEVEL_KW
    | SIZE_KW
    | NET_KW
    | PORT_KW
    | PARAM_KW
    | ATTACH_KW
    | ON_KW
    | TO_KW
    | FOR_KW
    | VIA_KW
    | AS_KW
    | EXTEND_KW
    | EXTENDS_KW
    | ABSTRACT_KW
    | OVERRIDE_KW
    | BENCH_KW
    | BUILTIN_KW
    | OUTPUTS_KW
    | CONFIG_KW
    | IMPLEMENTS_KW
    | REAL_KW
    | INT_KW
    | BOOL_KW
    | AUTO_KW
    | Z_KW
    | ICMR_KW
    | PVT_KW
    | DEVICE_KW
    | PRIMITIVE_KW
    | NEW_KW
    | INTERFACE_KW
    | CONNECTORS_KW
    | NUMERIC_KW
    | TECH_KW
    | GRAPH_KW
    | ENV_KW
    | INCLUDE_KW
    | SYNTH_KW
    | BENCHES_KW
    | BIND_KW
    | FUNCTION_KW
    | ANALYSIS_KW
    | MEASUREMENTS_KW
    | MEASUREMENT_KW
    | DUT_KW
    | STIM_KW
    | RESP_KW
    | ANALOG_KW
    | DIGITAL_KW
    | MIXED_KW
    | CLOCK_KW
    | RF_KW
    | IF_KW
    | ELSE_KW
    | RETURN_KW
    | WRAP_KW
    | SPICE_KW
    | MAP_KW
    | MATCH_KW
    | CASE_KW
    | REPEAT_KW
    | IN_KW
    | PAIR_KW
    | RENDER_KW
    | PLACE_KW
    | ORIENT_KW
    | MIRROR_KW
    | SIDE_KW
    | ROUTE_KW
    | WP_KW
    | ZINDEX_KW
    | HARD_KW
    | SOFT_KW
    | HINT_KW
    | ABS_KW
    | REF_KW
    | REL_KW
    | CANVAS_KW
    | ORIGIN_KW
    | CENTER_KW
    | AC_ANALYSIS_TYPE
    | DC_ANALYSIS_TYPE
    | TRAN_ANALYSIS_TYPE
    | NOISE_ANALYSIS_TYPE
    | STB_ANALYSIS_TYPE
    | SP_ANALYSIS_TYPE
    | FREQUENCY_TYPE
    | VOLTAGE_RATIO_TYPE
    | TRANSFER_FUNCTION_TYPE
    | GAIN_SPECTRUM_TYPE
    | PHASE_SPECTRUM_TYPE
    | COMPLEX_VOLTAGE_SPECTRUM_TYPE
    | COMPLEX_CURRENT_SPECTRUM_TYPE
    | VOLTAGE_SPECTRUM_TYPE
    | CURRENT_SPECTRUM_TYPE
    | NOISE_SPECTRUM_TYPE
    | IMPEDANCE_SPECTRUM_TYPE
    | VOLTAGE_WAVEFORM_TYPE
    | CURRENT_WAVEFORM_TYPE
    | NOISE_SPECTRAL_DENSITY_TYPE
    | INTEGRATED_NOISE_TYPE
    | IMPEDANCE_TYPE
    | CAPACITANCE_TYPE
    | INDUCTANCE_TYPE
    | VOLTAGE_TYPE
    | CURRENT_TYPE
    | TIME_TYPE
    | PHASE_TYPE
    | SCALAR_TYPE
    | S_PARAMETER_MATRIX_TYPE
    ;

// Pin references can contain keywords as parts (e.g., load.D).
pinRef
    : idPart ((DOT idPart) | (LBRACK NUMBER RBRACK))*
    ;

// ----------------------------------------------------------------------------
// Render block content
// ----------------------------------------------------------------------------

renderEntity
    : renderEntityRef renderOneLiner
    | renderEntityRef LBRACE renderField* RBRACE
    ;

renderEntityRef
    : idPart (DOT idPart)*
    ;

renderOneLiner
    : PLACE_KW pointExpr strengthLevel?
    ;

renderField
    : PLACE_KW pointExpr strengthLevel?
    | ORIENT_KW signedInt MIRROR_KW?
    | SIDE_KW IDENT
    | ROUTE_KW IDENT strengthLevel?
    | WP_KW LBRACK pointExpr (COMMA pointExpr)* RBRACK
    | ZINDEX_KW signedInt
    ;

strengthLevel
    : HARD_KW
    | SOFT_KW
    | HINT_KW
    ;

pointExpr
    : absPoint
    | refPoint
    | relPoint
    ;

absPoint
    : ABS_KW signedInt signedInt
    ;

refPoint
    : REF_KW renderAnchorRef (signedInt signedInt)?
    ;

relPoint
    : REL_KW signedInt signedInt
    ;

renderAnchorRef
    : CANVAS_KW (ORIGIN_KW | CENTER_KW)
    | pinRef
    ;

signedInt
    : MINUS? NUMBER
    ;

// ----------------------------------------------------------------------------
// Constraints block content
// ----------------------------------------------------------------------------

signedQuantity
    : MINUS? QUANTITY
    ;

signedThreshold
    : signedQuantity
    | MINUS? NUMBER
    ;

constraintSection
    : NUMERIC_KW LBRACE numericConstraint* RBRACE                   # NumericSection
    | TECH_KW LBRACE techConstraint* RBRACE                         # TechSection
    | GRAPH_KW LBRACE graphConstraint* RBRACE                       # GraphSection
    | numericConstraint                                             # NumericConstraintDirect
    ;

// id = Bench(args)::Metric(args) at Node >= ValueUnit
numericConstraint
    : IDENT EQ benchMetricRef (AT_KW nodeRef)? COMPARISON_OP signedThreshold
    ;

benchMetricRef
    : IDENT (LPAREN measurementArgList? RPAREN)? COLONCOLON idPart (LPAREN measurementArgList? RPAREN)?
    ;

nodeRef
    : nodeScope COLONCOLON pinRef
    ;

nodeScope
    : IDENT
    | NET_KW
    | PORT_KW
    ;

// id : Param >= ValueUnit on Scope
techConstraint
    : IDENT COLON IDENT COMPARISON_OP signedThreshold ON_KW techConstraintScope
    ;

techConstraintScope
    : IDENT
    | STAR
    ;

// id : rule { props }
graphConstraint
    : IDENT COLON IDENT (LBRACE graphProps RBRACE)?
    ;

graphProps
    : graphProp (COMMA graphProp)*
    ;

graphProp
    : IDENT EQ (IDENT | NUMBER | QUANTITY | STRING)
    ;

// ----------------------------------------------------------------------------
// Harness block content
// ----------------------------------------------------------------------------

harnessStatement
    : SUPPLY_KW IDENT EQ harnessValue                               # HarnessSupply
    | GROUND_KW IDENT EQ harnessValue                               # HarnessGround
    | BIAS_KW IDENT EQ harnessValue                                 # HarnessBias
    | LOAD_KW IDENT loadSpec                                        # HarnessLoad
    | SOURCE_KW IDENT sourceSpec                                    # HarnessSource
    | SWEEP_KW IDENT sweepSpec                                      # HarnessSweep
    | ICMR_KW LBRACK signedQuantity COLON signedQuantity RBRACK     # HarnessIcmr
    | PVT_KW pvtList                                                # HarnessPvt
    ;

harnessValue
    : signedQuantity
    ;

loadSpec
    : loadElement (COMMA loadElement)*                              # SimpleLoadSpec
    | LPAREN loadElement ((COMMA | PIPEPIPE) loadElement)* RPAREN   # ParenLoadSpec
    ;

loadElement
    : IDENT EQ signedQuantity
    ;

sourceSpec
    : Z_KW EQ signedQuantity
    ;

sweepSpec
    : LBRACK sweepRange RBRACK
    | LBRACK AUTO_KW RBRACK
    ;

sweepRange
    : sweepValue COLON sweepValue COLON sweepValue                  # ExplicitSweep
    | sweepValue COLON sweepValue                                   # AutoStepSweep
    ;

sweepValue
    : signedQuantity
    ;

pvtList
    : IDENT (COMMA IDENT)*
    ;

// ----------------------------------------------------------------------------
// Provenance block content
// ----------------------------------------------------------------------------

provenanceEntry
    : SOURCE_KW STRING (LBRACK NUMBER COLON NUMBER RBRACK)?         # ProvenanceSource
    | TRANSFORM_KW STRING                                           # ProvenanceTransform
    | ALIAS_KW IDENT EQ IDENT                                       # ProvenanceAlias
    ;

// ----------------------------------------------------------------------------
// Sizes and expressions
// ----------------------------------------------------------------------------

sizeExpr
    : SIZE_KW LPAREN sizeExprBody RPAREN
    ;

sizeExprBody
    : sizeKvList
    | sizeExprList
    ;

sizeKvList
    : sizeKvPair (COMMA sizeKvPair)*
    ;

sizeKvPair
    : sizeKey=IDENT EQ expr
    ;

sizeExprList
    : expr (COMMA expr)*
    ;

expr
    : expr (PLUS | MINUS) mulExpr
    | mulExpr
    ;

mulExpr
    : mulExpr (STAR | SLASH) unaryAtom
    | unaryAtom
    ;

unaryAtom
    : MINUS unaryAtom
    | exprPostfix
    ;

exprPostfix
    : exprPrimary methodCallSuffix*
    ;

exprPrimary
    : LPAREN expr RPAREN
    | sizeFieldAccess
    | scopedAccess
    | measurementFunctionCall
    | IDENT
    | NUMBER
    | QUANTITY
    | AUTO_KW
    | UNSIZED
    ;

scalarExpr
    : NUMBER
    | QUANTITY
    | IDENT
    | AUTO_KW
    | STRING
    | UNSIZED
    ;

qualifiedName
    : idPart (DOT idPart)*
    ;

// ----------------------------------------------------------------------------
// Env block content
// ----------------------------------------------------------------------------

envStatement
    : IDENT EQ envValue
    ;

envValue
    : impedanceExpr
    | LPAREN impedanceExpr RPAREN
    | QUANTITY
    ;

impedanceExpr
    : impedanceElement (PIPEPIPE impedanceElement)+
    ;

impedanceElement
    : QUANTITY
    ;

// ----------------------------------------------------------------------------
// Benches sections
// ----------------------------------------------------------------------------

interfaceBenchesSection
    : BENCHES_KW LBRACE benchBinding* RBRACE
    ;

circuitBenchesSection
    : BENCHES_KW LBRACE (benchBinding | benchExtension)* RBRACE
    ;

benchBinding
    : BIND_KW benchName=IDENT AS_KW bindingName=IDENT LBRACE bindingStatement* RBRACE
    ;

benchExtension
    : EXTEND_KW bindingName=IDENT LBRACE bindingStatement* RBRACE
    ;

bindingStatement
    : terminalMapping
    | bindingMeasurementsBlock
    | instanceDecl
    | dutConnection
    ;

bindingMeasurementsBlock
    : MEASUREMENTS_KW LBRACE bindingMeasurementDecl* RBRACE
    ;

bindingMeasurementDecl
    : MEASUREMENT_KW name=IDENT (LPAREN typedParamList? RPAREN)? COLON unitType EQ benchMeasurementRef
    ;

terminalMapping
    : BENCH_KW DOT IDENT WIRE_OP DUT_KW DOT pinRef
    ;

dutConnection
    : DUT_KW DOT pinRef WIRE_OP pinRef
    ;

// ----------------------------------------------------------------------------
// Synth blocks (extracted during linking)
// ----------------------------------------------------------------------------

synthEntry
    : IDENT EQ (IDENT | NUMBER | QUANTITY | STRING)
    ;

// ----------------------------------------------------------------------------
// Bench helper functions, analysis, and measurement expressions
// ----------------------------------------------------------------------------

functionDef
    : FUNCTION_KW name=IDENT LPAREN typedParamList? RPAREN COLON returnType LBRACE functionBody RBRACE
    ;

typedParamList
    : typedParam (COMMA typedParam)*
    ;

typedParam
    : typedParamType idPart
    ;

typedParamType
    : physicalType
    | analysisType
    | terminalRole
    ;

returnType
    : physicalType
    | BOOL_KW
    ;

physicalType
    : FREQUENCY_TYPE
    | VOLTAGE_RATIO_TYPE
    | TRANSFER_FUNCTION_TYPE
    | GAIN_SPECTRUM_TYPE
    | SCALAR_SPECTRUM_TYPE
    | PHASE_SPECTRUM_TYPE
    | TIME_SPECTRUM_TYPE
    | COMPLEX_VOLTAGE_SPECTRUM_TYPE
    | COMPLEX_CURRENT_SPECTRUM_TYPE
    | VOLTAGE_SPECTRUM_TYPE
    | CURRENT_SPECTRUM_TYPE
    | NOISE_SPECTRUM_TYPE
    | IMPEDANCE_SPECTRUM_TYPE
    | VOLTAGE_WAVEFORM_TYPE
    | CURRENT_WAVEFORM_TYPE
    | NOISE_SPECTRAL_DENSITY_TYPE
    | INTEGRATED_NOISE_TYPE
    | ELEMENT_PIN_TYPE
    | IMPEDANCE_TYPE
    | CAPACITANCE_TYPE
    | INDUCTANCE_TYPE
    | VOLTAGE_TYPE
    | CURRENT_TYPE
    | TIME_TYPE
    | PHASE_TYPE
    | SCALAR_TYPE
    | S_PARAMETER_MATRIX_TYPE
    ;

analysisType
    : AC_ANALYSIS_TYPE
    | DC_ANALYSIS_TYPE
    | TRAN_ANALYSIS_TYPE
    | NOISE_ANALYSIS_TYPE
    | STB_ANALYSIS_TYPE
    | SP_ANALYSIS_TYPE
    ;

functionBody
    : statement*
    ;

statement
    : variableDecl
    | ifStatement
    | returnStatement
    ;

variableDecl
    : typedParamType IDENT EQ measurementExpr
    ;

ifStatement
    : IF_KW boolExpr LBRACE statement* RBRACE (ELSE_KW LBRACE statement* RBRACE)?
    ;

returnStatement
    : RETURN_KW measurementExpr
    ;

analysisBlock
    : OVERRIDE_KW? ANALYSIS_KW LBRACE analysisDecl* RBRACE
    ;

analysisDecl
    : analysisType name=IDENT EQ NEW_KW analysisType LPAREN analysisParams? RPAREN
    ;

analysisParams
    : analysisParam (COMMA analysisParam)*
    ;

analysisParam
    : idPart EQ conditionalExpr
    ;

conditionalExpr
    : ifExpr
    | measurementExpr
    ;

ifExpr
    : LPAREN IF_KW boolExpr LBRACE measurementExpr RBRACE ELSE_KW LBRACE measurementExpr RBRACE RPAREN
    ;

measurementsBlock
    : MEASUREMENTS_KW LBRACE measurementDecl* RBRACE
    ;

measurementDecl
    : OVERRIDE_KW? MEASUREMENT_KW name=idPart (LPAREN typedParamList? RPAREN)? COLON unitType LBRACE measurementBody RBRACE
    ;

unitType
    : IDENT
    | NOISE_DENSITY_UNIT
    | INTEGRATED_RMS_UNIT
    | SCALAR_TYPE
    | TIME_TYPE
    ;

measurementBody
    : statement*
    ;

boolExpr
    : scopedAccess
    | pathAccess
    | measurementExpr COMPARISON_OP measurementExpr
    ;

measurementExpr
    : measurementExpr (PLUS | MINUS) mulMeasurementExpr
    | mulMeasurementExpr
    ;

mulMeasurementExpr
    : mulMeasurementExpr (STAR | SLASH) unaryMeasurementExpr
    | unaryMeasurementExpr
    ;

unaryMeasurementExpr
    : MINUS unaryMeasurementExpr
    | measurementPostfix
    ;

measurementPostfix
    : measurementPrimary methodCallSuffix*
    ;

methodCallSuffix
    : DOT idPart LPAREN measurementArgList? RPAREN
    ;

measurementPrimary
    : ifExpr
    | LPAREN measurementExpr RPAREN
    | benchMeasurementRef
    | measurementFunctionCall
    | scopedAccess
    | dutAccess
    | pathAccess
    | QUANTITY
    | NUMBER
    ;

// Cross-bench measurement reference used in constraint arguments (and allowed anywhere a measurementExpr is allowed).
// Syntax: binding_alias::Measurement(args)
benchMeasurementRef
    : IDENT COLONCOLON idPart (LPAREN measurementArgList? RPAREN)?
    ;

measurementFunctionCall
    : idPart LPAREN measurementArgList? RPAREN
    ;

measurementArgList
    : measurementArg (COMMA measurementArg)*
    ;

measurementArg
    : idPart EQ measurementExpr
    | measurementExpr
    ;

pathAccess
    : idPart (DOT idPart)*
    ;

scopedAccess
    : ENV_KW DOT IDENT
    | CONSTRAINTS_KW DOT IDENT
    | HARNESS_KW DOT pinRef
    ;

dutAccess
    : DUT_KW DOT pinRef
    ;

attachTargetList
    : (TO_KW IDENT)+
    ;

// Attach overrides can have bindings separated by whitespace or commas.
attachOverrides
    : LBRACE binding* RBRACE
    ;

// ============================================================================
// Lexer Rules
// ============================================================================

// Keywords (order matters - longer/more specific first)

VERSION_KW      : 'VERSION' ;
BUNDLE_KW       : 'bundle' ;
INTERFACE_KW    : 'interface' ;
BENCH_KW        : 'bench' ;
BENCHES_KW      : 'benches' ;
BIND_KW         : 'bind' ;
EXTEND_KW       : 'extend' ;
EXTENDS_KW      : 'extends' ;
ABSTRACT_KW     : 'abstract' ;
OVERRIDE_KW     : 'override' ;
CIRCUIT_KW      : 'circuit' ;
PRIMITIVE_KW    : 'primitive' ;
DEVICE_KW       : 'device' ;
PARAMS_KW       : 'params' ;
NEW_KW          : 'new' ;
INCLUDE_KW      : 'include' ;
SYNTH_KW        : 'synth' ;
WRAP_KW         : 'wrap' ;
SPICE_KW        : 'spice' ;
MAP_KW          : 'map' ;
MATCH_KW        : 'match' ;
CASE_KW         : 'case' ;
REPEAT_KW       : 'repeat' ;
IN_KW           : 'in' ;
PAIR_KW         : 'pair' ;
RENDER_KW       : 'render' ;
PLACE_KW        : 'place' ;
ORIENT_KW       : 'orient' ;
MIRROR_KW       : 'mirror' ;
SIDE_KW         : 'side' ;
ROUTE_KW        : 'route' ;
WP_KW           : 'wp' ;
ZINDEX_KW       : 'zindex' ;
HARD_KW         : 'hard' ;
SOFT_KW         : 'soft' ;
HINT_KW         : 'hint' ;
ABS_KW          : 'abs' ;
REF_KW          : 'ref' ;
REL_KW          : 'rel' ;
CANVAS_KW       : 'canvas' ;
ORIGIN_KW       : 'origin' ;
CENTER_KW       : 'center' ;

PORT_KW         : 'port' ;
INPUT_KW        : 'input' ;
OUTPUT_KW       : 'output' ;
IO_KW           : 'io' ;
CONNECTORS_KW   : 'connectors' ;
LEVEL_KW        : 'level' ;
INLINE_KW       : 'inline' ;
PACKAGE_KW      : 'library' ;
SUPPLY_KW       : 'supply' ;
GROUND_KW       : 'ground' ;
PARAM_KW        : 'param' ;
SLOT_KW         : 'slot' ;
SIZE_KW         : 'size' ;
FILL_KW         : 'fill' ;
CONSTRAINTS_KW  : 'constraints' ;
HARNESS_KW      : 'harness' ;
ENV_KW          : 'env' ;
PROVENANCE_KW   : 'provenance' ;
NET_KW          : 'net' ;
ATTACH_KW       : 'attach' ;
TO_KW           : 'to' ;
FOR_KW          : 'for' ;
VIA_KW          : 'via' ;
AS_KW           : 'as' ;
BUILTIN_KW      : 'builtin' ;
OUTPUTS_KW      : 'outputs' ;
CONFIG_KW       : 'config' ;
IMPLEMENTS_KW   : 'implements' ;
NUMERIC_KW      : 'numeric' ;
TECH_KW         : 'tech' ;
GRAPH_KW        : 'graph' ;
BIAS_KW         : 'bias' ;
LOAD_KW         : 'load' ;
SOURCE_KW       : 'source' ;
SWEEP_KW        : 'sweep' ;
ICMR_KW         : 'icmr' ;
PVT_KW          : 'pvt' ;
AUTO_KW         : 'Auto' ;
SOME_KW         : 'Some' ;
AT_KW           : 'at' ;
Z_KW            : 'Z' ;
ON_KW           : 'on' ;
REAL_KW         : 'real' ;
INT_KW          : 'int' ;
BOOL_KW         : 'bool' ;
TRANSFORM_KW    : 'transform' ;
ALIAS_KW        : 'alias' ;
HL_KW           : 'HL' ;
ML_KW           : 'ML' ;
EL_KW           : 'EL' ;

STIM_KW         : 'stim' ;
RESP_KW         : 'resp' ;
ANALOG_KW       : 'analog' ;
DIGITAL_KW      : 'digital' ;
MIXED_KW        : 'mixed' ;
CLOCK_KW        : 'clock' ;
RF_KW           : 'rf' ;
FUNCTION_KW     : 'function' ;
ANALYSIS_KW     : 'analysis' ;
MEASUREMENTS_KW : 'measurements' ;
MEASUREMENT_KW  : 'measurement' ;
DUT_KW          : 'dut' ;
IF_KW           : 'if' ;
ELSE_KW         : 'else' ;
RETURN_KW       : 'return' ;

FREQUENCY_TYPE              : 'Frequency' ;
VOLTAGE_RATIO_TYPE          : 'VoltageRatio' ;
TRANSFER_FUNCTION_TYPE      : 'TransferFunction' ;
GAIN_SPECTRUM_TYPE          : 'GainSpectrum' ;
SCALAR_SPECTRUM_TYPE        : 'ScalarSpectrum' ;
PHASE_SPECTRUM_TYPE         : 'PhaseSpectrum' ;
TIME_SPECTRUM_TYPE          : 'TimeSpectrum' ;
COMPLEX_VOLTAGE_SPECTRUM_TYPE : 'ComplexVoltageSpectrum' ;
COMPLEX_CURRENT_SPECTRUM_TYPE : 'ComplexCurrentSpectrum' ;
VOLTAGE_SPECTRUM_TYPE       : 'VoltageSpectrum' ;
CURRENT_SPECTRUM_TYPE       : 'CurrentSpectrum' ;
NOISE_SPECTRUM_TYPE         : 'NoiseSpectrum' ;
IMPEDANCE_SPECTRUM_TYPE     : 'ImpedanceSpectrum' ;
VOLTAGE_WAVEFORM_TYPE       : 'VoltageWaveform' ;
CURRENT_WAVEFORM_TYPE       : 'CurrentWaveform' ;
NOISE_SPECTRAL_DENSITY_TYPE : 'NoiseSpectralDensity' ;
INTEGRATED_NOISE_TYPE       : 'IntegratedNoise' ;
ELEMENT_PIN_TYPE            : 'ElementPin' ;
IMPEDANCE_TYPE              : 'Impedance' ;
CAPACITANCE_TYPE            : 'Capacitance' ;
INDUCTANCE_TYPE             : 'Inductance' ;
VOLTAGE_TYPE                : 'Voltage' ;
CURRENT_TYPE                : 'Current' ;
TIME_TYPE                   : 'Time' ;
PHASE_TYPE                  : 'Phase' ;
SCALAR_TYPE                 : 'Scalar' ;
S_PARAMETER_MATRIX_TYPE     : 'SParameterMatrix' ;

AC_ANALYSIS_TYPE    : 'ACAnalysis' ;
DC_ANALYSIS_TYPE    : 'DCAnalysis' ;
TRAN_ANALYSIS_TYPE  : 'TranAnalysis' ;
NOISE_ANALYSIS_TYPE : 'NoiseAnalysis' ;
STB_ANALYSIS_TYPE   : 'STBAnalysis' ;
SP_ANALYSIS_TYPE    : 'SPAnalysis' ;

DEVICE_TYPE
    : 'NMOS'
    | 'PMOS'
    | 'Resistor'
    | 'Capacitor'
    | 'Inductor'
    | 'Diode'
    ;

COMPARISON_OP
    : '>=' | '<=' | '==' | '>' | '<'
    ;

WIRE_OP         : '--' ;
COLONCOLON      : '::' ;
PIPEPIPE        : '||' ;
COLON           : ':' ;
COMMA           : ',' ;
BIND_DOT        : '.' { _atLineStart }? ;
DOT             : '.' ;
EQ              : '=' ;
LPAREN          : '(' ;
RPAREN          : ')' ;
LBRACK          : '[' ;
RBRACK          : ']' ;
LBRACE          : '{' ;
RBRACE          : '}' ;
STAR            : '*' ;
SLASH           : '/' ;
PLUS            : '+' ;
MINUS           : '-' ;
AT              : '@' ;

NOISE_DENSITY_UNIT  : ('V' | 'nV' | 'uV' | 'pV' | 'A' | 'nA' | 'pA' | 'uA') '/rtHz' ;
INTEGRATED_RMS_UNIT : ('V' | 'nV' | 'uV' | 'mV' | 'A' | 'nA' | 'pA' | 'uA') 'rms' ;

QUANTITY        : [0-9]* '.'? [0-9]+ ([eE] [+\-]? [0-9]+)? [fpnumkMGT]? [A-Za-z]+ ('/rtHz')? ;
NUMBER          : [0-9]* '.'? [0-9]+ ([eE] [+\-]? [0-9]+)? ;
IDENT           : [A-Za-z_][A-Za-z0-9_]* ;
TRIPLE_STRING   : '"""' ( . | '\r' | '\n' )*? '"""' ;
STRING          : '"' (~["\\] | '\\' .)* '"' ;
UNSIZED         : '??' ;

LINE_COMMENT    : '//' ~[\r\n]* -> skip ;
WS              : [ \t\r]+ -> skip ;
NEWLINE         : ('\r'? '\n')+ { _atLineStart = true; } -> skip ;
