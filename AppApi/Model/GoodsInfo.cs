namespace tori.AppApi.Model;

public record GoodsInfo(
    int Price,
    string ImgUrl,
    int BrandId,
    int GoodsId,
    string BrandName,
    string GoodsName);