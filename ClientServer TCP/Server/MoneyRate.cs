using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClientServerTCP
{
    internal class MoneyRate
    {
        public double RateDollar { get; set; }
        public double RateEuro { get; set; }


        public async Task SetRateUSD()
        {
            var json = await ReadJsonFileAsync("USD");

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            JsonElement firstElement = root[0];

            RateDollar = firstElement.GetProperty("rate").GetDouble();
        }


        public async Task SetRateEUR()
        {
            var json = await ReadJsonFileAsync("EUR");

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            JsonElement firstElement = root[0];
            RateEuro = firstElement.GetProperty("rate").GetDouble();
        }


        private async Task<string> ReadJsonFileAsync(string USD)
        {
            var CurrentDate = DateTime.Now.ToString("yyyyMMdd");

            var UrlRequest = "https://bank.gov.ua/NBUStatService/v1/statdirectory/exchange?valcode=" + USD + "&date=" + CurrentDate + "&json";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage response = await client.GetAsync(UrlRequest);
                    response.EnsureSuccessStatusCode();
                    string jsonContent = await response.Content.ReadAsStringAsync();
                    return jsonContent;
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при чтении данных: {ex.Message}\n{ex.StackTrace}");
                return "";
            }
        }


    }
}
