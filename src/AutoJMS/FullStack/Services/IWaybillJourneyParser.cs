using AutoJMS.FullStack.Models;

namespace AutoJMS.FullStack.Services
{
    public interface IWaybillJourneyParser
    {
        WaybillJourneyViewModel ParseDetails(string waybillNo, string rawJson);
        WaybillJourneyParseMetadata ReadMetadata(string rawJson);
    }
}
