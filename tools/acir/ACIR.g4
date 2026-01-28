grammar ACIR;

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
    : bundleDef
    | interfaceDef
    | benchDef
    | primitiveDef
    | circuit
    ;

// Version is a decimal number like 3.0
versionDecl
    : ACIR_KW NUMBER
    ;

// ----------------------------------------------------------------------------
// Bundle definitions
// ----------------------------------------------------------------------------

bundleDef
    : BUNDLE_KW name=IDENT LBRACE bundleField* RBRACE
    ;

bundleField
    : IDENT COLON IDENT
    ;

// ----------------------------------------------------------------------------
// Interface definitions
// ----------------------------------------------------------------------------

interfaceDef
    : INTERFACE_KW name=IDENT LBRACE interfaceMember* RBRACE
    ;

interfaceMember
    : direction portName COLON portType                             # InterfacePort
    | CONNECTORS_KW LBRACE connectorDef* RBRACE                      # InterfaceConnectors
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
    : BENCH_KW name=IDENT FOR_KW trait=IDENT LBRACE benchMember* RBRACE
    ;

benchMember
    : BUILTIN_KW IDENT
    | CONFIG_KW LBRACE benchConfigEntry* RBRACE
    | OUTPUTS_KW LBRACE benchOutput* RBRACE
    ;

benchConfigEntry
    : IDENT EQ (IDENT | NUMBER | QUANTITY | STRING)
    ;

benchOutput
    : IDENT
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
    : IMPLEMENTS_KW traitList
    ;

traitList
    : IDENT (COMMA IDENT)*
    ;

circuitMember
    : LEVEL_KW levelValue                                           # LevelDecl
    | INLINE_KW                                                     # InlineDecl
    | PACKAGE_KW qualifiedName                                      # PackageDecl
    | SUPPLY_KW IDENT                                               # SupplyDecl
    | GROUND_KW IDENT                                               # GroundDecl
    | direction portName COLON portType                             # PortDecl
    | slotDecl                                                      # SlotMember
    | FILL_KW LBRACE fillStatement* RBRACE                          # FillSection
    | CONSTRAINTS_KW LBRACE constraintSection* RBRACE               # ConstraintsSection
    | HARNESS_KW LBRACE harnessStatement* RBRACE                    # HarnessSection
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
// Slot declarations (HL)
// ----------------------------------------------------------------------------

slotDecl
    : SLOT_KW IDENT implementsClause? LBRACE slotStatement* RBRACE
    ;

slotStatement
    : PARAM_KW IDENT EQ scalarExpr                                  # SlotParam
    | binding                                                      # SlotBinding
    ;

// ----------------------------------------------------------------------------
// Fill block content
// ----------------------------------------------------------------------------

fillStatement
    : NET_KW IDENT COLON portType                                   # FillNetDecl
    | SIZE_KW sizeName=IDENT EQ sizeExpr                            # FillSizeDecl
    | instanceDecl                                                  # FillInstanceDecl
    | deviceDecl                                                    # FillDeviceDecl
    | ATTACH_KW IDENT attachTargetList VIA_KW IDENT COLONCOLON IDENT (AS_KW IDENT)? attachOverrides? # FillAttachDecl
    | pinRef WIRE_OP pinRef                                         # FillConnectDecl
    ;

instanceDecl
    : instanceId=IDENT EQ NEW_KW instanceType=IDENT (LPAREN argList? RPAREN)? bindingBlock
    ;

argList
    : arg (COMMA arg)*
    ;

arg
    : IDENT EQ argValue
    ;

argValue
    : sizeExpr
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
    ;

// Pin references can contain keywords as parts (e.g., load.D).
pinRef
    : idPart ((DOT idPart) | (LBRACK NUMBER RBRACK))*
    ;

// ----------------------------------------------------------------------------
// Constraints block content
// ----------------------------------------------------------------------------

constraintSection
    : NUMERIC_KW LBRACE numericConstraint* RBRACE                   # NumericSection
    | TECH_KW LBRACE techConstraint* RBRACE                         # TechSection
    | GRAPH_KW LBRACE graphConstraint* RBRACE                       # GraphSection
    ;

