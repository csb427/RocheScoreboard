namespace RocheScoreboard.Mac.Native;
public static class NativeQr
{
    public static NativeBitmap GenerateQrCode(string text)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
        using var qr = new QRCoder.BitmapByteQRCode(data);
        using var stream = new MemoryStream(qr.GetGraphic(6, [255, 255, 255], [13, 17, 23]));
        var image = new NativeBitmap { StreamSource = stream }; image.EndInit(); return image;
    }
}
