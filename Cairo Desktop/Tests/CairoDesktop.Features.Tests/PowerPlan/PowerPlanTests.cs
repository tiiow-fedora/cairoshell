using System.Linq;
using CairoDesktop.Features.PowerPlan;
using Xunit;

namespace CairoDesktop.Features.Tests.PowerPlan
{
    public class PowerPlanTests
    {
        private const string Sample =
            "Existing Power Schemes (* Active)\r\n-----------------------------------\r\n" +
            "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *\r\n" +
            "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)\r\n" +
            "Power Scheme GUID: a1841308-3541-4fab-bc81-f71556f20b4a  (Power saver)\r\n";

        [Fact]
        public void Parses_guid_name_and_active()
        {
            var plans = PowerPlanParser.Parse(Sample);
            Assert.Equal(3, plans.Count);
            Assert.Equal("Balanced", plans[0].Name);
            Assert.True(plans[0].IsActive);
            Assert.False(plans[1].IsActive);
            Assert.Equal("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", plans[1].Guid);
        }

        [Fact]
        public void Parses_localised_output()
        {
            var plans = PowerPlanParser.Parse("GUID de l'modo de energia : 381B4222-F694-41F0-9685-FF5BB260DF2E  (Equilibrado) *\n");
            Assert.Single(plans);
            Assert.Equal("Equilibrado", plans[0].Name);
            Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", plans[0].Guid);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("nothing useful here")]
        public void Junk_gives_no_plans(string text) => Assert.Empty(PowerPlanParser.Parse(text));

        [Fact]
        public void Next_cycles_and_wraps()
        {
            var model = new PowerPlanModel(new PowerPlanOptions());
            var plans = PowerPlanParser.Parse(Sample);
            Assert.Equal("High performance", model.Next(plans).Name);
            var last = PowerPlanParser.Parse(Sample.Replace("(Balanced) *", "(Balanced)").Replace("(Power saver)", "(Power saver) *"));
            Assert.Equal("Balanced", model.Next(last).Name);
        }

        [Fact]
        public void Cycle_option_restricts_by_name_or_guid()
        {
            var model = new PowerPlanModel(new PowerPlanOptions { Cycle = new[] { "power saver", "381b4222-f694-41f0-9685-ff5bb260df2e" } });
            var plans = PowerPlanParser.Parse(Sample);
            Assert.Equal(new[] { "Power saver", "Balanced" }, model.Candidates(plans).Select(p => p.Name).ToArray());
            Assert.Equal("Power saver", model.Next(plans).Name);
        }

        [Fact]
        public void Unknown_cycle_names_fall_back_to_all()
        {
            var model = new PowerPlanModel(new PowerPlanOptions { Cycle = new[] { "nope" } });
            Assert.Equal(3, model.Candidates(PowerPlanParser.Parse(Sample)).Count);
        }

        [Fact]
        public void Active_plan_outside_cycle_starts_at_first()
        {
            var model = new PowerPlanModel(new PowerPlanOptions { Cycle = new[] { "High performance", "Power saver" } });
            Assert.Equal("High performance", model.Next(PowerPlanParser.Parse(Sample)).Name);
        }

        [Fact]
        public void No_plans_is_handled()
        {
            var model = new PowerPlanModel(new PowerPlanOptions());
            Assert.Null(model.Next(PowerPlanParser.Parse("")));
            Assert.Equal("Power: ?", model.FormatText(PowerPlanParser.Parse("")));
            Assert.Contains("No power plans", model.FormatToolTip(null));
        }

        [Fact]
        public void Text_and_tooltip()
        {
            var model = new PowerPlanModel(new PowerPlanOptions());
            var plans = PowerPlanParser.Parse(Sample);
            Assert.Equal("Power: Balanced", model.FormatText(plans));
            Assert.Contains("* Balanced", model.FormatToolTip(plans));
            Assert.Contains("  Power saver", model.FormatToolTip(plans));
        }
    }
}
