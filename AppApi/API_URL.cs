using DotNetEnv;

namespace tori.AppApi;

// ReSharper disable once InconsistentNaming
public struct API_URL
{
    public static readonly string BaseUrl = Env.GetString("APP_API_HOST");

    public static readonly string RoomInfo = "/gs/game/room";
    public static readonly string UserInfo = "/gs/game/user";
    public static readonly string UserStatus = "/gs/game/status";
    public static readonly string Result = "/gs/game/result";
}