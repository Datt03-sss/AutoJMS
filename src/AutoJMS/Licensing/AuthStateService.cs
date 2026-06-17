using System;

namespace AutoJMS;

public sealed class AuthStateService : IAuthStateService
{
    public static readonly AuthStateService Instance = new();

    public string AuthToken { get; private set; } = string.Empty;
    public bool IsAuthenticated => !string.IsNullOrEmpty(AuthToken) && AuthToken.Length > 20;

    public event Action<string> TokenAcquired;

    public void SetToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        AuthToken = token;
        TokenAcquired?.Invoke(token);
    }

    public void ClearToken()
    {
        AuthToken = string.Empty;
    }

    private AuthStateService() { }
}
