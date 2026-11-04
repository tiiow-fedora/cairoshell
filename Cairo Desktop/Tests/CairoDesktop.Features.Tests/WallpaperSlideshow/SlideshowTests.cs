using System;
using System.Collections.Generic;
using System.Linq;
using CairoDesktop.Features.WallpaperSlideshow;
using Xunit;

namespace CairoDesktop.Features.Tests.WallpaperSlideshow
{
    public class SlideshowPlannerTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly string[] Files = { @"C:\w\b.jpg", @"C:\w\a.png", @"C:\w\c.bmp", @"C:\w\notes.txt", @"C:\w\.hidden.jpg", @"C:\w\~tmp.png", @"C:\w\d.JPEG" };

        [Fact]
        public void Filter_accepts_pictures_only()
        {
            Assert.True(ImageFilter.IsWallpaper(@"C:\x\a.JPG"));
            Assert.True(ImageFilter.IsWallpaper("/x/a.jpeg"));
            Assert.True(ImageFilter.IsWallpaper("a.bmp"));
            Assert.False(ImageFilter.IsWallpaper(@"C:\x\a.gif"));
            Assert.False(ImageFilter.IsWallpaper(@"C:\x\a.webp"));
            Assert.False(ImageFilter.IsWallpaper(@"C:\x\Thumbs.db"));
            Assert.False(ImageFilter.IsWallpaper(@"C:\x\.a.png"));
            Assert.False(ImageFilter.IsWallpaper(@"C:\x\~a.png"));
            Assert.False(ImageFilter.IsWallpaper(@"C:\x\folder.png\"));
            Assert.False(ImageFilter.IsWallpaper("noextension"));
            Assert.False(ImageFilter.IsWallpaper(null));
        }

        [Fact]
        public void Sequential_walks_name_order_and_wraps()
        {
            var p = new SlideshowPlanner();
            var seen = Enumerable.Range(0, 5).Select(_ => p.Next(Files, false, T0)).ToList();
            Assert.Equal(new[] { @"C:\w\a.png", @"C:\w\b.jpg", @"C:\w\c.bmp", @"C:\w\d.JPEG", @"C:\w\a.png" }, seen);
        }

        [Fact]
        public void Sequential_survives_the_current_file_being_deleted()
        {
            var p = new SlideshowPlanner();
            p.Next(Files, false, T0);                       // a
            p.Next(Files, false, T0);                       // b
            var withoutB = Files.Where(f => !f.EndsWith("b.jpg")).ToArray();
            Assert.Equal(@"C:\w\c.bmp", p.Next(withoutB, false, T0));
        }

        [Fact]
        public void Shuffle_shows_every_file_once_per_round()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var p = new SlideshowPlanner(new Random(seed));
                var round = Enumerable.Range(0, 4).Select(_ => p.Next(Files, true, T0)).ToList();
                Assert.Equal(4, round.Distinct().Count());
            }
        }

