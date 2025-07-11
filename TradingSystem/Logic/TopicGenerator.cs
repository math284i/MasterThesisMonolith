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

    public static string TopicForClientOrder()
    {
        return "order";
    }

    public static string TopicForClientOrderApproved()
    {
        return "orderApproved";
    }

    public static string TopicForClientOrderEnded(string clientId)
    {
        return "orderEnded" + clientId;
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

    public static string TopicForHedgingOrder()
    {
        return "HedgeOrder";
    }
    public static string TopicForHedgingOrderRequest()
    {
        return "HedgeOrderRequest";
    }
    public static string TopicForHedgingOrderResponse()
    {
        return "HedgeOrderResponse";
    }

    public static string TopicForHoldingOfClient(string clientId)
    {
        return "holdingOfClient" + clientId;
    }

    public static string TopicForDBDataOfClient(string clientId)
    {
        return "dbDataOfClient" + clientId;
    }

    public static string TopicForAllTargetPositions()
    {
        return "allTargetPositions";
    }

    public static string TopicForTargetPositionUpdate(string instrumentId)
    {
        return "targetPositionUpdate" + instrumentId;
    }
    
}