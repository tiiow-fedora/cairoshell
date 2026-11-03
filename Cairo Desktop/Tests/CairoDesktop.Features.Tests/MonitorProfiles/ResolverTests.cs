using System.Collections.Generic;
using CairoDesktop.Features.MonitorProfiles;
using Xunit;

namespace CairoDesktop.Features.Tests.MonitorProfiles
{
    public class ResolverTests
    {
        private static ProfileRule Rule(string name, MonitorSelector sel, ProfileSettings s) => new ProfileRule { Name = name, Match = sel, Settings = s };

        [Fact]
        public void NoConfig_GivesBuiltInDefaults()
        {
            var mons = Mon.Set(Mon.Fhd());
            var r = ProfileResolver.Resolve(mons, new ProfilesConfig())[@"\\.\DISPLAY1"];
            Assert.Equal(TaskbarEdge.Bottom, r.Settings.Position);
            Assert.Equal(30, r.Settings.Height);
            Assert.Equal(ProfileResolver.BuiltInSource, r.Sources["height"]);
            Assert.Empty(r.MatchedRules);
        }

        [Fact]
        public void NullConfigAndNullMonitors_DoNotThrow()
        {
            Assert.Single(ProfileResolver.Resolve(Mon.Set(Mon.Fhd()), null));
            Assert.Empty(ProfileResolver.Resolve(null, new ProfilesConfig()));
        }

        [Fact]
        public void DefaultProfile_OverridesBuiltIn_AndUnsetFallsThrough()
        {
            var cfg = new ProfilesConfig { Default = new ProfileSettings { Height = 44 } };
            var r = ProfileResolver.Resolve(Mon.Set(Mon.Fhd()), cfg)[@"\\.\DISPLAY1"];
            Assert.Equal(44, r.Settings.Height);
            Assert.Equal("default", r.Sources["height"]);
            Assert.Equal(TaskbarEdge.Bottom, r.Settings.Position);
            Assert.Equal("built-in", r.Sources["position"]);
        }

        [Fact]
        public void MoreSpecificRule_WinsOverLessSpecific_RegardlessOfFileOrder()
        {
            var cfg = new ProfilesConfig();
            cfg.Rules.Add(Rule("exact", new MonitorSelector { DeviceName = "DISPLAY2" }, new ProfileSettings { Height = 50 }));
            cfg.Rules.Add(Rule("all-secondary", new MonitorSelector { Primary = false }, new ProfileSettings { Height = 20 }));
            var r = ProfileResolver.Resolve(Mon.Set(Mon.Fhd(), Mon.Ultra()), cfg)[@"\\.\DISPLAY2"];
            Assert.Equal(50, r.Settings.Height);
            Assert.Equal("rule 'exact'", r.Sources["height"]);
            Assert.Equal(new[] { "all-secondary", "exact" }, r.MatchedRules);
        }

        [Fact]
        public void EqualSpecificity_LaterRuleInFileWins()
        {
            var cfg = new ProfilesConfig();
            cfg.Rules.Add(Rule("first", new MonitorSelector { Primary = true }, new ProfileSettings { Height = 20 }));
            cfg.Rules.Add(Rule("second", new MonitorSelector { Primary = true }, new ProfileSettings { Height = 21 }));
            var r = ProfileResolver.Resolve(Mon.Set(Mon.Fhd()), cfg)[@"\\.\DISPLAY1"];
            Assert.Equal(21, r.Settings.Height);
            Assert.Equal("rule 'second'", r.Sources["height"]);
        }

