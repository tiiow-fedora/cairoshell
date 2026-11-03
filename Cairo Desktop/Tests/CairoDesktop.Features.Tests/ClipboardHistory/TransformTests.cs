using System.Linq;
using CairoDesktop.Features.ClipboardHistory;
using Xunit;

namespace CairoDesktop.Features.Tests.ClipboardHistory
{
    public class TransformTests
    {
        private static string Ok(TransformResult r)
        {
            Assert.True(r.Success, r.Error);
            return r.Output;
        }

        [Fact] public void Trim() => Assert.Equal("a b", Ok(ClipTransforms.Trim("  a b \n")));
        [Fact] public void Upper() => Assert.Equal("HELLO É", Ok(ClipTransforms.Upper("hello é")));
        [Fact] public void Lower() => Assert.Equal("hello", Ok(ClipTransforms.Lower("HeLLo")));

        [Fact]
        public void Upper_InvariantNotTurkish()
        {
            Assert.Equal("TITLE", Ok(ClipTransforms.Upper("title")));
        }

        [Theory]
        [InlineData("hello world", "Hello World")]
        [InlineData("HELLO WORLD", "Hello World")]
        [InlineData("don't stop", "Don't Stop")]
        [InlineData("a-b c_d", "A-B C_D")]
        [InlineData("3rd place", "3rd Place")]
        [InlineData("  spaced   out ", "  Spaced   Out ")]
        public void TitleCase(string input, string expected) => Assert.Equal(expected, Ok(ClipTransforms.TitleCase(input)));

        [Fact]
        public void TitleCase_EmojiAndSurrogatesSurvive()
        {
            Assert.Equal("Hi \U0001F600 There", Ok(ClipTransforms.TitleCase("hi \U0001F600 there")));
            Assert.Equal("\U00010400x", Ok(ClipTransforms.TitleCase("\U00010400X"))); // astral letter untouched, rest lowercased
        }

        [Fact]
        public void TitleCase_LoneSurrogateDoesNotThrow()
        {
            Assert.True(ClipTransforms.TitleCase("a\uD800b").Success);
        }

        [Fact]
        public void StripFormatting_NormalisesNewlinesAndInvisibles()
        {
            string input = "a\r\nb\rc​ d \t\n﻿e";
            Assert.Equal("a\nb\nc d\ne", Ok(ClipTransforms.StripFormatting(input)));
        }

        [Fact]
        public void StripFormatting_RemovesAnsiColour()
        {
            Assert.Equal("red", Ok(ClipTransforms.StripFormatting("\u001B[31mred\u001B[0m")));
        }

        [Fact]
        public void JoinLines_SkipsBlankAndTrims()
        {
            Assert.Equal("a b c", Ok(ClipTransforms.JoinLines(" a \r\n\r\n b\n  c  \n")));
        }

        [Fact]
        public void SortLines_CaseInsensitiveWithDeterministicTies()
        {
            Assert.Equal("A\na\nb\nC", Ok(ClipTransforms.SortLines("b\nC\na\nA")));
        }

        [Fact]
        public void SortLines_PreservesCrLf()
        {
            Assert.Equal("a\r\nb", Ok(ClipTransforms.SortLines("b\r\na\r\n")));
        }

        [Fact]
        public void SortLines_UnicodeOrdinal()
        {
            Assert.Equal("z\né", Ok(ClipTransforms.SortLines("é\nz")));
        }

        [Fact]
        public void DedupeLines_KeepsFirstAndOrder()
        {
            Assert.Equal("b\na\nA\nc", Ok(ClipTransforms.DedupeLines("b\na\nb\nA\na\nc")));
        }

        [Fact]
        public void DedupeLines_KeepsBlankLineOnce()
        {
            Assert.Equal("a\n\nb", Ok(ClipTransforms.DedupeLines("a\n\n\nb")));
        }

        [Fact]
        public void UrlEncode_ReservedAndUnicode()
        {
            Assert.Equal("a%20b%26c%3D%C3%A9", Ok(ClipTransforms.UrlEncode("a b&c=é")));
        }

        [Fact]
        public void UrlEncode_SurrogatePairBecomesFourByteUtf8()
        {
            Assert.Equal("%F0%9F%98%80", Ok(ClipTransforms.UrlEncode("\U0001F600")));
        }

        [Fact]
        public void UrlEncode_LongInputIsChunkedAndRoundTrips()
        {
            string big = string.Concat(Enumerable.Repeat("a\U0001F600 ", 10000));
            string enc = Ok(ClipTransforms.UrlEncode(big));
            Assert.Equal(big, Ok(ClipTransforms.UrlDecode(enc)));
        }

        [Fact]
        public void UrlDecode_LeavesPlusAndMalformedPercent()
        {
            Assert.Equal("a+b%zz", Ok(ClipTransforms.UrlDecode("a+b%zz")));
            Assert.Equal("a b", Ok(ClipTransforms.UrlDecode("a%20b")));
        }

        [Fact]
        public void Base64_RoundTripUnicode()
        {
            string enc = Ok(ClipTransforms.Base64Encode("héllo \U0001F600"));
            Assert.Equal("héllo \U0001F600", Ok(ClipTransforms.Base64Decode(enc)));
        }

        [Fact]
        public void Base64Encode_KnownValue() => Assert.Equal("aGVsbG8=", Ok(ClipTransforms.Base64Encode("hello")));