// id = Bench::Metric at Node >= ValueUnit
numericConstraint
    : IDENT EQ benchMetricRef (AT_KW nodeRef)? COMPARISON_OP QUANTITY
    ;

benchMetricRef
    : IDENT COLONCOLON IDENT
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
    : IDENT COLON IDENT COMPARISON_OP QUANTITY ON_KW techConstraintScope
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
    | BIAS_KW IDENT EQ harnessValue                                 # HarnessBias
    | LOAD_KW IDENT loadSpec                                        # HarnessLoad
    | SOURCE_KW IDENT sourceSpec                                    # HarnessSource
    | SWEEP_KW IDENT sweepSpec                                      # HarnessSweep
    | ICMR_KW LBRACK QUANTITY COLON QUANTITY RBRACK                 # HarnessIcmr
    | PVT_KW pvtList                                                # HarnessPvt
    ;

// Harness value allows legacy format with space between number and unit (e.g., 1.8 V).
harnessValue
    : QUANTITY
    | NUMBER IDENT?                                                 // Allow "1.8 V" with space.
    ;

loadSpec
    : loadElement (COMMA loadElement)*                              # SimpleLoadSpec
    | LPAREN loadElement ((COMMA | PIPEPIPE) loadElement)* RPAREN   # ParenLoadSpec
    ;

// Load element allows legacy format with split value and unit (e.g., C=1p F).
loadElement
    : IDENT EQ (QUANTITY | NUMBER) IDENT?
    ;

// Source spec allows legacy format without unit (e.g., Z=50).
sourceSpec
    : Z_KW EQ (QUANTITY | NUMBER)
    ;

sweepSpec
    : LBRACK sweepRange RBRACK
    | LBRACK AUTO_KW RBRACK
    ;

sweepRange
    : sweepValue COLON sweepValue COLON sweepValue                  # ExplicitSweep
    | sweepValue COLON sweepValue                                   # AutoStepSweep
    ;

// Sweep value allows legacy format with space between number and unit (e.g., 0.3 V).
sweepValue
    : QUANTITY
    | NUMBER IDENT?
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
    | exprAtom
    ;

exprAtom
    : LPAREN expr RPAREN
    | sizeFieldAccess
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
    : IDENT (DOT IDENT)*
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
ACIR_KW         : 'ACIR' ;

BUNDLE_KW       : 'bundle' ;
INTERFACE_KW    : 'interface' ;
TRAIT_KW        : 'trait' ;
BENCH_KW        : 'bench' ;
CIRCUIT_KW      : 'circuit' ;
PRIMITIVE_KW    : 'primitive' ;
DEVICE_KW       : 'device' ;
PARAMS_KW       : 'params' ;
NEW_KW          : 'new' ;

PORT_KW         : 'port' ;
INPUT_KW        : 'input' ;
OUTPUT_KW       : 'output' ;
IO_KW           : 'io' ;
CONNECTORS_KW   : 'connectors' ;
LEVEL_KW        : 'level' ;
INLINE_KW       : 'inline' ;
PACKAGE_KW      : 'package' ;
SUPPLY_KW       : 'supply' ;
GROUND_KW       : 'ground' ;
PARAM_KW        : 'param' ;
SLOT_KW         : 'slot' ;
SIZE_KW         : 'size' ;
FILL_KW         : 'fill' ;
CONSTRAINTS_KW  : 'constraints' ;
HARNESS_KW      : 'harness' ;
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

DEVICE_TYPE
    : 'nmos'
    | 'pmos'
    | 'resistor'
    | 'capacitor'
    | 'inductor'
    | 'diode'
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

QUANTITY        : [0-9]* '.'? [0-9]+ ([eE] [+\-]? [0-9]+)? [fpnumkMGT]? [A-Za-z]+ ;
NUMBER          : [0-9]* '.'? [0-9]+ ([eE] [+\-]? [0-9]+)? ;
IDENT           : [A-Za-z_][A-Za-z0-9_]* ;
STRING          : '"' (~["\\] | '\\' .)* '"' ;
UNSIZED         : '??' ;

LINE_COMMENT    : '//' ~[\r\n]* -> skip ;
WS              : [ \t\r]+ -> skip ;
NEWLINE         : ('\r'? '\n')+ { _atLineStart = true; } -> skip ;
