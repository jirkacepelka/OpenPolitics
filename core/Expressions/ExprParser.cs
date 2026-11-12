using System.Globalization;

namespace OpenPolitics.Core.Expressions;

/// <summary>
/// Grammar (lowest to highest precedence): or, and, not, comparison, + -, * /, unary -, primary.
/// Primary: number, namespaced value id (<c>pack:name</c>), or parenthesised expression.
/// </summary>
public static class ExprParser
{
    public static bool TryParse(string text, out Expr? expr, out string? error)
    {
        expr = null; error = null;
        try
        {
            var p = new Parser(text ?? "");
            expr = p.ParseAll();
            // References are normalised to distinct, in order of first appearance.
            expr = new Distinct(expr);
            return true;
        }
        catch (FormatException e)
        {
            error = e.Message;
            return false;
        }
    }

    private sealed class Distinct(Expr inner) : Expr
    {
        public override double Eval(Func<string, double> lookup) => inner.Eval(lookup);
        public override IEnumerable<string> References => inner.References.Distinct(StringComparer.Ordinal);
    }

    private sealed class Parser
    {
        private readonly List<(string Kind, string Text, int Pos)> _tokens = new();
        private int _i;
        private readonly string _src;

        public Parser(string src)
        {
            _src = src;
            Tokenize();
        }

        private void Tokenize()
        {
            int i = 0;
            while (i < _src.Length)
            {
                char c = _src[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (char.IsDigit(c) || (c == '.' && i + 1 < _src.Length && char.IsDigit(_src[i + 1])))
                {
                    int s = i;
                    while (i < _src.Length && (char.IsDigit(_src[i]) || _src[i] == '.')) i++;
                    if (i < _src.Length && (_src[i] == 'e' || _src[i] == 'E'))
                    {
                        int j = i + 1;
                        if (j < _src.Length && (_src[j] == '+' || _src[j] == '-')) j++;
                        if (j < _src.Length && char.IsDigit(_src[j]))
                        {
                            while (j < _src.Length && char.IsDigit(_src[j])) j++;
                            i = j;
                        }
                    }
                    _tokens.Add(("num", _src[s..i], s));
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int s = i;
                    while (i < _src.Length && (char.IsLetterOrDigit(_src[i]) || _src[i] == '_')) i++;
                    string word = _src[s..i];
                    if (i < _src.Length && _src[i] == ':' && i + 1 < _src.Length && (char.IsLetterOrDigit(_src[i + 1]) || _src[i + 1] == '_'))
                    {
                        i++;
                        while (i < _src.Length && (char.IsLetterOrDigit(_src[i]) || _src[i] == '_')) i++;
                        _tokens.Add(("id", _src[s..i], s));
                    }
                    else if (word is "and" or "or" or "not") _tokens.Add((word, word, s));
                    else throw new FormatException($"Unknown word '{word}' at position {s}; value ids look like 'pack:name'.");
                    continue;
                }
                string two = i + 1 < _src.Length ? _src.Substring(i, 2) : "";
                if (two is "<=" or ">=" or "==" or "!=") { _tokens.Add(("op", two, i)); i += 2; continue; }
                if ("+-*/<>()".IndexOf(c) >= 0) { _tokens.Add(("op", c.ToString(), i)); i++; continue; }
                throw new FormatException($"Unexpected character '{c}' at position {i}.");
            }
        }

        private (string Kind, string Text, int Pos)? Peek => _i < _tokens.Count ? _tokens[_i] : null;

        private bool IsOp(string text) => Peek is { Kind: "op" } t && t.Text == text;

        public Expr ParseAll()
        {
            if (_tokens.Count == 0) throw new FormatException("Expression is empty.");
            var e = ParseOr();
            if (Peek is { } t) throw new FormatException($"Unexpected '{t.Text}' at position {t.Pos}.");
            return e;
        }

        private Expr ParseOr()
        {
            var left = ParseAnd();
            while (Peek is { Kind: "or" }) { _i++; left = new Expr.Binary("or", left, ParseAnd()); }
            return left;
        }

        private Expr ParseAnd()
        {
            var left = ParseNot();
            while (Peek is { Kind: "and" }) { _i++; left = new Expr.Binary("and", left, ParseNot()); }
            return left;
        }

        private Expr ParseNot()
        {
            if (Peek is { Kind: "not" }) { _i++; return new Expr.Unary("not", ParseNot()); }
            return ParseComparison();
        }

        private Expr ParseComparison()
        {
            var left = ParseAdditive();
            while (Peek is { Kind: "op" } t && t.Text is "<" or "<=" or ">" or ">=" or "==" or "!=")
            {
                _i++;
                left = new Expr.Binary(t.Text, left, ParseAdditive());
            }
            return left;
        }

        private Expr ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (IsOp("+") || IsOp("-"))
            {
                string op = _tokens[_i++].Text;
                left = new Expr.Binary(op, left, ParseMultiplicative());
            }
            return left;
        }

        private Expr ParseMultiplicative()
        {
            var left = ParseUnary();
            while (IsOp("*") || IsOp("/"))
            {
                string op = _tokens[_i++].Text;
                left = new Expr.Binary(op, left, ParseUnary());
            }
            return left;
        }

        private Expr ParseUnary()
        {
            if (IsOp("-")) { _i++; return new Expr.Unary("-", ParseUnary()); }
            return ParsePrimary();
        }

        private Expr ParsePrimary()
        {
            if (Peek is not { } t) throw new FormatException("Expression ends unexpectedly.");
            _i++;
            switch (t.Kind)
            {
                case "num":
                    if (!double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        throw new FormatException($"Bad number '{t.Text}' at position {t.Pos}.");
                    return new Expr.Num(v);
                case "id":
                    return new Expr.Ref(t.Text);
                case "op" when t.Text == "(":
                    var inner = ParseOr();
                    if (!IsOp(")")) throw new FormatException($"Missing ')' for '(' at position {t.Pos}.");
                    _i++;
                    return inner;
                default:
                    throw new FormatException($"Unexpected '{t.Text}' at position {t.Pos}.");
            }
        }
    }
}
