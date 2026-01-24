grammar ACIR;

// ============================================================================
// Parser Rules
// ============================================================================

// Document: optional version followed by bundles, traits, benches, and circuits
// Empty documents (no version) are allowed for compatibility
document
    : versionDecl? bundleDef* traitDef* benchDef* circuit* EOF
    ;

// Version is a decimal number like 3.0
versionDecl
    : ACIR_KW NUMBER
    ;

// ----------------------------------------------------------------------------
// Bundle definitions
// ----------------------------------------------------------------------------

bundleDef
    : BUNDLE_KW IDENT COLON bundleField+
    ;

bundleField
    : IDENT COLON IDENT
    ;

// ----------------------------------------------------------------------------
// Trait definitions
// ----------------------------------------------------------------------------

traitDef
    : TRAIT_KW IDENT COLON traitMember+
    ;

traitMember
    : PORT_KW portName COLON portType                               # TraitPort
    | CONNECTORS_KW connectorDef+                                   # TraitConnectors
    ;

connectorDef
    : TO_KW IDENT COLON connectorMapping+
    ;

connectorMapping
    : pinRef ARROW pinRef
    ;

// ----------------------------------------------------------------------------
// Bench definitions
// ----------------------------------------------------------------------------

benchDef
    : BENCH_KW IDENT FOR_KW IDENT benchMember*
    ;

benchMember
    : BUILTIN_KW IDENT
    | CONFIG_KW benchConfigEntry*
    | OUTPUTS_KW benchOutput*
    ;

benchConfigEntry
    : IDENT EQ (IDENT | NUMBER | QUANTITY | STRING)
    ;

benchOutput
    : IDENT
    ;

// ----------------------------------------------------------------------------
// Circuit definitions
// ----------------------------------------------------------------------------

circuit
    : CIRCUIT_KW IDENT (IMPLEMENTS_KW traitList)? circuitMember*
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
    | PORT_KW portName COLON portType                               # PortDecl
    | PARAM_KW IDENT COLON paramType (EQ paramValue)?               # ParamDecl
    | SIZE_KW IDENT (EQ sizeLiteral)?                               # SizeDecl
    | FILL_KW fillStatement*                                        # FillSection
    | CONSTRAINTS_KW constraintSection*                             # ConstraintsSection
    | HARNESS_KW harnessStatement*                                  # HarnessSection
    | PROVENANCE_KW provenanceEntry*                                # ProvenanceSection
    ;

levelValue
    : HL_KW | ML_KW | EL_KW
    ;

// Port names can have dots (e.g., OUT.P) and optional array indices
portName
    : IDENT (DOT IDENT)* (LBRACK NUMBER RBRACK)?
    | IDENT (DOT IDENT)* LBRACK STAR RBRACK
    ;

// Port type can be an identifier or certain keywords used as type names
portType
    : IDENT
    | BIAS_KW
    | SUPPLY_KW
    | GROUND_KW
    ;

paramType
    : REAL_KW
    | INT_KW
    ;

paramValue
    : NUMBER
    | QUANTITY
    | SYMBOLIC
    | STRING
    | IDENT
    ;

sizeLiteral
    : LPAREN sizeEntry (COMMA sizeEntry)* RPAREN
    ;

sizeEntry
    : IDENT EQ (NUMBER | QUANTITY | SYMBOLIC | UNSIZED)
    ;

qualifiedName
    : IDENT (DOT IDENT)*
    ;

// ----------------------------------------------------------------------------
// Fill block content
// ----------------------------------------------------------------------------

fillStatement
    : NET_KW IDENT COLON portType                                   # NetDecl
    | DEVICE_TYPE deviceId LPAREN bindingList RPAREN COLON deviceParams? pdkDeviceName?  # DeviceDecl
    | INST_KW IDENT (LPAREN bindingList RPAREN)? COLON IDENT instanceMember*    # InstanceDecl
    | ATTACH_KW IDENT attachTargetList VIA_KW IDENT COLONCOLON IDENT (AS_KW IDENT)? attachOverrides?  # AttachDecl
    | CONNECT_KW pinRef ARROW pinRef                                # ConnectDecl
    ;

// PDK device name can be IDENT or a device type keyword (nmos, pmos, etc.)
pdkDeviceName
    : IDENT
    | DEVICE_TYPE
    ;

// Device ID can contain keywords as parts (e.g., load.M where "load" is a keyword)
deviceId
    : idPart (DOT idPart)*
    ;

// Rule for identifiers that may also be keywords
// Some keywords (like load, bias, etc.) can appear as part of device/net names
idPart
    : IDENT
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
    | CONNECT_KW
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
    | AUTO_KW
    | Z_KW
    | ICMR_KW
    | PVT_KW
    ;

bindingList
    : binding (COMMA binding)*
    |
    ;

binding
    : pinRef ARROW pinRef
    ;

deviceParams
    : deviceParam+
    ;

deviceParam
    : IDENT EQ deviceParamValue
    | LOAD_TYPE EQ deviceParamValue                                 // Allow R/C as param names
    | SIZE_KW EQ sizeLiteral
    | SIZE_KW EQ IDENT
    ;

deviceParamValue
    : NUMBER
    | QUANTITY
    | SYMBOLIC
    ;

instanceMember
    : PARAM_KW IDENT EQ paramValue                                  # InstanceParam
    | SIZE_KW IDENT EQ sizeLiteral                                  # InstanceSize
    | CONNECT_KW pinRef ARROW pinRef                                # InstanceConnect
    | binding                                                       # InstanceBinding
    ;

attachTargetList
    : (TO_KW IDENT)+
    ;

// Attach overrides can have bindings separated by whitespace or commas
attachOverrides
    : LBRACE binding* RBRACE
    ;

