using System.Globalization;

namespace Lumo.Core;

/// <summary>
/// Small, safe arithmetic expression evaluator ("C/" prefix).
/// Recursive-descent parser with a hard depth guard — no external packages, no Eval,
/// cannot loop forever on any input.
/// </summary>
public static class Calculator
{
    public static bool TryEvaluate(string? expression, out string result)
    {
        result = string.Empty;
        if (string.IsNullOrWhiteSpace(expression)) return false;

        var expr = expression.Trim();
        if (expr.StartsWith("C/", StringComparison.OrdinalIgnoreCase)) expr = expr[2..].Trim();
        if (expr.Length == 0 || expr.Length > 256) return false;

        try
        {
            var parser = new Parser(expr.Replace(',', ' ').Replace('×', '*').Replace('÷', '/'));
            double value = parser.ParseExpression(depth: 0);
            if (parser.Position < parser.Length && !parser.SkipTrailing()) return false;
            if (double.IsNaN(value) || double.IsInfinity(value)) return false;

            result = FormatNumber(value);
            return true;
        }
        catch (DivideByZeroException)
        {
            result = "Cannot divide by zero";
            return true; // show as result text, not as a numeric answer
        }
        catch
        {
            return false;
        }
    }

    private static string FormatNumber(double v)
    {
        var s = v.ToString("G15", CultureInfo.InvariantCulture);
        if (s.Contains('E')) s = v.ToString("F6", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        return s;
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _pos;
        private const int MaxDepth = 64;

        public Parser(string s) { _s = s; }
        public int Position => _pos;
        public int Length => _s.Length;

        private void SkipWhite()
        {
            while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++;
        }

        private bool Eat(char c)
        {
            SkipWhite();
            if (_pos < _s.Length && _s[_pos] == c) { _pos++; return true; }
            return false;
        }

        public bool SkipTrailing() { SkipWhite(); return _pos >= _s.Length; }

        public double ParseExpression(int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("Expression too deep");
            var left = ParseTerm(depth);
            while (true)
            {
                SkipWhite();
                if (_pos < _s.Length && (_s[_pos] == '+' || _s[_pos] == '-'))
                {
                    char op = _s[_pos++];
                    var right = ParseTerm(depth);
                    left = op == '+' ? left + right : left - right;
                }
                else return left;
            }
        }

        private double ParseTerm(int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("Expression too deep");
            var left = ParsePower(depth);
            while (true)
            {
                SkipWhite();
                if (_pos < _s.Length && (_s[_pos] == '*' || _s[_pos] == '/' || _s[_pos] == '%'))
                {
                    char op = _s[_pos++];
                    var right = ParsePower(depth);
                    left = op switch
                    {
                        '*' => left * right,
                        '/' => right == 0 ? throw new DivideByZeroException() : left / right,
                        _ => right == 0 ? throw new DivideByZeroException() : left % right,
                    };
                }
                else return left;
            }
        }

        private double ParsePower(int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("Expression too deep");
            var @base = ParseUnary(depth);
            if (Eat('^'))
            {
                var exp = ParsePower(depth + 1);
                return Math.Pow(@base, exp);
            }
            return @base;
        }

        private double ParseUnary(int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("Expression too deep");
            SkipWhite();
            if (_pos < _s.Length && (_s[_pos] == '-' || _s[_pos] == '+'))
            {
                char op = _s[_pos++];
                var v = ParseUnary(depth + 1);
                return op == '-' ? -v : v;
            }
            return ParseAtom(depth);
        }

        private double ParseAtom(int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("Expression too deep");
            SkipWhite();
            if (_pos >= _s.Length) throw new FormatException("Unexpected end");

            if (_s[_pos] == '(')
            {
                _pos++;
                var v = ParseExpression(depth + 1);
                if (!Eat(')')) throw new FormatException("Missing ')'");
                return v;
            }

            // Function call: name followed by '(' — or single-argument shorthand name 9
            int start = _pos;
            while (_pos < _s.Length && (char.IsLetter(_s[_pos]))) _pos++;
            if (_pos > start)
            {
                var name = _s[start.._pos].ToLowerInvariant();
                double arg;
                if (Eat('('))
                {
                    arg = ParseExpression(depth + 1);
                    if (!Eat(')')) throw new FormatException("Missing ')'");
                }
                else
                {
                    arg = ParseUnary(depth + 1);
                }

                return name switch
                {
                    "sqrt" => Math.Sqrt(arg),
                    "abs" => Math.Abs(arg),
                    "sin" => Math.Sin(arg),
                    "cos" => Math.Cos(arg),
                    "tan" => Math.Tan(arg),
                    "log" => Math.Log10(arg),
                    "ln" => Math.Log(arg),
                    "floor" => Math.Floor(arg),
                    "ceil" => Math.Ceiling(arg),
                    "round" => Math.Round(arg),
                    _ => throw new FormatException($"Unknown function '{name}'"),
                };
            }

            // Constant or number
            if (MatchWord("pi")) return Math.PI;
            if (MatchWord("e")) return Math.E;

            int numStart = _pos;
            while (_pos < _s.Length && (char.IsDigit(_s[_pos]) || _s[_pos] == '.')) _pos++;
            if (_pos == numStart) throw new FormatException("Number expected");
            return double.Parse(_s[numStart.._pos], CultureInfo.InvariantCulture);
        }

        private bool MatchWord(string word)
        {
            SkipWhite();
            if (_pos + word.Length <= _s.Length &&
                string.CompareOrdinal(_s, _pos, word, 0, word.Length) == 0)
            {
                // avoid treating "exp" as "e" followed by junk
                int after = _pos + word.Length;
                if (after >= _s.Length || !char.IsLetter(_s[after]))
                {
                    _pos = after;
                    return true;
                }
            }
            return false;
        }
    }
}
