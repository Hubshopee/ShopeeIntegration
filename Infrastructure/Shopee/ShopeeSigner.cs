using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Shopee;

public static class ShopeeSigner
{
    public static string Generate(string partnerKey, string baseString)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(partnerKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(baseString));
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }
}
