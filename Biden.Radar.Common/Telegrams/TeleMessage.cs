using Microsoft.Extensions.Configuration;
using Telegram.Bot;

namespace Biden.Radar.Common.Telegrams
{
    public class TeleMessage : ITeleMessage
    {
        private readonly TelegramBotClient botClient;
        private readonly IConfiguration _configuration;
        public TeleMessage(IConfiguration configuration) 
        {
            _configuration = configuration;
            var tele = configuration.GetOptions<TelegramBot>("Telegram");
            botClient = new TelegramBotClient(tele.Token);
        }
        public async Task SendMessage(string message)
        {
            try
            {
                var tele = _configuration.GetOptions<TelegramBot>("Telegram");                
                var sentMessage = await botClient.SendTextMessageAsync(tele.ChannelSend, message, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html);
            }
            catch { 
                // ignore
            }            
        }        
    }
}
