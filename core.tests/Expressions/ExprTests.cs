using OpenPolitics.Core.Expressions;

namespace OpenPolitics.Core.Tests.Expressions;

public class ExprTests
{
    private static Expr Parse(string text)
    {
        Assert.True(ExprParser.TryParse(text, out var expr, out var error), error);
        return expr!;
    }

    private static double Eval(string text, Dictionary<string, double>? vars = null) =>
        Parse(text).Eval(id => vars![id]);

    [Fact]
    public void ComparisonAndLogic()
    {
        var vars = new Dictionary<string, double> { ["base:unemployment"] = 0.2, ["base:inflation"] = 3 };
        Assert.Equal(1.0, Eval("base:unemployment > 0.1 and base:inflation < 5", vars));
        Assert.Equal(0.0, Eval("base:unemployment > 0.5 and base:inflation < 5", vars));
        Assert.Equal(1.0, Eval("base:unemployment > 0.5 or base:inflation < 5", vars));
    }

    [Fact]
    public void ArithmeticPrecedence()
    {
        Assert.Equal(1.0, Eval("1 + 2 * 3 == 7"));
        Assert.Equal(9.0, Eval("(1 + 2) * 3"));
        Assert.Equal(-2.0, Eval("1 - 3"));
        Assert.Equal(-5.0, Eval("-5"));
        Assert.Equal(0.25, Eval("1e0 / 4"));
    }

    [Fact]
    public void NotAndParentheses()
    {
        Assert.Equal(1.0, Eval("not (1 > 2)"));
        Assert.Equal(0.0, Eval("not 1"));
        Assert.Equal(1.0, Eval("not 1 > 2"));
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("")]
    [InlineData("(1 + 2")]
    [InlineData("1 2")]
    [InlineData("foo > 1")]
    [InlineData("1 @ 2")]
    public void ParseErrorGivesMessage(string text)
    {
        Assert.False(ExprParser.TryParse(text, out var expr, out var error));
        Assert.Null(expr);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void DivisionByZeroIsZero()
    {
        Assert.Equal(0.0, Eval("5 / 0"));
        Assert.Equal(0.0, Eval("5 / (1 - 1)"));
    }

    [Fact]
    public void ReferencesInOrderOfAppearanceWithoutDuplicates()
    {
        var e = Parse("base:b > base:a and base:b < 3 or base:c == 0");
        Assert.Equal(new[] { "base:b", "base:a", "base:c" }, e.References.ToArray());
    }
}
