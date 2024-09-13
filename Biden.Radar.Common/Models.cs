namespace Biden.Radar.Common
{
    public class TickData
    {
        public long Timestamp { get; set; }
        public decimal Price { get; set; }
        public decimal Amount { get; set; }
    }

    public class Candle
    {
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public bool Confirmed { get; set; }
        public CandleType CandleType { get; set; }
    }

    public enum CandleType
    {
        Spot = 0,
        Margin = 1,
        Perp = 2
    }
}
