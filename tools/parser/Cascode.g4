grammar Cascode;

compilationUnit
    : packageDecl? importDecl* declaration* EOF
    ;

packageDecl
    : 'package' qualifiedName ';'
    ;

importDecl
    : 'import' qualifiedName ('.' '*')? ';'
    ;

declaration
    : traitDecl
    | motifDecl
    | benchDecl
    ;

traitDecl
    : 'trait' Identifier ('extend' qualifiedName (',' qualifiedName)*)? traitBody
    ;

traitBody
    : '{' .*? '}'
    ;

motifDecl
    : 'motif' Identifier ('implements' qualifiedName (',' qualifiedName)*)? motifBody
    ;

motifBody
    : '{' motifMember* '}'
    ;

motifMember
    : portsSquare
    | portsComputedBlock
    | useBlock
    | supplyDecl
    | groundDecl
    | paramsBlock
    | ';'
    ;

paramsBlock
    : 'params' '{' paramLine* '}'
    ;

paramLine
    : (Identifier | ':' | '=' | IntegerLiteral | RealLiteral | StringLiteral | 'true' | 'false')* ';'
    ;

supplyDecl
    : 'supply' Identifier ('=' literal)? ';'?
    ;

groundDecl
    : 'ground' Identifier ('=' literal)? ';'?
    ;

portsSquare
    : 'ports' '[' portList ']' ';'?
    ;

portList
    : portDecl (',' portDecl)*
    ;

portDecl
    : Identifier ':' portKind
    ;

portKind
    : Identifier
    | 'supply'
    | 'ground'
    | 'bias'
    | 'analog'
    | 'digital'
    ;

useBlock
    : 'use' '{' useStatement* '}'
    ;

useStatement
    : instanceDecl
    | attachStmt
    | connectStmt
    ;

instanceDecl
    : Identifier '=' 'new' Identifier constructorArgs? instanceParams? instanceBinds? ';'
    ;

constructorArgs
    : '(' (paramValue (',' paramValue)*)? ')'
    ;

instanceParams
    : '{' (instanceParam (';' instanceParam)* ';'?)? '}'
    ;

instanceParam
    : Identifier '=' paramValue
    ;

paramValue
    : Identifier
    | IntegerLiteral
    | RealLiteral
    | quantityLiteral
    | 'true'
    | 'false'
    ;

instanceBinds
    : '{' binding (';' binding)* ';'? '}'
    ;

binding
    : pinRef '->' pinRef
    ;

attachStmt
    : 'attach' Identifier 'to' Identifier ';'
    ;

connectStmt
    : 'connect' pinRef '->' pinRef ';'
    ;

benchDecl
    : 'bench' Identifier '{' .*? '}'
    ;

pinRef
    : Identifier ('.' Identifier)* ('[' IntegerLiteral ']')?
    ;

qualifiedName
    : nameSegment ('.' nameSegment)*
    ;

// Allows keywords to be used as identifiers in names (e.g., package analog.ota)
nameSegment
    : Identifier
    | 'supply'
    | 'ground'
    | 'bias'
    | 'analog'
    | 'digital'
    ;

literal
    : quantityLiteral
    | IntegerLiteral
    | RealLiteral
    | StringLiteral
    | 'true'
    | 'false'
    ;

// Quantity literals are numeric values followed immediately by a unit identifier.
// The lexer produces separate tokens (e.g., RealLiteral + Identifier), so the parser
// combines them here. Units include: V, mV, A, mA, uA, F, pF, Hz, MHz, GHz, deg, dB, s, ps, ns, nm, um, mW, etc.
quantityLiteral
    : (IntegerLiteral | RealLiteral) Identifier
    ;
    
portsComputedBlock
    : 'ports' '{' portsComputedToken* '}'
    ;

// Tokens that can appear in computed ports blocks (e.g., ports { if (hasTail) { BIAS: bias; } })
// Includes keywords that may appear in port kind position or control flow
portsComputedToken
    : Identifier
    | IntegerLiteral
    | '('
    | ')'
    | '{'
    | '}'
    | '['
    | ']'
    | ':'
    | ';'
    | ','
    | 'supply'
    | 'ground'
    | 'bias'
    | 'analog'
    | 'digital'
    | 'if'
    | 'for'
    | 'in'
    ;

Identifier
    : [A-Za-z_][A-Za-z0-9_]*
    ;

IntegerLiteral
    : [0-9]+
    ;

RealLiteral
    : [0-9]+'.'[0-9]*
    ;

StringLiteral
    : '"' (~["\\] | '\\' .)* '"'
    ;

LineComment
    : '//' ~[\r\n]* -> skip
    ;

BlockComment
    : '/*' .*? '*/' -> skip
    ;

WS
    : [ \t\r\n]+ -> skip
    ;