// Pin references can contain keywords as parts (e.g., load.D)
pinRef
    : idPart (DOT idPart)* (LBRACK NUMBER RBRACK)?
    ;

// ----------------------------------------------------------------------------
// Constraints block content
// ----------------------------------------------------------------------------

constraintSection
    : NUMERIC_KW numericConstraint*                                 # NumericSection
    | TECH_KW techConstraint*                                       # TechSection
    | GRAPH_KW graphConstraint*                                     # GraphSection
    ;

// id : Bench::Metric at Node >= ValueUnit
numericConstraint
    : IDENT COLON benchMetricRef (AT_KW nodeRef)? COMPARISON_OP QUANTITY
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

// Harness value allows legacy format with space between number and unit (e.g., 1.8 V)
harnessValue
    : QUANTITY
    | NUMBER IDENT?                                                 // Allow "1.8 V" with space
    ;

loadSpec
    : loadElement (COMMA loadElement)*                              # SimpleLoadSpec
    | LPAREN loadElement ((COMMA | PIPEPIPE) loadElement)* RPAREN   # ParenLoadSpec
    ;

// Load element allows legacy format with split value and unit (e.g., C=1p F)
loadElement
    : LOAD_TYPE EQ (QUANTITY | NUMBER) IDENT?
    ;

// Source spec allows legacy format without unit (e.g., Z=50)
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

// Sweep value allows legacy format with space between number and unit (e.g., 0.3 V)
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
    : SOURCE_PROV_KW STRING (LBRACK NUMBER COLON NUMBER RBRACK)?    # ProvenanceSource
    | TRANSFORM_KW STRING                                           # ProvenanceTransform
    | ALIAS_KW IDENT EQ IDENT                                       # ProvenanceAlias
    ;

// ============================================================================
// Lexer Rules
// ============================================================================

// Keywords (order matters - longer/more specific first)
ACIR_KW         : 'ACIR' ;

BUNDLE_KW       : 'bundle' ;
TRAIT_KW        : 'trait' ;
BENCH_KW        : 'bench' ;
CIRCUIT_KW      : 'circuit' ;
PORT_KW         : 'port' ;
CONNECTORS_KW   : 'connectors:' ;
LEVEL_KW        : 'level' ;
INLINE_KW       : 'inline' ;
PACKAGE_KW      : 'package' ;
SUPPLY_KW       : 'supply' ;
GROUND_KW       : 'ground' ;
PARAM_KW        : 'param' ;
SIZE_KW         : 'size' ;
FILL_KW         : 'fill:' ;
CONSTRAINTS_KW  : 'constraints:' ;
HARNESS_KW      : 'harness:' ;
PROVENANCE_KW   : 'provenance:' ;
NET_KW          : 'net' ;
INST_KW         : 'inst' ;
ATTACH_KW       : 'attach' ;
CONNECT_KW      : 'connect' ;
TO_KW           : 'to' ;
FOR_KW          : 'for' ;
VIA_KW          : 'via' ;
AS_KW           : 'as' ;
BUILTIN_KW      : 'builtin' ;
OUTPUTS_KW      : 'outputs:' ;
CONFIG_KW       : 'config:' ;
IMPLEMENTS_KW   : 'implements' ;
NUMERIC_KW      : 'numeric:' ;
TECH_KW         : 'tech:' ;
GRAPH_KW        : 'graph:' ;
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
SOURCE_PROV_KW  : 'source:' ;
TRANSFORM_KW    : 'transform:' ;
ALIAS_KW        : 'alias:' ;

// Level values
HL_KW           : 'HL' ;
ML_KW           : 'ML' ;
EL_KW           : 'EL' ;

// Device types
DEVICE_TYPE     : 'nmos' | 'pmos' | 'resistor' | 'capacitor' | 'inductor' | 'diode' ;

// Load types
LOAD_TYPE       : 'C' | 'R' ;

// Operators
COMPARISON_OP   : '>=' | '<=' | '==' | '>' | '<' ;
ARROW           : '->' ;
COLONCOLON      : '::' ;
PIPEPIPE        : '||' ;

// Punctuation
COLON           : ':' ;
COMMA           : ',' ;
DOT             : '.' ;
EQ              : '=' ;
LPAREN          : '(' ;
RPAREN          : ')' ;
LBRACK          : '[' ;
RBRACK          : ']' ;
LBRACE          : '{' ;
RBRACE          : '}' ;
STAR            : '*' ;
AT              : '@' ;

// Symbolic values like $Auto, $ratio
SYMBOLIC        : '$' [A-Za-z_][A-Za-z0-9_]* ;

// Unsized placeholder (ML level circuits may have unresolved sizes)
UNSIZED         : '??' ;

// Quantity: numeric value with SI prefix and/or unit (e.g., 1.8V, 100MHz, 10u, 180n)
// Must come before NUMBER to match longer token
QUANTITY        : '-'? [0-9]+ ('.' [0-9]+)? ([eE] [+-]? [0-9]+)? [fpnumkMGT] [A-Za-z]*
                | '-'? [0-9]+ ('.' [0-9]+)? ([eE] [+-]? [0-9]+)? [A-Za-z]+
                ;

// Plain numbers (integer or decimal)
NUMBER          : [0-9]+ ('.' [0-9]+)? ;

// Identifiers
IDENT           : [A-Za-z_][A-Za-z0-9_]* ;

// String literals
STRING          : '"' (~["\\] | '\\' .)* '"' ;

// Line comments
LINE_COMMENT    : '//' ~[\r\n]* -> skip ;

// Whitespace (skip all whitespace including newlines - indentation handled by structure)
WS              : [ \t\r\n]+ -> skip ;
