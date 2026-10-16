using System;
using Unruled.Core.Board;

namespace Unruled.Core.SystemFactions
{
    /// <summary>A storm front NOAA forecasts, and the tick it will form.</summary>
    public struct ForecastFront
    {
        public long StartTick;
        public StormFront Front;
    }

    /// <summary>
    /// NOAA storm warnings by bracket (tab 05 NOAA_Storm_Warning_Lead). A player sees the fronts that will form within
    /// their bracket's lead; "Current Storm Path" brackets get no lead, and unrated players get no NOAA service at all.
    /// The forecast is exact: it reads the same seeded rolls the weather engine will make.
    /// </summary>
    public sealed class NoaaForecast
    {
        private readonly NoaaWeatherEngine _weather;
        private readonly BracketRates _rates;

        public NoaaForecast(NoaaWeatherEngine weather, BracketRates rates)
        {
            _weather = weather ?? throw new ArgumentNullException(nameof(weather));
            _rates = rates ?? throw new ArgumentNullException(nameof(rates));
        }

        public bool SeesCurrentStorms(int playerId) => _rates.For(playerId).SeesCurrentStorms;

        public int LeadTicks(int playerId) => _rates.For(playerId).NoaaLeadTicks;

        /// <summary>
        /// Writes the fronts forming after <paramref name="tick"/> and within the player's lead into
        /// <paramref name="into"/>, in tick then star-system order; returns how many were written (at most its length).
        /// </summary>
        public int Forecast(int playerId, long tick, ForecastFront[] into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            int lead = LeadTicks(playerId);
            int interval = _weather.RollIntervalTicks;
            int maxCluster = _weather.MaxClusterId();
            int count = 0;
            for (long t = (tick / interval + 1) * interval; t <= tick + lead && count < into.Length; t += interval)
            {
                for (int cluster = 0; cluster <= maxCluster && count < into.Length; cluster++)
                {
                    if (_weather.TryRollFront(t, cluster, out var front))
                        into[count++] = new ForecastFront { StartTick = t, Front = front };
                }
            }
            return count;
        }
    }
}
