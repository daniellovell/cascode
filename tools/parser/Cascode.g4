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
    : Identifier ':' Identifier
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
    : Identifier '=' 'new' Identifier instanceParams? instanceBinds? ';'
    ;

instanceParams
    : '{' (~'}')* '}'
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
    : Identifier ('.' Identifier)*
    ;

literal
    : IntegerLiteral
    | RealLiteral
    | StringLiteral
    | 'true'
    | 'false'
    ;
    
portsComputedBlock
    : 'ports' '{' portsComputedToken* '}'
    ;

portsComputedToken
    : Identifier
    | '('
    | ')'
    | '{'
    | '}'
    | ':'
    | ';'
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
