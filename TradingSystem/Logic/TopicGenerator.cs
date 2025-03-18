namespace TradingSystem.Logic;

public class TopicGenerator
{
    public static string TopicForClientInstrumentPrice(string instrument)
    {
        return "clientPrice" + instrument;
    }

    public static string TopicForMarketInstrumentPrice(string instrument)
    {
        return "marketPrice" + instrument;
    }

    public static string TopicForClientBuyOrder()
    {
        return "buyOrder";
    }

    public static string TopicForClientBuyOrderApproved()
    {
        return "buyOrderApproved";
    }

    public static string TopicForClientOrderEnded(string clientId)
    {
        return "buyOrderEnded" + clientId;
    }
    
    public static string TopicForLoginRequest()
    {
        return "loginRequested";
    }

    public static string TopicForLoginResponse()
    {
        return "loginResponded";
    }

    public static string TopicForAllClients()
    {
        return "allClients";
    }

    public static string TopicForAllInstruments()
    {
        return "allInstruments";
    }

    public static string TopicForBookingOrder()
    {
        return "BookOrder";
    }

    public static string TopicForHoldingOfClient(string clientId)
    {
        return "holdingOfClient" + clientId;
    }

    public static string TopicForDBDataOfClient(string clientId)
    {
        return "dbDataOfClient" + clientId;
    }
    
}