        [Fact]
        public void PerProperty_LayeringMixesSources()
        {
            var cfg = new ProfilesConfig { Default = new ProfileSettings { ShowClock = false } };
            cfg.Rules.Add(Rule("sec", new MonitorSelector { Primary = false }, new ProfileSettings { Position = TaskbarEdge.Top, Height = 24 }));
            cfg.Rules.Add(Rule("ultra", new MonitorSelector { Primary = false, MinAspectRatio = 2.1 }, new ProfileSettings { Height = 36, Floating = true }));
            var r = ProfileResolver.Resolve(Mon.Set(Mon.Fhd(), Mon.Ultra()), cfg)[@"\\.\DISPLAY2"];
            Assert.Equal(TaskbarEdge.Top, r.Settings.Position);
            Assert.Equal(36, r.Settings.Height);
            Assert.Equal(true, r.Settings.Floating);
            Assert.Equal(false, r.Settings.ShowClock);
            Assert.Equal("rule 'sec'", r.Sources["position"]);
            Assert.Equal("rule 'ultra'", r.Sources["height"]);
            Assert.Equal("default", r.Sources["showClock"]);
        }

        [Fact]
        public void NonMatchingRule_IsIgnored()
        {
            var cfg = new ProfilesConfig();
            cfg.Rules.Add(Rule("portrait", new MonitorSelector { Orientation = MonitorOrientation.Portrait }, new ProfileSettings { Position = TaskbarEdge.Left }));
            var r = ProfileResolver.Resolve(Mon.Set(Mon.Fhd()), cfg)[@"\\.\DISPLAY1"];
            Assert.Equal(TaskbarEdge.Bottom, r.Settings.Position);
        }

        [Fact]
        public void UnnamedRule_GetsPositionalName()
        {
            var cfg = new ProfilesConfig();
            cfg.Rules.Add(Rule(null, new MonitorSelector(), new ProfileSettings { Height = 33 }));
            var r = ProfileResolver.Resolve(Mon.Set(Mon.Fhd()), cfg)[@"\\.\DISPLAY1"];
            Assert.Equal("rule 'rule #1'", r.Sources["height"]);
        }

        [Fact]
        public void LayoutName_UnsetStaysNull_AndSetWins()
        {
            var cfg = new ProfilesConfig();
            var r0 = ProfileResolver.Resolve(Mon.Set(Mon.Fhd()), cfg)[@"\\.\DISPLAY1"];
            Assert.Null(r0.Settings.LayoutName);
            Assert.Equal("built-in (unset)", r0.Sources["layoutName"]);
            cfg.Rules.Add(Rule("l", new MonitorSelector { Primary = true }, new ProfileSettings { LayoutName = "compact" }));
            var r1 = ProfileResolver.Resolve(Mon.Set(Mon.Fhd()), cfg)[@"\\.\DISPLAY1"];
            Assert.Equal("compact", r1.Settings.LayoutName);
        }

        [Fact]
        public void BooleanFalse_CountsAsSet_AndOverridesTrue()
        {
            var cfg = new ProfilesConfig();
            cfg.Rules.Add(Rule("noclock", new MonitorSelector { Primary = true }, new ProfileSettings { ShowClock = false }));
            var r = ProfileResolver.Resolve(Mon.Set(Mon.Fhd()), cfg)[@"\\.\DISPLAY1"];
            Assert.Equal(false, r.Settings.ShowClock);
        }

        [Fact]
        public void DuplicateDeviceNames_FirstWins()
        {
            var a = Mon.Fhd();
            var b = Mon.Make(@"\\.\DISPLAY1", 5000, 0, 800, 600);
            var map = ProfileResolver.Resolve(new List<MonitorInfo> { a, b }, new ProfilesConfig());
            Assert.Single(map);
            Assert.Same(a, map[@"\\.\DISPLAY1"].Monitor);
        }

        [Fact]
        public void ResultDictionary_IsCaseInsensitive()
        {
            var map = ProfileResolver.Resolve(Mon.Set(Mon.Fhd()), new ProfilesConfig());
            Assert.True(map.ContainsKey(@"\\.\display1"));
        }

        [Fact]
        public void Resolution_DoesNotMutateConfig()
        {
            var cfg = new ProfilesConfig { Default = new ProfileSettings { Height = 44 } };
            ProfileResolver.Resolve(Mon.Set(Mon.Fhd()), cfg);
            Assert.Null(cfg.Default.Position);
            Assert.Equal(44, cfg.Default.Height);
        }

