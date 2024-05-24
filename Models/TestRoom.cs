using System.ComponentModel.DataAnnotations;
using tori.AppApi.Model;

namespace tori.Models;

#if !RELEASE
public record TestRoomInfo(
        int RoomId,
        DateTime BeginRunningTime,
        DateTime EndRunningTime)
    : RoomInfo(
        RoomId,
        50,
        new GoodsInfo(
            1000,
            "https://image.gift-n.net/goods/0000001853_1.jpg",
            83,
            "0000001853",
            "기프트레터 상품권",
            "[기프트레터] 상품권 1천원"),
        BeginRunningTime,
        EndRunningTime);

public class TestRoom
{
    [Key]
    public int RoomId { get; set; }
    
    public DateTime BeginRunningTime { get; set; }
    
    public DateTime EndRunningTime { get; set; }
    
    public DateTime ExpireAt { get; set; }

    public TestRoomInfo ToRoomInfo() => new(
        this.RoomId,
        this.BeginRunningTime,
        this.EndRunningTime);
}
#endif