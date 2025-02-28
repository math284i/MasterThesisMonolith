namespace TradingSystem.Logic;

public class TopicGenerator
{
    public static string TopicForClientInstrumentPrice(string instrumnet)
    {
        return "clientPrice" + instrumnet;
    }
}