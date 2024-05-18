using Tori.Controllers.Data;

namespace tori.AppApi.Model;

public record GoodsInfo(
    int Price,
    string ImgUrl,
    int BrandId,
    string GoodsId,
    string BrandName,
    string GoodsName)
{
    public GameReward ToReward() => new()
    {
        Price = this.Price,
        BrandId = this.BrandId,
        GoodsId = this.GoodsId,
        BrandName = this.BrandName,
        GoodsName = this.GoodsName,
        RewardImage = this.ImgUrl
    };
}