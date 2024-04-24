namespace tori.Core;

public enum ResultCode
{
    InvalidParameter = -2,
    UnhandledError = -1,
    Ok,
    
    AlreadyJoined,
    SessionNotFound,
    NotJoinedUser,
}