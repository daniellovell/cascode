using System;
using System.Collections.Generic;

namespace Cascode.Language.BenchRuntime;

internal static class BenchPrimitiveCallFinder
{
    public static bool ContainsCall(BenchDefinition bench, string name)
    {
        ArgumentNullException.ThrowIfNull(bench);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        foreach (var a in bench.Analyses)
        {
            foreach (var expr in a.Parameters.Values)
            {
                if (Contains(expr, name))
                {
                    return true;
                }
            }
        }

        foreach (var m in bench.Measurements)
        {
            foreach (var stmt in m.Body)
            {
                if (Contains(stmt, name))
                {
                    return true;
                }
            }
        }

        foreach (var fn in bench.Functions)
        {
            foreach (var stmt in fn.Body)
            {
                if (Contains(stmt, name))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Contains(BenchStatement stmt, string name)
    {
        return stmt switch
        {
            BenchVarDecl v => Contains(v.Expr, name),
            BenchReturn r => Contains(r.Expr, name),
            BenchIf i => Contains(i.Condition, name)
                || Any(i.ThenBody, name)
                || (i.ElseBody is not null && Any(i.ElseBody, name)),
            _ => false,
        };
    }

    private static bool Any(IEnumerable<BenchStatement> statements, string name)
    {
        foreach (var s in statements)
        {
            if (Contains(s, name))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Contains(BoolExpr expr, string name)
    {
        return expr switch
        {
            BoolCompare c => Contains(c.Left, name) || Contains(c.Right, name),
            BoolTruthy t => Contains(t.Expr, name),
            _ => false,
        };
    }

    private static bool Contains(MeasurementExpr expr, string name)
    {
        switch (expr)
        {
            case MeasurementCall c:
                if (c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                foreach (var a in c.Args)
                {
                    if (Contains(a.Value, name))
                    {
                        return true;
                    }
                }
                return false;

            case MeasurementMethodCall m:
                if (Contains(m.Receiver, name))
                {
                    return true;
                }
                foreach (var a in m.Args)
                {
                    if (Contains(a.Value, name))
                    {
                        return true;
                    }
                }
                return false;

            case MeasurementBinary b:
                return Contains(b.Left, name) || Contains(b.Right, name);

            case MeasurementUnary u:
                return Contains(u.Operand, name);

            case MeasurementConditional c:
                return Contains(c.Condition, name)
                    || Contains(c.ThenExpr, name)
                    || Contains(c.ElseExpr, name);

            default:
                return false;
        }
    }
}
