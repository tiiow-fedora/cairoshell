using System.Collections.Generic;
using CairoDesktop.Features.ClipboardHistory;
using Xunit;

namespace CairoDesktop.Features.Tests.ClipboardHistory
{
    public class SensitiveTests
    {
        private static SensitivePolicy Default() => new SensitivePolicy();

        private static SensitiveReason Reason(string text, string app = null, ClipboardFormatFlags flags = default(ClipboardFormatFlags), SensitivePolicy p = null)
            => (p ?? Default()).Evaluate(text, app, flags).Reason;

        [Theory]
        [InlineData("4111111111111111")]            // Visa test
        [InlineData("4111 1111 1111 1111")]
        [InlineData("4111-1111-1111-1111")]
        [InlineData("5500005555555559")]            // Mastercard test
        [InlineData("378282246310005")]             // Amex test (15)
        [InlineData("3782 822463 10005")]           // Amex grouped 4-6-5
        [InlineData("4222222222222")]               // 13-digit Visa test
        [InlineData("my card is 4111 1111 1111 1111 ok")]
        public void CreditCard_ValidLuhnIsBlocked(string text)
        {
            Assert.Equal(SensitiveReason.CreditCard, Reason(text));
        }

        [Theory]
        [InlineData("4111111111111112")]            // Luhn fails
        [InlineData("1234567890123456")]            // Luhn fails
        [InlineData("1111111111111111")]            // all same digit, passes Luhn? still ignored
        [InlineData("1700000000000")]               // epoch ms (13 digits, starts with 1)
        [InlineData("order 4111111111111111 shipped")] // bare run inside prose is not treated as a card
        [InlineData("555-1234")]
        [InlineData("12345678901")]                 // too short
        public void CreditCard_FalsePositivesAllowed(string text)
        {
            Assert.Equal(SensitiveReason.None, Reason(text));
        }

        [Fact]
        public void Luhn_KnownGoodAndBad()
        {
            Assert.True(SensitivePolicy.IsLuhnValid("79927398713"));
            Assert.False(SensitivePolicy.IsLuhnValid("79927398710"));
            Assert.False(SensitivePolicy.IsLuhnValid(""));
            Assert.False(SensitivePolicy.IsLuhnValid("12a4"));
            Assert.False(SensitivePolicy.IsLuhnValid(null));
        }

        [Theory]
        [InlineData("AKIAIOSFODNN7EXAMPLE")]
        [InlineData("aws_access_key_id = AKIAIOSFODNN7EXAMPLE")]
        [InlineData("ASIAIOSFODNN7EXAMPLE")]
        public void AwsKey_Blocked(string text)
        {
            Assert.Equal(SensitiveReason.AwsAccessKey, Reason(text));
        }

        [Theory]
        [InlineData("AKIAIOSFODNN7EXAMPL")]          // 15 after prefix
        [InlineData("AKIAIOSFODNN7EXAMPLEXTRA")]     // runs on
        [InlineData("akiaiosfodnn7example")]
        public void AwsKey_NearMissesAllowed(string text)
        {
            Assert.NotEqual(SensitiveReason.AwsAccessKey, Reason(text));
        }

