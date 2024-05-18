namespace tori.AppApi.Model;

public record ApiResponse<T>(string Result, T Info) where T : class;