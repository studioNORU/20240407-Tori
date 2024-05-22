using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using tori.AppApi.Model;

namespace tori.AppApi;

public class ApiClient
{
    private readonly HttpClient client;
    private readonly JsonSerializerOptions serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ApiClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (msg, cert, chain, sslPolicyErrors) =>
            {
                if (sslPolicyErrors == SslPolicyErrors.None) return true;
                
                //NOTE: 개발용 API 서버의 인증서 문제가 있어 해당 인증서에 대해서만 인증서 관련 오류를 무시하도록 합니다.
                return cert?.GetCertHashString(HashAlgorithmName.SHA256).Equals(
                    "59a8356662f1e995978cd92bceb3282b0b5ece9c8dedb069808a219227306304",
                    StringComparison.InvariantCultureIgnoreCase) == true;
            }
        };
        
        this.client = new HttpClient(handler)
        {
            BaseAddress = new Uri(API_URL.BaseUrl)
        };
        
        this.client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string> GetAsync(string endpoint, Dictionary<string, string>? queryParams = null)
    {
        if (queryParams != null)
        {
            endpoint = QueryHelpers.AddQueryString(endpoint, queryParams!);
        }
        
        var res = await this.client.GetAsync(endpoint);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync();
    }

    public async Task<T?> GetAsync<T>(string endpoint, Dictionary<string, string>? queryParams = null) where T : class
    {
        var json = await this.GetAsync(endpoint, queryParams);
        var res = JsonSerializer.Deserialize<ApiResponse<T>>(json, this.serializerOptions);
        return res?.Info;
    }

    public async Task<string> PostAsync(string endpoint, HttpContent content)
    {
        var res = await this.client.PostAsync(endpoint, content);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync();
    }
    
    public async Task<T?> PostAsync<T>(string endpoint, HttpContent content) where T : class
    {
        var json = await this.PostAsync(endpoint, content);
        var res = JsonSerializer.Deserialize<ApiResponse<T>>(json, this.serializerOptions);
        return res?.Info;
    }
}