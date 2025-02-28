namespace TradingSystem.Logic;

public class TopicGenerator
{
    public static string TopicForClientInstrumentPrice(string instrument)
    {
        return "clientPrice" + instrument;
    }
}