        [Fact]
        public void Base64Encode_LoneSurrogateFailsGracefully()
        {
            var r = ClipTransforms.Base64Encode("a\uD800");
            Assert.False(r.Success);
            Assert.NotNull(r.Error);
        }

        [Theory]
        [InlineData("aGVsbG8")]       // missing padding
        [InlineData("aGVs bG8=")]     // whitespace
        [InlineData("aGVsbG8=\n")]
        public void Base64Decode_Lenient(string input) => Assert.Equal("hello", Ok(ClipTransforms.Base64Decode(input)));

        [Fact]
        public void Base64Decode_UrlSafeAlphabet()
        {
            string std = Ok(ClipTransforms.Base64Encode("??>>"));
            string urlSafe = std.Replace('+', '-').Replace('/', '_').TrimEnd('=');
            Assert.Equal("??>>", Ok(ClipTransforms.Base64Decode(urlSafe)));
        }

        [Theory]
        [InlineData("!!!!")]
        [InlineData("a")]
        [InlineData("")]
        [InlineData("   ")]
        public void Base64Decode_BadInputFails(string input)
        {
            var r = ClipTransforms.Base64Decode(input);
            Assert.False(r.Success);
            Assert.False(string.IsNullOrEmpty(r.Error));
        }

        [Fact]
        public void Base64Decode_NonUtf8BytesFail()
        {
            var r = ClipTransforms.Base64Decode("/w=="); // 0xFF
            Assert.False(r.Success);
        }

        [Fact]
        public void JsonPretty_FormatsWithTwoSpaces()
        {
            Assert.Equal("{\n  \"a\": 1,\n  \"b\": [\n    true,\n    null\n  ]\n}", Ok(ClipTransforms.JsonPretty("{\"a\":1,\"b\":[true,null]}")));
        }

        [Fact]
        public void JsonMinify_RemovesWhitespace()
        {
            Assert.Equal("{\"a\":1,\"b\":[1,2]}", Ok(ClipTransforms.JsonMinify("{ \"a\" : 1,\n \"b\": [1, 2] }")));
        }

        [Fact]
        public void Json_KeepsUnicodeAndBigNumbers()
        {
            Assert.Equal("{\"k\":\"é\U0001F600\",\"n\":12345678901234567890123}", Ok(ClipTransforms.JsonMinify("{\"k\":\"é\U0001F600\",\"n\":12345678901234567890123}")));
        }

        [Fact]
        public void Json_EscapedBackslashBeforeUEscapeIsNotMistakenForSurrogates()
        {
            string input = "{\"k\":\"\\\\uD83D\\\\uDE00\"}"; // JSON text: "\\uD83D\\uDE00"
            Assert.Equal(input, Ok(ClipTransforms.JsonMinify(input)));
        }

        [Theory]
        [InlineData("{\"a\":}")]
        [InlineData("not json")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{\"a\":1,}")]
        public void Json_InvalidFailsGracefully(string input)
        {
            Assert.False(ClipTransforms.JsonPretty(input).Success);
            Assert.False(ClipTransforms.JsonMinify(input).Success);
        }

        [Fact]
        public void Json_TooDeepFailsGracefully()
        {
            string deep = new string('[', 200) + new string(']', 200);
            var r = ClipTransforms.JsonPretty(deep);
            Assert.False(r.Success);
        }

        [Fact]
        public void AllTransforms_NullInputFailsWithoutThrowing()
        {
            foreach (ITransform t in TransformRegistry.CreateDefault().All)
            {
                var r = t.Apply(null);
                Assert.False(r.Success);
            }
        }

        [Fact]
        public void AllTransforms_EmptyStringDoesNotThrow()
        {
            foreach (ITransform t in TransformRegistry.CreateDefault().All)
                t.Apply("");
        }

        [Fact]
        public void Registry_HasAllDocumentedNames()
        {
            var names = TransformRegistry.CreateDefault().Names.ToArray();
            Assert.Equal(new[] { "trim", "upper", "lower", "title", "strip-formatting", "join-lines", "sort-lines", "dedupe-lines", "url-encode", "url-decode", "base64-encode", "base64-decode", "json-pretty", "json-minify" }, names);
        }

        [Fact]
        public void Registry_LookupIsCaseInsensitiveAndTrims()
        {
            var r = TransformRegistry.CreateDefault();
            Assert.True(r.TryGet(" JSON-Pretty ", out ITransform t));
            Assert.Equal("json-pretty", t.Name);
        }

        [Fact]
        public void Registry_UnknownNameIsFailureNotException()
        {
            var res = TransformRegistry.CreateDefault().Apply("nope", "x");
            Assert.False(res.Success);
            Assert.Contains("nope", res.Error);
        }

        [Fact]
        public void Registry_ThrowingTransformIsContained()
        {
            var r = new TransformRegistry();
            r.Register("bad", "d", s => throw new System.InvalidOperationException("x"));
            Assert.False(r.Apply("bad", "in").Success);
        }

        [Fact]
        public void Registry_RegisterReplacesSameName()
        {
            var r = TransformRegistry.CreateDefault();
            int before = r.All.Count;
            r.Register("upper", "custom", s => TransformResult.Ok("custom"));
            Assert.Equal(before, r.All.Count);
            Assert.Equal("custom", r.Apply("upper", "x").Output);
        }
    }
}
