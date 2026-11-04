using System;
using System.Globalization;
using CairoDesktop.Features.CommandPalette;
using Xunit;

namespace CairoDesktop.Features.Tests.CommandPalette
{
    public class CalculatorTests
    {
        private static double Ok(string expr)
        {
            var r = Calculator.Evaluate(expr);
            Assert.True(r.Success, expr + " => " + r.Error);
            return r.Value;
        }

        private static string Err(string expr)
        {
            var r = Calculator.Evaluate(expr);
            Assert.False(r.Success, expr + " should fail but gave " + r.Value);
            Assert.False(string.IsNullOrEmpty(r.Error));
            return r.Error;
        }

        [Theory]
        [InlineData("1+2", 3)]
        [InlineData("10-4", 6)]
        [InlineData("6*7", 42)]
        [InlineData("9/4", 2.25)]
        [InlineData("10%3", 1)]
        [InlineData("2^10", 1024)]
        [InlineData("  7 +   8 ", 15)]
        [InlineData("1.5+1.5", 3)]
        [InlineData(".5*4", 2)]
        [InlineData("1e3", 1000)]
        [InlineData("2.5E-1", 0.25)]
        public void BasicOperators(string expr, double expected) => Assert.Equal(expected, Ok(expr), 12);

        [Fact]
        public void MultiplicationBindsTighterThanAddition() => Assert.Equal(14, Ok("2+3*4"));

        [Fact]
        public void ParenthesesOverridePrecedence() => Assert.Equal(20, Ok("(2+3)*4"));

        [Fact]
        public void SubtractionIsLeftAssociative() => Assert.Equal(5, Ok("10-3-2"));

        [Fact]
        public void DivisionIsLeftAssociative() => Assert.Equal(2, Ok("16/4/2"));

        [Fact]
        public void PowerIsRightAssociative() => Assert.Equal(512, Ok("2^3^2"));

        [Fact]
        public void UnaryMinusBindsLooserThanPower() => Assert.Equal(-4, Ok("-2^2"));

        [Fact]
        public void ParenthesisedNegativeBase() => Assert.Equal(4, Ok("(-2)^2"));

        [Fact]
        public void NegativeExponent() => Assert.Equal(0.5, Ok("2^-1"));

        [Fact]
        public void DoubleUnaryMinus() => Assert.Equal(5, Ok("--5"));

        [Fact]
        public void UnaryPlus() => Assert.Equal(5, Ok("+5"));

        [Fact]
        public void MinusAfterOperator() => Assert.Equal(-6, Ok("2*-3"));

        [Fact]
        public void Constants()
        {
            Assert.Equal(Math.PI, Ok("pi"), 12);
            Assert.Equal(Math.E, Ok("E"), 12);
            Assert.Equal(2 * Math.PI, Ok("2*pi"), 12);
        }

        [Fact]
        public void Functions()
        {
            Assert.Equal(4, Ok("sqrt(16)"));
            Assert.Equal(3, Ok("abs(-3)"));
            Assert.Equal(3, Ok("round(2.5)"));
            Assert.Equal(-3, Ok("round(-2.5)"));
            Assert.Equal(3.14, Ok("round(3.14159, 2)"), 12);
            Assert.Equal(2, Ok("floor(2.9)"));
            Assert.Equal(3, Ok("ceil(2.1)"));
            Assert.Equal(1, Ok("min(3,1,2)"));
            Assert.Equal(3, Ok("max(3,1,2)"));
        }

        [Fact]
        public void FunctionNamesAreCaseInsensitive() => Assert.Equal(4, Ok("SQRT(16)"));

        [Fact]
        public void NestedFunctionsAndExpressions() => Assert.Equal(5, Ok("sqrt(9+16)"));

        [Fact]
        public void PercentOf() => Assert.Equal(10, Ok("20% of 50"));

        [Fact]
        public void PercentOfWithoutSpaces() => Assert.Equal(10, Ok("20%of 50"));

        [Fact]
        public void PercentOfIsCaseInsensitive() => Assert.Equal(10, Ok("20% OF 50"));

        [Fact]
        public void ModuloStillWorksNextToWordsStartingWithOf() => Assert.Equal(1, Ok("10 % 3"));

        [Fact]
        public void DivideByZeroIsError() => Assert.Contains("zero", Err("1/0"), StringComparison.OrdinalIgnoreCase);

        [Fact]
        public void ModuloByZeroIsError() => Assert.Contains("zero", Err("5%0"), StringComparison.OrdinalIgnoreCase);

        [Fact]
        public void OverflowIsError()
        {
            Err("10^400");
            Err("1e308*10");
        }

        [Fact]
        public void SqrtOfNegativeIsError() => Err("sqrt(-1)");

        [Fact]
        public void ZeroToNegativePowerIsError() => Err("0^-1");

        [Fact]
        public void NegativeBaseFractionalPowerIsError() => Err("(-8)^0.5");

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("+")]
        [InlineData("1+")]
        [InlineData("*2")]
        [InlineData("(1+2")]
        [InlineData("1+2)")]
        [InlineData("1 2")]
        [InlineData("2e")]
        [InlineData("1..2")]
        [InlineData(".")]
        [InlineData("foo")]
        [InlineData("foo(1)")]
        [InlineData("sqrt()")]
        [InlineData("sqrt(1,2)")]
        [InlineData("min()")]
        [InlineData("round(1,2,3)")]
        [InlineData("round(1,-1)")]
        [InlineData("round(1,1.5)")]
        [InlineData("1,5")]
        [InlineData("1 + $")]
        public void MalformedInputIsErrorNotException(string expr) => Err(expr);

        [Fact]
        public void NullIsError() => Assert.False(Calculator.Evaluate(null).Success);

        [Fact]
        public void TooLongIsError() => Err(new string('1', Calculator.MaxLength + 1));

        [Fact]
        public void DeepNestingIsErrorNotStackOverflow()
        {
            string expr = new string((char)40, 200) + "1" + new string((char)41, 200);
            Assert.Contains("deep", Err(expr), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LongUnaryChainIsBounded() => Err(new string('-', 400) + "1");

        [Fact]
        public void ModerateNestingStillWorks() => Assert.Equal(1, Ok(new string('(', 20) + "1" + new string(')', 20)));

        [Fact]
        public void ManyTermsAreFine() => Assert.Equal(100, Ok(string.Join("+", System.Linq.Enumerable.Repeat("1", 100))));

        [Fact]
        public void FormatIsTrimmedAndInvariant()
        {
            Assert.Equal("0.3", Calculator.Evaluate("0.1+0.2").Format());
            Assert.Equal("3", Calculator.Evaluate("6/2").Format());
            Assert.Equal("0", Calculator.Evaluate("-0").Format());
        }

        [Fact]
        public void FormatOfFailureIsTheError()
        {
            var r = Calculator.Evaluate("1/0");
            Assert.Equal(r.Error, r.Format());
        }

        [Theory]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        [InlineData("tr-TR")]
        [InlineData("ar-SA")]
        public void IsLocaleIndependent(string culture)
        {
            var old = CultureInfo.CurrentCulture;
            var oldUi = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                CultureInfo.CurrentUICulture = new CultureInfo(culture);
                Assert.Equal(3.5, Ok("1.5+2"));
                Assert.Equal("3.5", Calculator.Evaluate("1.5+2").Format());
                Assert.Equal(1000, Ok("1e3"));
                Err("1,5+2");
            }
            finally
            {
                CultureInfo.CurrentCulture = old;
                CultureInfo.CurrentUICulture = oldUi;
            }
        }
    }
}
