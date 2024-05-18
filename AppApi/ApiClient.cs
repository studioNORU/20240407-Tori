using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using tori.AppApi.Model;

namespace tori.AppApi;

public class ApiClient
{
    private readonly HttpClient client;
    private readonly JsonSerializerOptions defaultJsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private readonly Dictionary<Type, JsonSerializerOptions> jsonSerializerOptions = new()
    {
        {
            typeof(GoodsInfo),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper,
                PropertyNameCaseInsensitive = true
            }
        },
        {
            typeof(ItemInfo),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = true
            }
        },
    };

    public ApiClient()
    {
        this.client = new HttpClient
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
        var options = this.jsonSerializerOptions.GetValueOrDefault(typeof(T)) ?? this.defaultJsonSerializerOptions;

        var res = JsonSerializer.Deserialize<ApiResponse<T>>(json, options);
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
        var options = this.jsonSerializerOptions.GetValueOrDefault(typeof(T)) ?? this.defaultJsonSerializerOptions;

        var res = JsonSerializer.Deserialize<ApiResponse<T>>(json, options);
        return res?.Info;
    }
}