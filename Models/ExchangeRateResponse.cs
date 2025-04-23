using Newtonsoft.Json;

public class ExchangeRateResponse
{
    [JsonProperty("rates")]
    public Dictionary<string, double>? Rates { get; set; }
}