        [Theory]
        [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIE...\n-----END RSA PRIVATE KEY-----")]
        [InlineData("-----BEGIN PRIVATE KEY-----")]
        [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----")]
        [InlineData("-----BEGIN PGP PRIVATE KEY BLOCK-----")]
        public void PrivateKey_Blocked(string text)
        {
            Assert.Equal(SensitiveReason.PrivateKey, Reason(text));
        }

        [Fact]
        public void PublicKeyAndCertificate_Allowed()
        {
            Assert.Equal(SensitiveReason.None, Reason("-----BEGIN PUBLIC KEY-----\nabc\n-----END PUBLIC KEY-----"));
            Assert.Equal(SensitiveReason.None, Reason("-----BEGIN CERTIFICATE-----\nabc\n-----END CERTIFICATE-----"));
        }

        [Fact]
        public void Jwt_Blocked_AlsoInsideAuthorizationHeader()
        {
            const string jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";
            Assert.Equal(SensitiveReason.Jwt, Reason(jwt));
            Assert.Equal(SensitiveReason.Jwt, Reason("Authorization: Bearer " + jwt));
        }

        [Fact]
        public void Jwt_LookalikeWithoutThreeSegmentsAllowed()
        {
            Assert.Equal(SensitiveReason.None, Reason("eyJhbGciOiJIUzI1NiJ9"));
            Assert.Equal(SensitiveReason.None, Reason("see eyJ in the docs"));
        }

        [Theory]
        [InlineData("xK9#mP2$vL8@nQ4!wR7&tY3*")]
        [InlineData("Zm9vYmFyQmF6cXV4MTIzNDU2Nzg5MGFiY2RlZg9Q")]
        [InlineData("sk_li" + "ve_4eC39HqLyjWDarjtT1zdp7dcXyZ")]
        public void HighEntropyToken_Blocked(string text)
        {
            Assert.Equal(SensitiveReason.HighEntropyToken, Reason(text));
        }

        [Theory]
        [InlineData("The quick brown fox jumps over the lazy dog and keeps running")]  // sentence
        [InlineData("https://example.com/a/very/long/path?with=query&and=more&params=1234567890")]
        [InlineData("C:\\Users\\someone\\Documents\\ProjectFolder\\file-name-final-v2.docx")]
        [InlineData("a94a8fe5ccb19ba61c4c0873d391e987982fbbd3")]                        // git sha-1
        [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")] // sha-256
        [InlineData("ThisIsAVeryLongIdentifierName")]                                  // identifier
        [InlineData("some.long.dotted.namespace.ClassName")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaa")]                                   // low entropy
        [InlineData("short1A#")]
        [InlineData("someone.with.a.long.name@example-company.com")]
        [InlineData("{\"key\":\"value\",\"other\":[1,2,3,4,5,6,7,8,9,10]}")]
        public void HighEntropyToken_FalsePositivesAllowed(string text)
        {
            Assert.Equal(SensitiveReason.None, Reason(text));
        }

        [Fact]
        public void HighEntropy_CanBeDisabledOrTuned()
        {
            var off = new SensitivePolicy(new SensitivePolicyOptions { DetectHighEntropyTokens = false });
            Assert.Equal(SensitiveReason.None, Reason("xK9#mP2$vL8@nQ4!wR7&tY3*", null, default(ClipboardFormatFlags), off));
            var longer = new SensitivePolicy(new SensitivePolicyOptions { MinTokenLength = 40 });
            Assert.Equal(SensitiveReason.None, Reason("xK9#mP2$vL8@nQ4!wR7&tY3*", null, default(ClipboardFormatFlags), longer));
        }

        [Fact]
        public void DetectorsCanBeSwitchedOffIndividually()
        {
            var p = new SensitivePolicy(new SensitivePolicyOptions { DetectCreditCards = false, DetectAwsKeys = false, DetectJwt = false, DetectPrivateKeys = false });
            Assert.Equal(SensitiveReason.None, Reason("4111111111111111", null, default(ClipboardFormatFlags), p));
            Assert.Equal(SensitiveReason.None, Reason("AKIAIOSFODNN7EXAMPLE", null, default(ClipboardFormatFlags), p));
            Assert.Equal(SensitiveReason.None, Reason("-----BEGIN PRIVATE KEY-----", null, default(ClipboardFormatFlags), p));
        }

        [Theory]
        [InlineData("KeePass.exe", true)]
        [InlineData("keepassxc", true)]
        [InlineData("1Password", true)]
        [InlineData("notepad.exe", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ExcludedApps_GlobMatchesCaseInsensitivelyWithOrWithoutExe(string app, bool excluded)
        {
            var p = new SensitivePolicy(new SensitivePolicyOptions { ExcludedApps = new List<string> { "keepass*", "1password" } });
            Assert.Equal(excluded, p.IsExcludedApp(app));
            Assert.Equal(excluded ? SensitiveReason.ExcludedApp : SensitiveReason.None, p.Evaluate("hello", app).Reason);
        }

        [Fact]
        public void ExcludedApps_QuestionMarkAndRegexCharsAreLiteral()
        {
            var p = new SensitivePolicy(new SensitivePolicyOptions { ExcludedApps = new List<string> { "app?.v(1)" } });
            Assert.True(p.IsExcludedApp("appX.v(1)"));
            Assert.False(p.IsExcludedApp("appX.v1"));
        }

        [Fact]
        public void FormatFlags_EachBlocksByDefault()
        {
            Assert.Equal(SensitiveReason.FormatFlag, Reason("x", null, new ClipboardFormatFlags(true, false, false)));
            Assert.Equal(SensitiveReason.FormatFlag, Reason("x", null, new ClipboardFormatFlags(false, true, false)));
            Assert.Equal(SensitiveReason.FormatFlag, Reason("x", null, new ClipboardFormatFlags(false, false, true)));
            Assert.Equal(SensitiveReason.None, Reason("x", null, ClipboardFormatFlags.None));
        }

        [Fact]
        public void FormatFlags_CanBeIgnoredPerFlag()
        {
            var p = new SensitivePolicy(new SensitivePolicyOptions { HonorCloudUploadDisallowedFlag = false });
            Assert.Equal(SensitiveReason.None, Reason("x", null, new ClipboardFormatFlags(false, false, true), p));
            Assert.Equal(SensitiveReason.FormatFlag, Reason("x", null, new ClipboardFormatFlags(true, false, true), p));
        }

        [Fact]
        public void Evaluate_NullOrEmptyTextIsAllowed()
        {
            Assert.True(Default().Evaluate(null).Allowed);
            Assert.True(Default().Evaluate("").Allowed);
        }

        [Fact]
        public void Entropy_Basics()
        {
            Assert.Equal(0, SensitivePolicy.ShannonEntropy(""));
            Assert.Equal(0, SensitivePolicy.ShannonEntropy("aaaa"), 6);
            Assert.Equal(1, SensitivePolicy.ShannonEntropy("abab"), 6);
        }
    }

    public class KindTests
    {
        [Theory]
        [InlineData("https://example.com/a?b=1", ClipKind.Url)]
        [InlineData("http://localhost:8080", ClipKind.Url)]
        [InlineData("www.example.com", ClipKind.Url)]
        [InlineData("C:\\Windows\\System32\\notepad.exe", ClipKind.FilePath)]
        [InlineData("\\\\server\\share\\file.txt", ClipKind.FilePath)]
        [InlineData("/usr/local/bin/tool", ClipKind.FilePath)]
        [InlineData("#fff", ClipKind.Color)]
        [InlineData("#1A2b3C", ClipKind.Color)]
        [InlineData("rgb(10, 20, 30)", ClipKind.Color)]
        [InlineData("rgba(10,20,30,0.5)", ClipKind.Color)]
        [InlineData("someone@example.com", ClipKind.Email)]
        [InlineData("42", ClipKind.Number)]
        [InlineData("-3.14", ClipKind.Number)]
        [InlineData("1,234,567.89", ClipKind.Number)]
        [InlineData("1e10", ClipKind.Number)]
        [InlineData("{\"a\":1}", ClipKind.Json)]
        [InlineData("[1, 2, 3]", ClipKind.Json)]
        [InlineData("hello world", ClipKind.Plain)]
        [InlineData("#12", ClipKind.Plain)]
        [InlineData("not an email@", ClipKind.Plain)]
        [InlineData("{not json}", ClipKind.Plain)]
        [InlineData("", ClipKind.Plain)]
        [InlineData(null, ClipKind.Plain)]
        public void Detect(string text, ClipKind expected)
        {
            Assert.Equal(expected, ClipKindDetector.Detect(text));
        }

        [Fact]
        public void Detect_MultiLineCode()
        {
            Assert.Equal(ClipKind.Code, ClipKindDetector.Detect("int Main() {\n  return 0;\n}"));
            Assert.Equal(ClipKind.Code, ClipKindDetector.Detect("a = 1;\nb = 2;"));
        }

        [Fact]
        public void Detect_MultiLineProseIsPlain()
        {
            Assert.Equal(ClipKind.Plain, ClipKindDetector.Detect("line one\nline two\nline three"));
        }

        [Fact]
        public void Detect_UrlWithSpacesIsNotUrl()
        {
            Assert.Equal(ClipKind.Plain, ClipKindDetector.Detect("https://example.com and more words"));
        }

        [Fact]
        public void Detect_SurroundingWhitespaceIgnored()
        {
            Assert.Equal(ClipKind.Url, ClipKindDetector.Detect("  https://example.com\n"));
        }

        [Fact]
        public void TryParseNumber_HandlesCommas()
        {
            Assert.True(ClipKindDetector.TryParseNumber("1,234.5", out double v));
            Assert.Equal(1234.5, v);
            Assert.False(ClipKindDetector.TryParseNumber("abc", out _));
            Assert.False(ClipKindDetector.TryParseNumber(null, out _));
        }

        [Fact]
        public void Preview_FlattensAndTruncatesWithoutSplittingSurrogates()
        {
            var e = new ClipboardEntry(1, "ab\r\ncd\t\U0001F600\U0001F600\U0001F600", ClipKind.Plain, System.DateTimeOffset.UtcNow, false, null);
            Assert.Equal("ab cd \U0001F600\U0001F600\U0001F600", e.Preview(100));
            string cut = e.Preview(7); // "ab cd \u{1F600}" would end mid-pair at 7
            Assert.EndsWith("\u2026", cut);
            Assert.Equal("ab cd ", cut.Substring(0, cut.Length - 1));
        }

        [Fact]
        public void Entry_ByteSizeIsUtf8()
        {
            var e = new ClipboardEntry(1, "\u20AC", ClipKind.Plain, System.DateTimeOffset.UtcNow, false, "  ");
            Assert.Equal(3, e.ByteSize);
            Assert.Null(e.SourceApp);
        }
    }
}