        [Fact]
        public void Shuffle_never_repeats_straight_across_a_round_boundary()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var p = new SlideshowPlanner(new Random(seed));
                string previous = null;
                for (int i = 0; i < 20; i++)
                {
                    string next = p.Next(Files, true, T0);
                    Assert.NotEqual(previous, next);
                    previous = next;
                }
            }
        }

        [Fact]
        public void Shuffle_with_one_file_repeats_it()
        {
            var p = new SlideshowPlanner(new Random(1));
            Assert.Equal("only.png", p.Next(new[] { "only.png" }, true, T0));
            Assert.Equal("only.png", p.Next(new[] { "only.png" }, true, T0));
        }

        [Fact]
        public void New_files_join_the_current_round_and_deleted_ones_leave()
        {
            var p = new SlideshowPlanner(new Random(3));
            var two = new[] { "a.png", "b.png" };
            string first = p.Next(two, true, T0);
            var picked = new HashSet<string> { first };
            var three = new[] { "a.png", "b.png", "c.png" };
            picked.Add(p.Next(three, true, T0));
            picked.Add(p.Next(three, true, T0));
            Assert.Equal(3, picked.Count);                      // no repeats, and c was reachable in the same round
            Assert.Equal("b.png", p.Next(new[] { "b.png" }, true, T0)); // everything else vanished
        }

        [Fact]
        public void No_candidates_gives_null_and_keeps_state()
        {
            var p = new SlideshowPlanner();
            p.Next(Files, false, T0);
            string before = p.State.Last;
            Assert.Null(p.Next(new[] { "x.txt" }, false, T0.AddHours(1)));
            Assert.Null(p.Next(null, true, T0));
            Assert.Equal(before, p.State.Last);
            Assert.Equal(T0, p.State.LastChange);
        }

        [Fact]
        public void Case_differences_are_the_same_file()
        {
            var p = new SlideshowPlanner(new Random(0));
            Assert.Equal("A.png", p.Next(new[] { "A.png", "a.PNG" }, true, T0));
            Assert.Equal("A.png", p.Next(new[] { "A.png", "a.PNG" }, true, T0));
        }

        [Fact]
        public void Interval_countdown_resumes_after_a_restart()
        {
            var p = new SlideshowPlanner();
            TimeSpan interval = TimeSpan.FromMinutes(30);
            Assert.Equal(TimeSpan.Zero, p.UntilNext(T0, interval));          // nothing shown yet
            p.Next(Files, false, T0);
            Assert.Equal(TimeSpan.FromMinutes(20), p.UntilNext(T0.AddMinutes(10), interval));
            Assert.Equal(TimeSpan.Zero, p.UntilNext(T0.AddHours(3), interval));
            Assert.Equal(interval, p.UntilNext(T0.AddDays(-3), interval));   // clock went backwards
        }

        [Fact]
        public void State_round_trips_and_damaged_state_is_ignored()
        {
            var p = new SlideshowPlanner(new Random(2));
            p.Next(Files, true, T0);
            p.Next(Files, true, T0.AddMinutes(30));
            SlideshowState loaded = SlideshowPlanner.Deserialize(p.Serialize());
            Assert.Equal(p.State.Last, loaded.Last);
            Assert.Equal(p.State.Played, loaded.Played);
            Assert.Equal(p.State.LastChange, loaded.LastChange);

            foreach (string bad in new[] { null, "", "{", "[]", "{\"played\":5,\"last\":3,\"lastChange\":\"yesterday\"}" })
            {
                SlideshowState s = SlideshowPlanner.Deserialize(bad);
                Assert.NotNull(s);
                Assert.NotNull(s.Played);
                Assert.Null(s.LastChange);
            }
        }

        [Fact]
        public void A_restored_state_continues_the_same_round()
        {
            var first = new SlideshowPlanner(new Random(5));
            string a = first.Next(Files, true, T0);
            var second = new SlideshowPlanner(new Random(9), SlideshowPlanner.Deserialize(first.Serialize()));
            var rest = Enumerable.Range(0, 3).Select(_ => second.Next(Files, true, T0)).ToList();
            Assert.Equal(4, rest.Concat(new[] { a }).Distinct().Count());
        }
    }

    public class SlideshowConfigTests
    {
        [Fact]
        public void Missing_section_is_off()
        {
            var r = SlideshowConfigParser.ParseDocument("{ }");
            Assert.False(r.Config.Enabled);
            Assert.Empty(r.Findings);
        }

        [Fact]
        public void Full_section()
        {
            var r = SlideshowConfigParser.ParseDocument(@"{ // c
              ""wallpaperSlideshow"": { ""enabled"": true, ""folder"": ""%USERPROFILE%\\Pictures\\Walls"", ""intervalMinutes"": 15,
                ""shuffle"": false, ""includeSubfolders"": true, ""style"": ""FIT"", ""changeOnStart"": true, }, }");
            Assert.True(r.Config.Enabled);
            Assert.Equal(@"%USERPROFILE%\Pictures\Walls", r.Config.Folder);
            Assert.Equal(15, r.Config.IntervalMinutes);
            Assert.False(r.Config.Shuffle);
            Assert.True(r.Config.IncludeSubfolders);
            Assert.Equal("fit", r.Config.Style);
            Assert.True(r.Config.ChangeOnStart);
            Assert.Empty(r.Findings);
        }

        [Fact]
        public void Enabled_without_a_folder_turns_itself_off()
        {
            var r = SlideshowConfigParser.ParseDocument("{ \"wallpaperSlideshow\": { \"enabled\": true } }");
            Assert.False(r.Config.Enabled);
            Assert.Contains(r.Findings, x => x.IsError && x.Path.EndsWith("folder"));
        }

        [Theory]
        [InlineData("0", 1)]
        [InlineData("99999", 1440)]
        [InlineData("2.6", 3)]
        public void Interval_is_clamped(string value, int expected)
        {
            var r = SlideshowConfigParser.ParseDocument("{ \"wallpaperSlideshow\": { \"folder\": \"x\", \"intervalMinutes\": " + value + " } }");
            Assert.Equal(expected, r.Config.IntervalMinutes);
        }

        [Fact]
        public void Bad_values_fall_back_and_never_throw()
        {
            var r = SlideshowConfigParser.ParseDocument("{ \"wallpaperSlideshow\": { \"folder\": 5, \"intervalMinutes\": \"soon\", \"style\": \"zoom\", \"shuffle\": \"yes\", \"bogus\": 1 } }");
            Assert.Equal(30, r.Config.IntervalMinutes);
            Assert.Equal("fill", r.Config.Style);
            Assert.True(r.Config.Shuffle);
            Assert.Equal(4, r.Findings.Count(x => x.IsError));
            Assert.Contains(r.Findings, x => !x.IsError);
            Assert.Contains(SlideshowConfigParser.ParseDocument("{ \"wallpaperSlideshow\": [] }").Findings, x => x.IsError);
            Assert.Single(SlideshowConfigParser.ParseDocument("{ nope").Findings);
            Assert.Empty(SlideshowConfigParser.ParseDocument(null).Findings);
        }
    }
}
