using System;
using System.Collections.Generic;
using System.Globalization;

namespace CairoDesktop.Features.CommandPalette
{
    /// <summary>Result of <see cref="Calculator.Evaluate"/>. Exactly one of Value/Error is meaningful. Thread-safety: immutable.</summary>
    public readonly struct CalcResult
    {
        private CalcResult(bool ok, double value, string error)
        {
            Success = ok;
            Value = value;
            Error = error;
        }

        public bool Success { get; }
        public double Value { get; }
        public string Error { get; }

        public static CalcResult Ok(double value) => new CalcResult(true, value, null);
        public static CalcResult Fail(string error) => new CalcResult(false, 0, error);

        /// <summary>Invariant-culture text for the value (up to 15 significant digits), e.g. "0.3" for 0.1+0.2.</summary>
        public string Format()
        {
            if (!Success) return Error;
            double v = Value == 0 ? 0 : Value; // normalise -0
            return v.ToString("G15", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Safe expression evaluator (recursive descent; no eval, no reflection, never throws to the caller).
    /// <code>
    /// expr    := term (('+'|'-') term)*
    /// term    := unary (('*'|'/'|'%') unary)*
    /// unary   := ('-'|'+') unary | power
    /// power   := primary ('^' unary)?          right associative; -2^2 = -4; 2^-1 = 0.5
    /// primary := number | '(' expr ')' | name '(' args ')' | constant | number '%' 'of' unary
    /// </code>
    /// Functions: sqrt abs round(x[,digits]) floor ceil min max. Constants: pi e. Numbers use '.' as decimal point
    /// and may have an exponent (1e3), regardless of the current culture. '%' between operands is modulo;
    /// "20% of 50" is 10. Input is limited to <see cref="MaxLength"/> characters and nesting to <see cref="MaxDepth"/>.
    /// Thread-safety: stateless and safe to call from any thread.
    /// </summary>
    public static class Calculator
    {
        public const int MaxLength = 512;
        public const int MaxDepth = 64;
        private const int MaxArguments = 32;

        public static CalcResult Evaluate(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return CalcResult.Fail("Empty expression.");
            if (expression.Length > MaxLength) return CalcResult.Fail("Expression is too long.");

            try
            {
                var parser = new Parser(expression);
                double value = parser.ParseAll();
                if (double.IsNaN(value) || double.IsInfinity(value)) return CalcResult.Fail("Result is out of range.");
                return CalcResult.Ok(value);
            }
            catch (CalcException ex)
            {
                return CalcResult.Fail(ex.Message);
            }
        }

        private sealed class CalcException : Exception
        {
            public CalcException(string message) : base(message) { }
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _pos;
            private int _depth;

            public Parser(string s) { _s = s; }

            public double ParseAll()
            {
                double v = ParseExpr();
                SkipWs();
                if (_pos < _s.Length) throw new CalcException($"Unexpected '{_s[_pos]}' at position {_pos + 1}.");
                return v;
            }

            private void Enter()
            {
                if (++_depth > MaxDepth) throw new CalcException("Expression is nested too deeply.");
            }

            private void Leave() { _depth--; }

            private void SkipWs()
            {
                while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++;
            }

            private bool Peek(char c)
            {
                SkipWs();
                return _pos < _s.Length && _s[_pos] == c;
            }

            private double Check(double v)
            {
                if (double.IsNaN(v) || double.IsInfinity(v)) throw new CalcException("Result is out of range.");
                return v;
            }

            private double ParseExpr()
            {
                Enter();
                double left = ParseTerm();
                while (true)
                {
                    if (Peek('+')) { _pos++; left = Check(left + ParseTerm()); }
                    else if (Peek('-')) { _pos++; left = Check(left - ParseTerm()); }
                    else break;
                }
                Leave();
                return left;
            }

            private double ParseTerm()
            {
                double left = ParseUnary();
                while (true)
                {
                    if (Peek('*')) { _pos++; left = Check(left * ParseUnary()); }
                    else if (Peek('/'))
                    {
                        _pos++;
                        double r = ParseUnary();
                        if (r == 0) throw new CalcException("Division by zero.");
                        left = Check(left / r);
                    }
                    else if (Peek('%'))
                    {
                        _pos++;
                        double r = ParseUnary();
                        if (r == 0) throw new CalcException("Division by zero.");
                        left = Check(left % r);
                    }
                    else break;
                }
                return left;
            }

            private double ParseUnary()
            {
                Enter();
                double result;
                if (Peek('-')) { _pos++; result = -ParseUnary(); }
                else if (Peek('+')) { _pos++; result = ParseUnary(); }
                else result = ParsePower();
                Leave();
                return result;
            }

            private double ParsePower()
            {
                double b = ParsePrimary();

                // "20% of 50": a percent sign directly followed by the word "of".
                if (Peek('%') && IsWordAt(_pos + 1, "of"))
                {
                    SkipWs();
                    _pos++;
                    SkipWs();
                    _pos += 2;
                    return Check(b / 100.0 * ParseUnary());
                }

                if (Peek('^'))
                {
                    _pos++;
                    double exp = ParseUnary();
                    double p = Math.Pow(b, exp);
                    if (double.IsNaN(p)) throw new CalcException("Invalid power.");
                    return Check(p);
                }

                return b;
            }

            private bool IsWordAt(int index, string word)
            {
                while (index < _s.Length && char.IsWhiteSpace(_s[index])) index++;
                if (string.Compare(_s, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
                int end = index + word.Length;
                return end >= _s.Length || !char.IsLetterOrDigit(_s[end]);
            }

            private double ParsePrimary()
            {
                SkipWs();
                if (_pos >= _s.Length) throw new CalcException("Unexpected end of expression.");

                char c = _s[_pos];
                if (c == '(')
                {
                    _pos++;
                    double v = ParseExpr();
                    if (!Peek(')')) throw new CalcException("Missing ')'.");
                    _pos++;
                    return v;
                }

                if ((c >= '0' && c <= '9') || c == '.') return ParseNumber();
                if (IsAsciiLetter(c)) return ParseNameOrCall();

                throw new CalcException($"Unexpected '{c}' at position {_pos + 1}.");
            }

            private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

            private double ParseNumber()
            {
                int start = _pos;
                bool digits = false;
                while (_pos < _s.Length && _s[_pos] >= '0' && _s[_pos] <= '9') { _pos++; digits = true; }
                if (_pos < _s.Length && _s[_pos] == '.')
                {
                    _pos++;
                    while (_pos < _s.Length && _s[_pos] >= '0' && _s[_pos] <= '9') { _pos++; digits = true; }
                }
                if (!digits) throw new CalcException("Invalid number.");

                if (_pos < _s.Length && (_s[_pos] == 'e' || _s[_pos] == 'E'))
                {
                    int save = _pos;
                    _pos++;
                    if (_pos < _s.Length && (_s[_pos] == '+' || _s[_pos] == '-')) _pos++;
                    int expStart = _pos;
                    while (_pos < _s.Length && _s[_pos] >= '0' && _s[_pos] <= '9') _pos++;
                    if (_pos == expStart) _pos = save; // "2e" is 2 followed by junk, reported by the caller
                }

                string text = _s.Substring(start, _pos - start);
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw new CalcException($"Invalid number '{text}'.");
                return Check(value);
            }

            private double ParseNameOrCall()
            {
                int start = _pos;
                while (_pos < _s.Length && (IsAsciiLetter(_s[_pos]) || (_s[_pos] >= '0' && _s[_pos] <= '9'))) _pos++;
                string name = _s.Substring(start, _pos - start).ToLowerInvariant();

                if (!Peek('('))
                {
                    if (name == "pi") return Math.PI;
                    if (name == "e") return Math.E;
                    throw new CalcException($"Unknown name '{name}'.");
                }

                _pos++; // (
                var args = new List<double>();
                if (!Peek(')'))
                {
                    while (true)
                    {
                        args.Add(ParseExpr());
                        if (args.Count > MaxArguments) throw new CalcException("Too many arguments.");
                        if (Peek(',')) { _pos++; continue; }
                        break;
                    }
                }
                if (!Peek(')')) throw new CalcException("Missing ')'.");
                _pos++;

                return Check(Apply(name, args));
            }

            private static double Apply(string name, List<double> a)
            {
                switch (name)
                {
                    case "sqrt":
                        Require(name, a, 1, 1);
                        if (a[0] < 0) throw new CalcException("Square root of a negative number.");
                        return Math.Sqrt(a[0]);
                    case "abs": Require(name, a, 1, 1); return Math.Abs(a[0]);
                    case "floor": Require(name, a, 1, 1); return Math.Floor(a[0]);
                    case "ceil": Require(name, a, 1, 1); return Math.Ceiling(a[0]);
                    case "round":
                        Require(name, a, 1, 2);
                        if (a.Count == 1) return Math.Round(a[0], MidpointRounding.AwayFromZero);
                        if (a[1] < 0 || a[1] > 15 || a[1] != Math.Floor(a[1])) throw new CalcException("round() digits must be a whole number from 0 to 15.");
                        return Math.Round(a[0], (int)a[1], MidpointRounding.AwayFromZero);
                    case "min":
                    case "max":
                        Require(name, a, 1, MaxArguments);
                        double r = a[0];
                        for (int i = 1; i < a.Count; i++) r = name == "min" ? Math.Min(r, a[i]) : Math.Max(r, a[i]);
                        return r;
                    default:
                        throw new CalcException($"Unknown function '{name}'.");
                }
            }

            private static void Require(string name, List<double> a, int min, int max)
            {
                if (a.Count < min || a.Count > max)
                    throw new CalcException($"{name}() takes {(min == max ? min.ToString(CultureInfo.InvariantCulture) : min + " to " + max)} argument(s).");
            }
        }
    }
}
