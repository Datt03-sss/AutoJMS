using System;

namespace AutoJMS;

public interface IAuthStateService
{
    string AuthToken { get; }
    bool IsAuthenticated { get; }
    event Action<string> TokenAcquired;
    void SetToken(string token);
    void ClearToken();
}
