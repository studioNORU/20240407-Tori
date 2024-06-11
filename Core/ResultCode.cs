namespace tori.Core;

public enum ResultCode
{
    InvalidParameter = -2,
    UnhandledError = -1,
    Ok,
    
    AlreadyJoined,
    SessionNotFound,
    NotJoinedUser,
    
    CannotJoinToStartedGame = 100,
    CannotJoinToEndedGame,
    CannotJoinToFullRoom,
    CannotJoinToRoomBeforePreload,
    DataNotFound,
    CannotUseBothNormalTest,
    CanResumeGame,
}