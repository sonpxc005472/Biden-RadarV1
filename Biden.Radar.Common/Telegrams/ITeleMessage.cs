namespace Biden.Radar.Common.Telegrams
{
    public interface ITeleMessage
    {
        Task SendMessage(string message);
    }
}
