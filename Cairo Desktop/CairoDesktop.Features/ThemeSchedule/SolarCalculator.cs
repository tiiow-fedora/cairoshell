using System;

namespace CairoDesktop.Features.ThemeSchedule
{
    public enum SunCondition { Normal, PolarDay, PolarNight }

    /// <summary>Sunrise and sunset for one UTC calendar day at a place. Instants are real UTC moments (they can fall on the neighbouring UTC day).</summary>
    public struct SunTimes
    {
        public SunCondition Condition;
        public DateTime SunriseUtc;
        public DateTime SunsetUtc;
    }

    /// <summary>
    /// NOAA solar position approximation (accurate to about a minute between 60 degrees north and south, a few minutes near
    /// the poles). Pure maths; no clock, no time zones.
    /// </summary>
    public static class SolarCalculator
    {
        private const double Deg = Math.PI / 180.0;

        /// <param name="utcDate">Only Year/Month/Day are used.</param>
        /// <param name="latitude">Degrees, north positive, -90..90.</param>
        /// <param name="longitude">Degrees, east positive, -180..180.</param>
        public static SunTimes Calculate(DateTime utcDate, double latitude, double longitude)
        {
            var midnight = new DateTime(utcDate.Year, utcDate.Month, utcDate.Day, 0, 0, 0, DateTimeKind.Utc);
            double jd = JulianDay(midnight.AddHours(12));
            double t = (jd - 2451545.0) / 36525.0;

            double l0 = Mod(280.46646 + t * (36000.76983 + t * 0.0003032), 360.0);
            double m = 357.52911 + t * (35999.05029 - 0.0001537 * t);
            double e = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
            double mRad = m * Deg;
            double c = Math.Sin(mRad) * (1.914602 - t * (0.004817 + 0.000014 * t))
                       + Math.Sin(2 * mRad) * (0.019993 - 0.000101 * t)
                       + Math.Sin(3 * mRad) * 0.000289;
            double trueLong = l0 + c;
            double omega = 125.04 - 1934.136 * t;
            double lambda = trueLong - 0.00569 - 0.00478 * Math.Sin(omega * Deg);
            double eps0 = 23.0 + (26.0 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60.0) / 60.0;
            double eps = eps0 + 0.00256 * Math.Cos(omega * Deg);
            double decl = Math.Asin(Math.Sin(eps * Deg) * Math.Sin(lambda * Deg));

            double y = Math.Tan(eps * Deg / 2.0);
            y *= y;
            double l0Rad = l0 * Deg;
            double eqTime = 4.0 / Deg * (y * Math.Sin(2 * l0Rad) - 2 * e * Math.Sin(mRad)
                                         + 4 * e * y * Math.Sin(mRad) * Math.Cos(2 * l0Rad)
                                         - 0.5 * y * y * Math.Sin(4 * l0Rad) - 1.25 * e * e * Math.Sin(2 * mRad));

            double latRad = latitude * Deg;
            double cosHa = Math.Cos(90.833 * Deg) / (Math.Cos(latRad) * Math.Cos(decl)) - Math.Tan(latRad) * Math.Tan(decl);
            double solarNoonMinutes = 720.0 - 4.0 * longitude - eqTime;

            if (double.IsNaN(cosHa) || cosHa > 1.0) return new SunTimes { Condition = SunCondition.PolarNight };
            if (cosHa < -1.0) return new SunTimes { Condition = SunCondition.PolarDay };

            double haMinutes = 4.0 * Math.Acos(cosHa) / Deg;
            return new SunTimes
            {
                Condition = SunCondition.Normal,
                SunriseUtc = midnight.AddMinutes(solarNoonMinutes - haMinutes),
                SunsetUtc = midnight.AddMinutes(solarNoonMinutes + haMinutes),
            };
        }

        private static double JulianDay(DateTime utc)
        {
            int y = utc.Year, mo = utc.Month;
            if (mo <= 2) { y--; mo += 12; }
            int a = y / 100;
            int b = 2 - a + a / 4;
            return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (mo + 1)) + utc.Day + b - 1524.5
                   + (utc.Hour + utc.Minute / 60.0 + utc.Second / 3600.0) / 24.0;
        }

        private static double Mod(double x, double m)
        {
            double r = x % m;
            return r < 0 ? r + m : r;
        }
    }
}
