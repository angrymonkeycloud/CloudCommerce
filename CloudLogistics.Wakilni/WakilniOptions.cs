namespace AngryMonkey.CloudLogistics.Wakilni;

public sealed class WakilniOptions
{
    public static readonly Uri ProductionAddress = new("https://api.wakilni.com/");
    public static readonly Uri TestingAddress = new("https://api-dev.wakilni.com/");
    public static readonly Uri JordanAddress = new("https://jordan-api.wakilni.com/");

    /// <summary>Created in the Wakilni back office under Settings, Api Tokens, Link to your custom app.</summary>
    public string Key { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public Uri ApiBaseAddress { get; set; } = ProductionAddress;

    /// <summary>The id of the pickup location in your own system. Wakilni requires one for every pickup.</summary>
    public long PickupLocationId { get; set; } = 1;

    /// <summary>Package type for each item: 57 paper, 58 regular box, 59 small box, 60 large box, 61 very large box.</summary>
    public int PackageTypeId { get; set; } = WakilniPackageTypes.RegularBox;

    /// <summary>Asks Wakilni for the barcode label of each delivery and returns it as a PNG shipment document.</summary>
    public bool RequestLabels { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key) || string.IsNullOrWhiteSpace(Secret))
            throw new InvalidOperationException("The Wakilni key and secret are required.");
    }
}

public static class WakilniPackageTypes
{
    public const int Paper = 57;
    public const int RegularBox = 58;
    public const int SmallBox = 59;
    public const int LargeBox = 60;
    public const int VeryLargeBox = 61;
}

/// <summary>Service codes: a bulk delivery is the default; express is offered to accounts Wakilni allows it for.</summary>
public static class WakilniServices
{
    public const string Bulk = "bulk";
    public const string Express = "express";
}

/// <summary>Keys a <see cref="ShipmentRequest"/> can carry in its destination metadata.</summary>
public static class WakilniMetadata
{
    public const string ReceiverId = "wakilni.receiverId";
    public const string ReceiverLocationId = "wakilni.receiverLocationId";
    public const string Floor = "wakilni.floor";
    public const string Building = "wakilni.building";

    /// <summary>Package type for every item of the delivery, one of <see cref="WakilniPackageTypes"/>; the configured default otherwise.</summary>
    public const string PackageType = "wakilni.packageType";

    /// <summary>"true" asks for a car instead of a motorbike.</summary>
    public const string CarNeeded = "wakilni.carNeeded";

    public const string RequireSignature = "wakilni.requireSignature";
    public const string RequirePicture = "wakilni.requirePicture";

    /// <summary>"true" lets the recipient try the items on before accepting them.</summary>
    public const string TryOn = "wakilni.tryOn";

    /// <summary>"true" when the driver also brings an item back from the recipient.</summary>
    public const string Exchange = "wakilni.exchange";

    public const string StatusCode = "wakilni.statusCode";
    public const string Status = "wakilni.status";
    public const string Comments = "wakilni.comments";
}
