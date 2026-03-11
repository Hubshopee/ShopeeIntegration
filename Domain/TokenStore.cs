namespace Domain;

public class TokenStore
{
    public int Id { get; set; }

    public string AccessToken { get; set; } = "";

    public string RefreshToken { get; set; } = "";

    public DateTime ExpireAt { get; set; }
}