        [Fact]
        public void Explain_ShowsSourcesAndValues()
        {
            var cfg = new ProfilesConfig();
            cfg.Rules.Add(Rule("wide", new MonitorSelector { MinAspectRatio = 2.1 }, new ProfileSettings { Height = 40 }));
            string text = ProfileResolver.ExplainAll(Mon.Set(Mon.Fhd(), Mon.Ultra()), cfg);
            Assert.Contains(@"\\.\DISPLAY2", text);
            Assert.Contains("rules applied: wide", text);
            Assert.Contains("rules applied: (none)", text);
            Assert.Contains("<- rule 'wide'", text);
            Assert.Contains("<- built-in", text);
            Assert.Contains("height", text);
        }

        [Fact]
        public void ChangedMonitors_DetectsSettingChangesAddsAndRemoves()
        {
            var cfg = new ProfilesConfig();
            cfg.Rules.Add(Rule("sec", new MonitorSelector { Primary = false }, new ProfileSettings { Height = 20 }));
            var before = ProfileResolver.Resolve(Mon.Set(Mon.Fhd(), Mon.Ultra()), cfg);

            var p = Mon.Fhd();
            var u = Mon.Ultra();
            var third = Mon.Portrait();
            var after = ProfileResolver.Resolve(Mon.Set(p, u, third), cfg);
            var changed = ProfileResolver.ChangedMonitors(before, after);
            Assert.Equal(new[] { @"\\.\DISPLAY3" }, changed);

            var cfg2 = new ProfilesConfig();
            var after2 = ProfileResolver.Resolve(Mon.Set(Mon.Fhd(), Mon.Ultra()), cfg2);
            var changed2 = ProfileResolver.ChangedMonitors(before, after2);
            Assert.Equal(new[] { @"\\.\DISPLAY2" }, changed2);

            var only = ProfileResolver.Resolve(Mon.Set(Mon.Fhd()), cfg);
            Assert.Contains(@"\\.\DISPLAY2", ProfileResolver.ChangedMonitors(before, only));
        }

        [Fact]
        public void PrimarySwap_ChangesWhichMonitorGetsPrimaryRule()
        {
            var cfg = new ProfilesConfig();
            cfg.Rules.Add(Rule("main", new MonitorSelector { Primary = true }, new ProfileSettings { Height = 50 }));
            var a = Mon.Make(@"\\.\DISPLAY1", 0, 0, 1920, 1080, true);
            var b = Mon.Make(@"\\.\DISPLAY2", 1920, 0, 1920, 1080);
            var r1 = ProfileResolver.Resolve(Mon.Set(a, b), cfg);
            Assert.Equal(50, r1[@"\\.\DISPLAY1"].Settings.Height);
            Assert.Equal(30, r1[@"\\.\DISPLAY2"].Settings.Height);

            var a2 = Mon.Make(@"\\.\DISPLAY1", -1920, 0, 1920, 1080);
            var b2 = Mon.Make(@"\\.\DISPLAY2", 0, 0, 1920, 1080, true);
            var r2 = ProfileResolver.Resolve(Mon.Set(a2, b2), cfg);
            Assert.Equal(30, r2[@"\\.\DISPLAY1"].Settings.Height);
            Assert.Equal(50, r2[@"\\.\DISPLAY2"].Settings.Height);
        }

        [Fact]
        public void Settings_GetSetRoundTrip_AllProperties()
        {
            var s = ProfileSettings.BuiltInDefaults();
            var copy = new ProfileSettings();
            foreach (string n in ProfileSettings.PropertyNames) copy.Set(n, s.Get(n));
            Assert.True(s.ValueEquals(copy));
            Assert.Throws<System.ArgumentException>(() => s.Get("nope"));
        }
    }
}
