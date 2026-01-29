using System;
using System.Collections.Generic;
using System.IO;
using Antlr4.Runtime;

namespace Cascode.Language;

/// <summary>
/// Evaluates arithmetic expressions using the ANTLR-generated parser.
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>
    /// Evaluates an expression string to a numeric result.
    /// </summary>
    /// <param name="expression">Expression to evaluate.</param>
    /// <param name="resolveIdentifier">Resolver for identifiers.</param>
    /// <returns>Evaluated numeric value as formatted string with SI prefix.</returns>
    public static string Evaluate(string expression, Func<string, string?> resolveIdentifier)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(resolveIdentifier);

        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ArgumentException($"Empty or invalid expression: '{expression}'");
        }

        var errors = new List<string>();
        var parser = CreateParser(expression, errors, out var tokenStream);
        var tree = parser.expr();

        if (
            errors.Count > 0
            || parser.NumberOfSyntaxErrors > 0
            || tokenStream.LA(1) != TokenConstants.EOF
        )
        {
            throw new ArgumentException($"Invalid expression: '{expression}'");
        }

        var visitor = new EvaluatingVisitor(resolveIdentifier);
        var result = visitor.Visit(tree);
        return ParameterEvaluator.FormatNumeric(result);
    }

    private static CascodeParser CreateParser(
        string expression,
        List<string> errors,
        out CommonTokenStream tokenStream
    )
    {
        var inputStream = CharStreams.fromString(expression);
        var lexer = new CascodeLexer(inputStream);
        tokenStream = new CommonTokenStream(lexer);
        var parser = new CascodeParser(tokenStream);

        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();

        var errorListener = new ExpressionErrorListener(errors);
        lexer.AddErrorListener(errorListener);
        parser.AddErrorListener(errorListener);

        return parser;
    }

    private sealed class ExpressionErrorListener
        : IAntlrErrorListener<IToken>,
            IAntlrErrorListener<int>
    {
        private readonly List<string> _errors;

        public ExpressionErrorListener(List<string> errors)
        {
            _errors = errors;
        }

        public void SyntaxError(
            TextWriter output,
            IRecognizer recognizer,
            IToken offendingSymbol,
            int line,
            int charPositionInLine,
            string msg,
            RecognitionException e
        )
        {
            _errors.Add(msg);
        }

        public void SyntaxError(
            TextWriter output,
            IRecognizer recognizer,
            int offendingSymbol,
            int line,
            int charPositionInLine,
            string msg,
            RecognitionException e
        )
        {
            _errors.Add(msg);
        }
    }

    private sealed class EvaluatingVisitor : CascodeBaseVisitor<double>
    {
        private readonly Func<string, string?> _resolveIdentifier;

        public EvaluatingVisitor(Func<string, string?> resolveIdentifier)
        {
            _resolveIdentifier = resolveIdentifier;
        }

        public override double VisitExpr(CascodeParser.ExprContext ctx)
        {
            if (ctx.expr() is null)
            {
                return Visit(ctx.mulExpr());
            }

            var left = Visit(ctx.expr());
            var right = Visit(ctx.mulExpr());
            return ctx.PLUS() is not null ? left + right : left - right;
        }

        public override double VisitMulExpr(CascodeParser.MulExprContext ctx)
        {
            if (ctx.mulExpr() is null)
            {
                return Visit(ctx.unaryAtom());
            }

            var left = Visit(ctx.mulExpr());
            var right = Visit(ctx.unaryAtom());
            return ctx.STAR() is not null ? left * right
                : right != 0 ? left / right
                : throw new DivideByZeroException();
        }

        public override double VisitUnaryAtom(CascodeParser.UnaryAtomContext ctx)
        {
            if (ctx.MINUS() is not null)
            {
                return -Visit(ctx.unaryAtom());
            }

            return Visit(ctx.exprAtom());
        }

        public override double VisitExprAtom(CascodeParser.ExprAtomContext ctx)
        {
            if (ctx.expr() is not null)
            {
                return Visit(ctx.expr());
            }

            if (ctx.NUMBER() is not null || ctx.QUANTITY() is not null)
            {
                return ParameterEvaluator.ParseNumeric(ctx.GetText());
            }

            if (ctx.IDENT() is not null || ctx.sizeFieldAccess() is not null)
            {
                var name = ctx.GetText();
                var resolved = _resolveIdentifier(name);
                if (resolved is null)
                {
                    throw new ArgumentException($"Undefined parameter reference: {name}");
                }
                return ParameterEvaluator.ParseNumeric(resolved);
            }

            throw new ArgumentException($"Unsupported expression atom: {ctx.GetText()}");
        }
    }
}
