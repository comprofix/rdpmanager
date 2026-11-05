using System.Globalization;

namespace RDPManager.Core.Sessions;

/// <summary>Whether a session ended normally or because of a problem.</summary>
public enum DisconnectKind
{
    /// <summary>A normal end: the user or the server ended the session.</summary>
    Disconnected,

    /// <summary>The connection failed or was refused.</summary>
    Error,
}

/// <summary>
/// Classifies an RDP disconnect and produces a readable message. Codes and meanings are Microsoft's
/// (IMsTscAxEvents::OnDisconnected and ExtendedDisconnectReasonCode on Microsoft Learn). The control's own
/// "is an error" flag is not used: the RDP spike found it false even for gateway failures.
/// </summary>
public sealed record DisconnectInfo(DisconnectKind Kind, string Message, int Code, int ExtendedCode)
{
    /// <summary>Microsoft's "not an error" codes: local disconnection, remote by user, remote by server.</summary>
    private static readonly HashSet<int> NormalCodes = [1, 2, 3];

    /// <summary>Extended reasons that mean the server refused the session, so the end is an error.</summary>
    private static bool IsRefusal(int extended) =>
        extended is 6 or 7 or 8 or 9 or 10 or 768 or (>= 256 and <= 267) or (>= 4096 and <= 32767);

    private static readonly Dictionary<int, string> CodeMessages = new()
    {
        [1] = "You disconnected the session.",
        [2] = "The session was disconnected from the remote computer.",
        [3] = "The remote computer ended the session.",
        [260] = "The computer's name could not be found (DNS lookup failed).",
        [1288] = "The computer's name could not be found (DNS lookup failed).",
        [520] = "The remote computer could not be found.",
        [264] = "The connection timed out.",
        [1796] = "The connection timed out.",
        [516] = "The remote computer could not be reached.",
        [2308] = "The connection was closed.",
        [2052] = "The IP address is not valid.",
        [776] = "The IP address is not valid.",
        [2056] = "Licence negotiation with the remote computer failed.",
        [2312] = "Licensing timed out.",
        [2055] = "The logon failed. Check the username and password.",
        [2567] = "The user account does not exist.",
        [2823] = "The user account is disabled.",
        [3591] = "The user account has expired.",
        [3335] = "The user account is locked out.",
        [3079] = "The user account is restricted.",
        [3847] = "The password has expired.",
        [4615] = "The password must be changed before logging on.",
        [6151] = "No authority could be contacted for authentication. Check the domain name.",
        [6919] = "The remote computer's certificate has expired.",
        [8455] = "The server requires credentials to be entered again.",
    };

    private static readonly Dictionary<int, string> ExtendedMessages = new()
    {
        [2] = "The session was logged off.",
        [3] = "The server ended the session because it was idle for too long.",
        [4] = "The server ended the session because logging on took too long.",
        [5] = "The session was taken over by another connection to the remote computer.",
        [7] = "The remote computer denied the connection.",
        [8] = "The remote computer denied the connection for security reasons.",
        [9] = "The remote computer denied the connection for security reasons.",
        [10] = "The remote computer requires credentials to be entered again.",
        [11] = "The session was disconnected by user activity.",
        [12] = "The user logged off.",
        [257] = "No licence server was available.",
        [258] = "No valid licence was available.",
        [266] = "The remote computer is not licensed to accept remote connections.",
        [768] = "The credentials were not valid.",
    };

    /// <param name="code">The disconnect reason code.</param>
    /// <param name="extendedCode">The extended disconnect reason code.</param>
    /// <param name="controlDescription">The RDP control's own description, which may be empty.</param>
    public static DisconnectInfo Classify(int code, int extendedCode, string? controlDescription)
    {
        var kind = NormalCodes.Contains(code) && !IsRefusal(extendedCode) ? DisconnectKind.Disconnected : DisconnectKind.Error;

        var message = !string.IsNullOrWhiteSpace(controlDescription) ? controlDescription.Trim()
            : ExtendedMessages.TryGetValue(extendedCode, out var extendedText) ? extendedText
            : CodeMessages.TryGetValue(code, out var codeText) ? codeText
            : kind == DisconnectKind.Error ? "The connection failed." : "The session ended.";

        return new DisconnectInfo(kind, message, code, extendedCode);
    }

    /// <summary>Technical details for "Show details", for example "Code 0x3000008 (50331656), extended 0".</summary>
    public string Details => string.Create(CultureInfo.InvariantCulture, $"Code 0x{Code:X} ({Code}), extended {ExtendedCode}");
}
