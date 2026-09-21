using System;

namespace Walkabout.Data
{
    /// <summary>
    /// Money is stored as an INTEGER count of ten-thousandths on every engine.
    ///
    /// STRICT tables (spec section 1.7.1, adopted) allow only INT/INTEGER/REAL/TEXT/BLOB/ANY, so
    /// the "money" column type today's reflection-to-DDL emits is illegal. REAL loses exactness.
    /// TEXT is exact but not SQL-summable, which would kill section 3.3 stage 1's whole point -
    /// filtering and subtotaling happening in SQL rather than in memory.
    ///
    /// INTEGER ten-thousandths is exact, summable, and is LITERALLY SQL Server's money
    /// representation: money is an int64 of ten-thousandths whose maximum,
    /// 922,337,203,685,477.5807, is Int64.MaxValue / 10000. So adopting this on SQLite converges
    /// the two engines rather than diverging them - spec section 6.8 rule 1.
    /// </summary>
    public static class MoneyScale
    {
        public const int Scale = 4;

        private const decimal Factor = 10000m;
        private const decimal MaxValue = 922337203685477.5807m;
        private const decimal MinValue = -922337203685477.5808m;

        public static long ToStorage(decimal value)
        {
            if (value < MinValue || value > MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"Money values must be between {MinValue} and {MaxValue} - the range SQL Server's "
                    + "money type can hold, which this storage encoding matches exactly.");
            }

            decimal scaled = value * Factor;
            if (decimal.Truncate(scaled) != scaled)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"Money values carry at most {Scale} decimal places. Silent rounding here is how "
                    + "a fraction of a cent goes missing with nobody able to say where.");
            }

            return (long)scaled;
        }

        public static decimal FromStorage(long stored) => stored / Factor;
    }
}
