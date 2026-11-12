namespace OpenPolitics.Core.Expressions;

/// <summary>
/// Expression tree for event conditions. Booleans are 1.0 (true) and 0.0 (false); division by zero is 0.
/// Evaluation is pure: it can only read values through the supplied lookup.
/// </summary>
public abstract class Expr
{
    public abstract double Eval(Func<string, double> lookup);
    public abstract IEnumerable<string> References { get; }

    internal sealed class Num(double value) : Expr
    {
        public override double Eval(Func<string, double> lookup) => value;
        public override IEnumerable<string> References => Array.Empty<string>();
    }

    internal sealed class Ref(string id) : Expr
    {
        public override double Eval(Func<string, double> lookup) => lookup(id);
        public override IEnumerable<string> References => new[] { id };
    }

    internal sealed class Unary(string op, Expr operand) : Expr
    {
        public override double Eval(Func<string, double> lookup)
        {
            double v = operand.Eval(lookup);
            return op == "not" ? (v == 0 ? 1.0 : 0.0) : -v;
        }
        public override IEnumerable<string> References => operand.References;
    }

    internal sealed class Binary(string op, Expr left, Expr right) : Expr
    {
        public override double Eval(Func<string, double> lookup)
        {
            // and/or evaluate both sides: expressions are pure, so this is only a matter of simplicity.
            double a = left.Eval(lookup), b = right.Eval(lookup);
            return op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => b == 0 ? 0.0 : a / b,
                "<" => a < b ? 1.0 : 0.0,
                "<=" => a <= b ? 1.0 : 0.0,
                ">" => a > b ? 1.0 : 0.0,
                ">=" => a >= b ? 1.0 : 0.0,
                "==" => a == b ? 1.0 : 0.0,
                "!=" => a != b ? 1.0 : 0.0,
                "and" => (a != 0 && b != 0) ? 1.0 : 0.0,
                "or" => (a != 0 || b != 0) ? 1.0 : 0.0,
                _ => throw new InvalidOperationException(op),
            };
        }
        public override IEnumerable<string> References => left.References.Concat(right.References);
    }
}
